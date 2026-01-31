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

    Vector2 boundingBoxAnchoredPositionPixel;
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


        //Aplly calculated transformations on UI for visualization (Only visual inpact)
        boundingBoxTransform.anchoredPosition = new Vector2(min_x, min_y); // set position
        boundingBoxTransform.sizeDelta = new Vector2(width, height);      // set length

        datasetGen.actualModelBoundingBox = new Vector4 (min_x,min_y,width,height); // Actualize datasetgeneration component value to actual bounding box values

        Debug.Log("boundingBox em pixels: " + datasetGen.actualModelBoundingBox);
        Debug.Log("bounding box yolo format: " + GetYOLOFormat(datasetGen.actualModelBoundingBox, 0));

    }


    //Transforms the 8 world-space corners of the model's bounds into screen-space pixel coordinates related to the shaded camera.
    public List<Vector3> CameraBoundsVec3()
    {
        Vector3[] actualModelBoundsVec3 = BoundsToVec3(GetActualModelBounds());
        List<Vector3> actualModelCameraBoundsVec3 = new List<Vector3>();

        foreach (Vector3 point in actualModelBoundsVec3)
        {
            actualModelCameraBoundsVec3.Add(datasetGen.cameraShaded.WorldToScreenPoint(point));
        }

        return actualModelCameraBoundsVec3;
    }


    public Bounds GetActualModelBounds()
    {
        Debug.Log("A variavel actualModel tem valor: " + datasetGen.actualModel.name);
        return datasetGen.actualModel.GetComponentInChildren<MeshRenderer>().bounds;

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
    public string GetYOLOFormat(Vector4 box, int classId)
    {
        //Gets shaded camera width and height
        float targetW = datasetGen.cameraShaded.targetTexture.width;
        float targetH = datasetGen.cameraShaded.targetTexture.height;

        //Normalize values based on shaded camera lenght ()
        float x_center = (box.x + (box.z / 2f)) / targetW;
        float y_center = 1.0f - ((box.y + (box.w / 2f)) / targetH); //1.0f is necessary because YOLO format considers y = 0 as upLeft on image while unity uses bottomLeft
        float w = box.z / targetW;
        float h = box.w / targetH;

        return classId.ToString() + " " + x_center.ToString("F6") + " " + y_center.ToString("F6") + " " + w.ToString("F6") + " " + h.ToString("F6");
    }


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
