using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class RandomTextureCreator : MonoBehaviour
{
    public string materialPath;
    public int widht = 128;
    public int height = 128;

    private Texture2D noiseTexture;
    Material randomMaterial;
    Renderer insectRenderer;
    // Start is called before the first frame update
    void Start()
    {
        if (materialPath.Equals(""))
        {
            Debug.LogError("materialPath variable was not insert");
        }

        noiseTexture = CreateRandomTexture();

        //Adding material to insect
        Transform insectMeshObject = transform.GetChild(0);
        insectRenderer = insectMeshObject.GetComponent<Renderer>();
        insectRenderer.material = randomMaterial;
        randomMaterial.mainTexture = noiseTexture;
        Debug.Log(insectMeshObject.name);
    }

    public void SetMaterial()
    {

    }

    private Texture2D CreateRandomTexture()
    {
        randomMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        //Create a new Texture 2d With the specified width and height
        Texture2D randomTexture = new Texture2D(widht, height);

        //Generate random pixel values for the texture
        Color[] pixels = new Color[widht * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            float r = Random.value;
            float g = Random.value;
            float b = Random.value;

            pixels[i] = new Color(r, g, b);
        }

        //Set the pixels of the texture and appy the changes
        randomTexture.SetPixels(pixels);
        randomTexture.Apply();

        return randomTexture;
    }
}
