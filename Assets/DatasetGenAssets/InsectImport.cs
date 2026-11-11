using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using System.Linq;
using UnityEditor;
using Dummiesman;
using GaussianSplatting.Runtime;


public class InsectImport : MonoBehaviour
{
    //3D models files directory
    DirectoryInfo meshModelsDir = new DirectoryInfo("C:\\Users\\felip\\Documents\\Mestrado\\Insetos\\ModelosTratados");
    FileInfo[] meshModelsFileInfo;

    string splatModelsDirString;
    DirectoryInfo splatModelsDir;  //only works for folders inside "assets"

    FileInfo[] splatModelsFileInfo;

    public GaussianSplatRenderer splatRenderer;

    public string[] meshModelNames;
    public List<string> splatModelNames;
    string[] textureNames;

    public Material randomMaterial;
    public string texturesPath;

    [Header("Bounding box dos splats")]
    [Tooltip("Pasta com os .ply que geraram os assets (mesmo nome do asset). " +
             "Usados para calcular bounding boxes justas. Pode ser a mesma pasta do SplatsAutoGenerator.")]
    public string plySourceFolder = "C:/Users/felip/Documents/Mestrado/Insetos/ArtigoSibgrapi/.ply usados para gerar os datasets do artigo/Usados (alta variacao)/fly100_RD/fly100_RD";

    // Lista completa e ordenada dos assets (não é alterada durante a geração).
    public IReadOnlyList<string> AllSplatModelNames => allSplatModelNames;
    private readonly List<string> allSplatModelNames = new List<string>();

    private void Awake()
    {
        splatModelsDirString = "Assets/GaussianAssets";
        splatModelsDir = new DirectoryInfo(splatModelsDirString); //only works for folders inside "assets"

        splatRenderer = this.gameObject.GetComponentInChildren<GaussianSplatRenderer>();

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
        if (meshModelsDir.Exists)
        {
            meshModelsFileInfo = meshModelsDir.GetFiles("*.*").OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        }
        else
        {
            meshModelsFileInfo = new FileInfo[0];
            Debug.LogWarning("Pasta de modelos de malha não encontrada: " + meshModelsDir.FullName);
        }
        meshModelNames = new string[meshModelsFileInfo.Length];
        for (int i = 0; i < meshModelsFileInfo.Length; i++)
        {
            meshModelNames[i] = meshModelsFileInfo[i].FullName;
        }

        //find all .asset files on splatModelsDirectory (ordem alfabética = ordem reproduzível)
        splatModelsFileInfo = splatModelsDir.Exists ? splatModelsDir.GetFiles("*.asset") : new FileInfo[0];
        if (splatModelNames == null) splatModelNames = new List<string>();
        splatModelNames.Clear();
        foreach (var f in splatModelsFileInfo.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            splatModelNames.Add(f.Name);
        }
        allSplatModelNames.Clear();
        allSplatModelNames.AddRange(splatModelNames);

        if (texturesPath.Equals(""))
        {
            Debug.LogError("TexturesPath variable was not insert");
        }
    }

    void Start()
    {

    }

    //Instantiate a random model in Resources path.
    public void InstantiateRandomMeshModel()
    {
        if (meshModelNames.Length == 0) { Debug.LogError("Nenhum modelo de malha encontrado."); return; }
        GameObject modelToInstatiate = new OBJLoader().Load(meshModelNames[UnityEngine.Random.Range(0, meshModelNames.Length)]);

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
        if (allSplatModelNames.Count == 0) { Debug.LogError("Nenhum asset de splat em " + splatModelsDirString); return; }
        LoadSplatModel(allSplatModelNames[UnityEngine.Random.Range(0, allSplatModelNames.Count)]);
    }

    // Carrega um asset de splat pelo nome exato do arquivo (ex.: "modelo_PipeA_Photometric_0001.asset").
    public bool LoadSplatModel(string assetFileName)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(Path.Combine(splatModelsDirString, assetFileName));
        if (asset == null)
        {
            Debug.LogError("Asset de splat não encontrado: " + assetFileName);
            return false;
        }
        splatRenderer.m_Asset = asset;
        splatRenderer.gameObject.tag = "Model";
        GetComponent<DatasetGenerator>().actualModel = splatRenderer.gameObject;
        return true;
    }

    public string CurrentSplatAssetName => (splatRenderer != null && splatRenderer.m_Asset != null) ? splatRenderer.m_Asset.name : "";

    // Procura o .ply que gerou o asset: <plySourceFolder>/<nome>.ply e, como alternativa, Assets/GaussianAssets/<nome>.ply.
    public string FindPlyForAsset(string assetName)
    {
        string baseName = Path.GetFileNameWithoutExtension(assetName);
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(plySourceFolder)) candidates.Add(Path.Combine(plySourceFolder, baseName + ".ply"));
        candidates.Add(Path.Combine(splatModelsDirString, baseName + ".ply"));
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    //Finds the current instantiated model and destroy it.
    public void destroyActualModel()
    {
        if (!GetComponent<DatasetGenerator>().splatMode)
        {
            //Checks if there is a mesh model instantiated before trying to destroying it
            GameObject actualModel = GameObject.FindGameObjectWithTag("Model");
            if (actualModel != null)
            {
                // DestroyImmediate: evita que o modelo antigo ainda seja encontrado pela tag no mesmo frame
                DestroyImmediate(actualModel);
            }
        }
    }


    //Add a material and aply the random texture to the new model.
    public void AddMaterial(GameObject model)
    {
        if (textureNames == null || textureNames.Length == 0) return;

        for (int j = 0; j < model.transform.childCount; j++)
        {
            GameObject insectMeshObject = model.transform.GetChild(j).gameObject;
            Renderer modelRenderer = insectMeshObject.GetComponent<Renderer>();
            if (modelRenderer == null) continue;

            Texture2D texture = null;
            for (int tries = 0; tries < 10 && texture == null; tries++)
                texture = Resources.Load<Texture2D>("InsectTextures/" + textureNames[UnityEngine.Random.Range(0, textureNames.Length)]);

            modelRenderer.material = randomMaterial;
            randomMaterial.mainTexture = texture;
        }
    }
}
