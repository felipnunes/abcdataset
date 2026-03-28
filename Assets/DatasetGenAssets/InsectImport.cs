using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEditor;
using Dummiesman;
using GaussianSplatting.Runtime;


public class InsectImport : MonoBehaviour
{
    //3D models files directory
    DirectoryInfo meshModelsDir = new DirectoryInfo("C:\\Users\\felip\\Documents\\Mestrado\\Insetos\\ModelosTratados");
    FileInfo[] meshModelsFileInfo;

    //DirectoryInfo splatModelsDir = new DirectoryInfo("Assets\\GaussianAssets");               //usar para instanciar objetos de fora da pasta assets
    string splatModelsDirString;
    DirectoryInfo splatModelsDir;  //only works for folders inside "assets"



    FileInfo[] splatModelsFileInfo;

    public GaussianSplatRenderer splatRenderer;

    public string[] meshModelNames;
    public string[] splatModelNames;
    string[] textureNames;

    public Material randomMaterial;
    public string texturesPath;

    private void Awake()
    {

        splatModelsDirString = "Assets/GaussianAssets";
        splatModelsDir = new DirectoryInfo(splatModelsDirString); //only works for folders inside "assets"



        splatRenderer = this.gameObject.GetComponentInChildren<GaussianSplatRenderer>();


        UnityEngine.Random.InitState((int)System.DateTime.Now.Ticks);

        var texturefiles = Resources.LoadAll("InsectTextures", typeof(Texture2D));

        if (texturefiles != null && texturefiles.Length > 0)
        {

            this.textureNames = new string[texturefiles.Length];

            for (int i = 0; i < texturefiles.Length; i++)
            {
                this.textureNames[i] = texturefiles[i].name;
            }
        }



        //find all files on meshModelsDirectory and create a sting[] containing it's names
        meshModelsFileInfo = meshModelsDir.GetFiles("*.*");
        meshModelNames = new string[meshModelsFileInfo.Length];
        for (int i = 0; i < meshModelsFileInfo.Length; i++)
        {
            meshModelNames[i] = meshModelsFileInfo[i].FullName;
        }

        //find all .asset files on splatModelsDirectory and create a string[] containing it's names
        splatModelsFileInfo = splatModelsDir.GetFiles("*.asset");
        splatModelNames = new string[splatModelsFileInfo.Length];
        for (int i = 0; i < splatModelsFileInfo.Length; i++)
        {
            splatModelNames[i] = splatModelsFileInfo[i].Name;
        }



        if (texturesPath.Equals(""))
        {
            Debug.LogError("TexturesPath variable was not insert");
        }

        //if (randomMaterial == null)
        //{
        //   Debug.LogError("RandomMaterial variable is null. Check if MaterialPath is correct on inspector");
        //}

    }

    void Start()
    {
      
    }

    


    //Instantiate a random model in Resources path.
    public void InstantiateRandomMeshModel()
    {

        GameObject modelToInstatiate = new OBJLoader().Load(meshModelNames[UnityEngine.Random.Range(0, meshModelsFileInfo.Length)]);


        modelToInstatiate.transform.position = new Vector3(modelToInstatiate.transform.position.x, 0.5f, modelToInstatiate.transform.position.z);
        modelToInstatiate.tag = "Model";
        AddMaterial(modelToInstatiate);

        Physics.SyncTransforms();
        GetComponent<DatasetGenerator>().actualModel = modelToInstatiate;
    }

    public void InstantiateMeshModel(string modelName)
    {
        foreach (string modelFileName in meshModelNames)
        {
            if (modelName.Equals(modelFileName))
            {
                GameObject model = new OBJLoader().Load(modelName);
                model.transform.position = new Vector3(model.transform.position.x, 0.5f, model.transform.position.z);
                model.tag = "Model";
                AddMaterial(model);

                Physics.SyncTransforms();
                GetComponent<DatasetGenerator>().actualModel = model;

                break;
            }
        }
        
    }

    public void InstantiateRandomSplatModel()
    {

        int model = UnityEngine.Random.Range(0, splatModelNames.Length);

        splatRenderer.m_Asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(Path.Combine(splatModelsDirString, splatModelNames[model]));
        GetComponent<DatasetGenerator>().actualModel = splatRenderer.gameObject;
        splatRenderer.gameObject.tag = "Model";
    }

    //Finds the current instantiated model and destroy it.
    public void destroyActualModel()
    {
        if(!GetComponent<DatasetGenerator>().splatMode)
        {
            //Checks if there is a mesh model instantiated before trying to destroying it
            if (GameObject.FindGameObjectWithTag("Model") != null)
            {
                GameObject actualModel = GameObject.FindGameObjectWithTag("Model");

                Destroy(actualModel);
            }
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