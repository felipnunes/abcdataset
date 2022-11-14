using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System; 

public class InsectImport : MonoBehaviour
{
    string[] rawModelFileNames;
    string[] modelFileNames;

    void Start()
    {      
        rawModelFileNames = System.IO.Directory.GetFiles("Assets/Resources");
        modelFileNames = ObjectNameFilter(rawModelFileNames);
        InstantiateRandomModel();
    }

    /*Returns .obj names array without the extention ".obj" and removes .meta files from a given array.*/
    private string[] ObjectNameFilter(string[] fileNames)
    {
        string[] filteredFileNames = new string[fileNames.Length / 2];

        for (int i = 0, j = 0; i < fileNames.Length; i++)
        {
            if (i % 2 == 0)
            {
                filteredFileNames[j] = fileNames[i];
                filteredFileNames[j] = Path.GetFileNameWithoutExtension(fileNames[i]);

                j++;
            }
        }
        return filteredFileNames;
    }

    public void InstantiateRandomModel()
    {
        if (GameObject.FindGameObjectWithTag("Model") != null)
        {
            GameObject actualModel = GameObject.FindGameObjectWithTag("Model");
            Destroy(actualModel);
        }
        else
        {
            GameObject insect = Resources.Load<GameObject>(modelFileNames[UnityEngine.Random.Range(0, modelFileNames.Length)]);
            insect.tag = "Model";
            Instantiate(insect);
        }


    }



    
}