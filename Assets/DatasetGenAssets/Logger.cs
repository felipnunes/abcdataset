using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;

[Serializable]
public struct CameraPose
{
    public Vector3 position;
    public Quaternion rotation;
}

[Serializable]
public struct Sample
{

    public string fileName;
    public string label;
    public CameraPose cameraPose;
    public Vector4 boundingBox;
}

//This class Creates and manege logs for machine learning training labeling
public class Logger : MonoBehaviour
{
    public bool isLogging = true;

    private DatasetGenerator datasetGen;

    [SerializeField]
    public GameObject boundingBox;

    RectTransform boundingBoxTransform;

    void Start()
    {
        datasetGen = GetComponent<DatasetGenerator>();
        if (datasetGen == null)
        {
            Debug.Log("logger requires a dataset generator component");
            isLogging = false;
        }

        boundingBoxTransform = boundingBox.GetComponent<RectTransform>();
        boundingBoxTransform.pivot = new Vector2(0,0); //Set pivot to up left on image (necessary for implementing YOLO format intuitively)
        boundingBoxTransform.anchorMin = new Vector2(0, 0);
        boundingBoxTransform.anchorMax = new Vector2(0, 0);
    }

    public void CalculatesBoundingBox2DPoints()
    {
        Debug.Log("CalculatesBoundingBox2DPoints() foi chamada!");

        List<Vector3> actualModelCameraBounds = CameraBoundsVec3();

        float min_x = float.MaxValue;
        float max_x = float.MinValue;
        float min_y = float.MaxValue;
        float max_y = float.MinValue;

        foreach(Vector3 point in actualModelCameraBounds)
        {
            if (point.z > 0)
            {
                if (point.x < min_x) min_x = point.x;
                if (point.x > max_x) max_x = point.x;
                if (point.y < min_y) min_y = point.y;
                if (point.y > max_y) max_y = point.y;
            }
        }

        float width = max_x - min_x;
        float height = max_y - min_y;


        //Aplly calculated transformations on UI for visualization using pixel positions before converting to YOLO format (Only visual inpact)
        boundingBoxTransform.anchoredPosition = new Vector2(min_x, min_y); // set position
        boundingBoxTransform.sizeDelta = new Vector2(width, height);      // set length

        datasetGen.actualModelBoundingBox = new Vector4 (min_x,min_y,width,height); // Actualize datasetgeneration component value to actual bounding box values (in YOLO format)

        Debug.Log("boundingBox em pixels: " + datasetGen.actualModelBoundingBox);
        Debug.Log("bounding box yolo format: " + GetYOLOFormat(datasetGen.actualModelBoundingBox, 0));

        

    }


    //Transforms the 8 world-space corners of the model's bounds into screen-space pixel coordinates related to the shaded camera.
    public List<Vector3> CameraBoundsVec3()
    {
        // 1. Pega os limites locais (-1 a 1)
        Bounds localBounds = GetActualModelBounds();
        Vector3[] localCorners = BoundsToVec3(localBounds);

        List<Vector3> screenPoints = new List<Vector3>();

        // 2. Referência do Transform do modelo que está na cena
        Transform modelTransform = datasetGen.actualModel.transform;

        foreach (Vector3 localPoint in localCorners)
        {
            // 3. TRADUÇÃO ESSENCIAL: Local -> Mundo
            Vector3 worldPoint = modelTransform.TransformPoint(localPoint);

            // 4. Projeção: Mundo -> Tela (Pixels)
            screenPoints.Add(datasetGen.cameraShaded.WorldToScreenPoint(worldPoint));
        }

        return screenPoints;
    }


    public Bounds GetActualModelBounds()
    {
        if (!gameObject.GetComponent<DatasetGenerator>().splatMode) //works for mesh models
        {
            Debug.Log("A variavel actualModel tem valor: " + datasetGen.actualModel.name);
            return datasetGen.actualModel.GetComponentInChildren<MeshRenderer>().bounds;
        }

        else //works for splat models
        {
            var gsRenderer = datasetGen.actualModel.GetComponentInChildren<GaussianSplatting.Runtime.GaussianSplatRenderer>();

            if (gsRenderer != null && gsRenderer.asset != null)
            {
                // O GaussianSplatAsset armazena min e max separadamente
                Vector3 max = gsRenderer.asset.boundsMax;
                Vector3 min = gsRenderer.asset.boundsMin;

               

                // Criamos um Bounds do Unity a partir desses pontos
                Bounds modelBounds = new Bounds();
                modelBounds.SetMinMax(min, max);

                return modelBounds;
            }

        }

        return new Bounds(); // Retorna vazio se não encontrar
    }



    public Vector3[] BoundsToVec3(Bounds b)
    {
        Vector3[] corners = new Vector3[8];

        //The 8 corners combining Min and Max of each axis (X, Y, Z)
        corners[0] = new Vector3(b.min.x, b.min.y, b.min.z); // Bottom-Left-Front
        corners[1] = new Vector3(b.min.x, b.min.y, b.max.z); // Bottom-Left-Back
        corners[2] = new Vector3(b.min.x, b.max.y, b.min.z); // Top-Left-Front
        corners[3] = new Vector3(b.min.x, b.max.y, b.max.z); // Top-Left-Back
        corners[4] = new Vector3(b.max.x, b.min.y, b.min.z); // Bottom-Right-Front
        corners[5] = new Vector3(b.max.x, b.min.y, b.max.z); // Bottom-Right-Back
        corners[6] = new Vector3(b.max.x, b.max.y, b.min.z); // Top-Right-Front
        corners[7] = new Vector3(b.max.x, b.max.y, b.max.z); // Top-Right-Back

        return corners;
    }

    //Converts CalculatesBoundingBox2DPoints() into yolo format
    public Vector4 GetYOLOFormat(Vector4 box, int classId)
    {
        //Gets shaded camera width and height
        float targetW = datasetGen.cameraShaded.targetTexture.width;
        float targetH = datasetGen.cameraShaded.targetTexture.height;

        //Normalize values based on shaded camera lenght ()
        float x_center = (box.x + (box.z / 2f)) / targetW;
        float y_center = 1.0f - ((box.y + (box.w / 2f)) / targetH); //1.0f is necessary because YOLO format considers y = 0 as upLeft on image while unity uses bottomLeft
        float w = box.z / targetW;
        float h = box.w / targetH;

        return new Vector4 (x_center, y_center, w,  h);
    }

    // Converts Vector4 to string (YOLO format)
    public string ConvertToYOLOString(Vector4 yoloBox, int classId)
    {
        //Format: <class> <x_center> <y_center> <width> <height>
        return $"{classId} {yoloBox.x:F6} {yoloBox.y:F6} {yoloBox.z:F6} {yoloBox.w:F6}";
    }


    public void CreateYOLOFile(Vector4 box, string nome)
    {
        string labelsPath = Path.Combine(datasetGen.datasetPath, "labels");

        if (!Directory.Exists(labelsPath)) Directory.CreateDirectory(labelsPath);

        string content = ConvertToYOLOString(box, 0);

        //create new .txt
        string filePath = Path.Combine(labelsPath, nome + ".txt");

        //fills txt with content var
        //close file
        File.WriteAllText(filePath, content);
    }

    //------------------------------------------------------------------------------------------------------------------------

    [ContextMenu("Gerar Dataset YOLO")]
    public void GenerateYOLODatasetFromJSON()
    {
        
        //Debug.Log();
        }
    //------------------------------------------------------------------------------------------------------------------------


    public void LogSample(string fileName, string label, Transform cameraPose, Vector4 boundingBox){

        if (!isLogging)
            return;

        var sample = new Sample();
        sample.fileName = fileName;
        sample.label = label;
        sample.cameraPose.position = cameraPose.position;
        sample.cameraPose.rotation = cameraPose.rotation;
        sample.boundingBox = boundingBox;
      
        var jsonSample = JsonUtility.ToJson(sample, true);
        var path = Path.Combine(datasetGen.datasetPath, "log.json");
        var prepend = "";
        if (!File.Exists(path))
        {
            var header = "{\n\"samples\": [\n";
            File.WriteAllText(path, header);
        }else{
            prepend = ",\n";
        }
        
        File.AppendAllText(path, prepend + jsonSample);
        
    }

    public void CloseLog(){

        if (!isLogging)
            return;

        var path = Path.Combine(datasetGen.datasetPath, "log.json");

        if (!File.Exists(path)){
            Debug.Log("Log file not found");
            return;
        }else{
            var footer = "]\n}";
            File.AppendAllText(path, footer);
        }
    }
}
