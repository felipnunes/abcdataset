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
}

//This class Creates and manege logs for machine learning training labeling
public class Logger : MonoBehaviour
{
    public bool isLogging = true;
    private static DatasetGenerator datasetGen;









    void Start()
    {
        datasetGen = GetComponent<DatasetGenerator>();
        if (datasetGen == null) {
            Debug.Log("logger requires a dataset generator component");
            isLogging = false;
        }

    }


    Vector4 CalculatesBoundingBox2DPoints()
    {
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

        return new Vector4(min_x,min_y,width,height);
    }


    // Transforms the 8 world-space corners of the model's bounds into screen-space pixel coordinates related to the shaded camera.
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


    static Bounds GetActualModelBounds()
    {
        return datasetGen.actualModel.GetComponent<MeshRenderer>().bounds;

    }

    public Vector3[] BoundsToVec3(Bounds b)
    {
        Vector3[] corners = new Vector3[8];

        // The 8 corners combining Min and Max of each axis (X, Y, Z)
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



    public void LogSample(string fileName, string label, Transform cameraPose){

        if (!isLogging)
            return;

        var sample = new Sample();
        sample.fileName = fileName;
        sample.label = label;
        sample.cameraPose.position = cameraPose.position;
        sample.cameraPose.rotation = cameraPose.rotation;
      
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
