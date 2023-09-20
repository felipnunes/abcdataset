using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEditor;
using Dummiesman;


public class InsectImport : MonoBehaviour
{
    //3D models files directory
    DirectoryInfo modelsDirectory = new DirectoryInfo("C:\\Users\\felip\\Documents\\IC_Projeto\\ModelosTratados");
    FileInfo[] modelsFileInfo;

    public string[] modelNames;
    string[] textureNames;

    public Material randomMaterial;
    public string texturesPath;

    void Start()
    { 
        UnityEngine.Random.InitState(RandomSeedCreator.CreateRandomSeed());

        var texturefiles = Resources.LoadAll("InsectTextures", typeof(Texture2D));

        if (texturefiles != null && texturefiles.Length > 0)
        {

            this.textureNames = new string[texturefiles.Length];

            for (int i = 0; i < texturefiles.Length; i++)
            {
                this.textureNames[i] = texturefiles[i].name;
            }
        }



        //find all files on modelsDirectory and create a sting[] containing it's names
        modelsFileInfo = modelsDirectory.GetFiles("*.*");
        modelNames = new string[modelsFileInfo.Length];
        for(int i = 0; i < modelsFileInfo.Length; i++)
        {
            modelNames[i] = modelsFileInfo[i].FullName;
        }
        
        

        

        if (texturesPath.Equals(""))
        {
            Debug.LogError("TexturesPath variable was not insert");
        }

        //if (randomMaterial == null)
        //{
         //   Debug.LogError("RandomMaterial variable is null. Check if MaterialPath is correct on inspector");
        //}

        InstantiateRandomModel();
    }

    //Instantiate a random model in Resources path.
    public void InstantiateRandomModel()
    {

        GameObject modelToInstatiate = new OBJLoader().Load(modelNames[UnityEngine.Random.Range(0, modelsFileInfo.Length)]);

        modelToInstatiate.transform.position = new Vector3(modelToInstatiate.transform.position.x, 0.5f, modelToInstatiate.transform.position.z);
        modelToInstatiate.tag = "Model";
        AddMaterial(modelToInstatiate);
        Debug.Log(GameObject.FindGameObjectsWithTag("Model").Length);

    }

    public void InstantiateModel(string modelName)
    {
        foreach (string modelFileName in modelNames)
        {
            if (modelName.Equals(modelFileName))
            {
                GameObject model = new OBJLoader().Load(modelName);
                model.transform.position = new Vector3(model.transform.position.x, 0.5f, model.transform.position.z);
                model.tag = "Model";
                AddMaterial(model);
                break;
            }
        }
        
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

            Texture2D texture = Resources.Load<Texture2D>("InsectTextures/" + textureNames[UnityEngine.Random.Range(0, textureNames.Length)]);

            //Adding material to insect model
            modelRenderer = insectMeshObject.GetComponent<Renderer>();
            modelRenderer.material = randomMaterial;
                   
                randomMaterial.mainTexture = texture;

            if (randomMaterial.mainTexture == null)
            {
                AddMaterial(model);
            }

        }
        
    }
}