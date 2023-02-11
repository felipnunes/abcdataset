using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEditor;
using System.Net.Configuration;

public class InsectImport : MonoBehaviour
{
    string[] rawModelFileNames;
    string[] modelFileNames;

    Material randomMaterial;
    public string materialPath;

    [SerializeField]
    private int textureWidht = 3;
    [SerializeField]
    private int textureHeight = 3;

    void Start()
    {      
        rawModelFileNames = System.IO.Directory.GetFiles("Assets/Resources");
        modelFileNames = ObjectNameFilter(rawModelFileNames);

        //Check if materialPath was written on spector 
        if (materialPath.Equals(""))
        {
            Debug.LogError("MaterialPath variable was not insert");
        }

        //Instantiate randomMaterial material from path
        randomMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (randomMaterial == null)
        {
            Debug.LogError("RandomMaterial variable is null. Check if MaterialPath is correct on inspector");
        }

        InstantiateRandomModel();
    }

    //Returns .obj names array without the extention ".obj" and removes .meta files from a given array.
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

    //Instantiate a random model in Resources path.
    public void InstantiateRandomModel()
    {
       
        GameObject insect = Resources.Load<GameObject>(modelFileNames[UnityEngine.Random.Range(0, modelFileNames.Length)]);
        insect.transform.position = new Vector3(insect.transform.position.x, 0.5f, insect.transform.position.z);
        insect.tag = "Model";
        AddMaterial(insect);
        Instantiate(insect);

    }

    //Finds the current instantiated model and destroy it.
    public void destroyActualModel()
    {
        if (GameObject.FindGameObjectWithTag("Model") != null)
        {
            GameObject actualModel = GameObject.FindGameObjectWithTag("Model");
            Destroy(actualModel);
        }
    }


    //Add a material and aply the random texture to the new model.
    public void AddMaterial(GameObject model)
    {
        
        

        Renderer modelRenderer;
        GameObject insectMeshObject;


        for (int j = 0; j < model.transform.childCount; j++)
        {
            insectMeshObject = model.transform.GetChild(j).gameObject;

            //Creates a new Texture 2d With the specified width and height
            Texture2D randomTexture = new Texture2D(textureWidht, textureHeight);

            //Generate random pixel values for the texture
            Color[] pixels = new Color[textureWidht * textureHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                float r = UnityEngine.Random.value;
                float g = UnityEngine.Random.value;
                float b = UnityEngine.Random.value;

                pixels[i] = new Color(r, g, b);
            }

            //Set the pixels of the texture and appy the changes
            randomTexture.SetPixels(pixels);
            randomTexture.Apply();

            //Adding material to insect model
            modelRenderer = insectMeshObject.GetComponent<Renderer>();
            modelRenderer.material = randomMaterial;
            randomMaterial.mainTexture = randomTexture;

        }
    }
}