using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System; 

public class InsectImport : MonoBehaviour
{
    string[] rawFileNames;
    string[] fileNames;

    void Start()
    {
        
        rawFileNames = System.IO.Directory.GetFiles("Assets/Resources");
        fileNames = ObjectNameFilter(rawFileNames);

        //Instanciate a new object by file name
        var insect = Resources.Load<GameObject>(fileNames[UnityEngine.Random.Range(0, fileNames.Length)]);
        Instantiate(insect);

    }

    //Returns .obj names array without the extention ".obj" and removes .meta files from a given array.
    string[] ObjectNameFilter(string[] fileNames)
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

    
}