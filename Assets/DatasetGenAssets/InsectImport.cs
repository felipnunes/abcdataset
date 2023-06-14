using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEditor;


public class InsectImport : MonoBehaviour
{
    
    public string[] modelNames;
    string[] textureNames;

    public Material randomMaterial;
    public string texturesPath;

    void Start()
    {

        var texturefiles = Resources.LoadAll("InsectTextures", typeof(Texture2D));

        if (texturefiles != null && texturefiles.Length > 0)
        {

            this.textureNames = new string[texturefiles.Length];

            for (int i = 0; i < texturefiles.Length; i++)
            {
                this.textureNames[i] = texturefiles[i].name;
            }
        }



        var modelNames = Resources.LoadAll("Models", typeof(GameObject));

        if (modelNames != null && modelNames.Length > 0)
        {

            this.modelNames = new string[modelNames.Length];

            for (int i = 0; i < modelNames.Length; i++)
            {
                this.modelNames[i] = modelNames[i].name;
            }
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

        GameObject insect = Resources.Load<GameObject>("Models/" + modelNames[UnityEngine.Random.Range(0, modelNames.Length)]);
        insect.transform.position = new Vector3(insect.transform.position.x, 0.5f, insect.transform.position.z);
        insect.tag = "Model";
        AddMaterial(insect);
        Instantiate(insect);

    }

    public void InstantiateModel(string modelName)
    {
        foreach (string modelFileName in modelNames)
        {
            if (modelName.Equals(modelFileName))
            {
                GameObject model = Resources.Load<GameObject>("Models/" + modelName);
                model.transform.position = new Vector3(model.transform.position.x, 0.5f, model.transform.position.z);
                model.tag = "Model";
                AddMaterial(model);
                Instantiate(model);
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