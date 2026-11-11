using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    private int width = 256;
    private int height = 256;
    private float scale = 10;
    public float peakHeights = 1f;

    Terrain terrain;
    Texture2D[] groundTextureFiles;

    [HideInInspector] public string lastTextureName = "";

    // Obs.: o RNG não é mais re-semeado aqui. A seed é definida uma única vez pelo
    // DatasetGenerator no início da geração, o que torna o dataset reproduzível.
    void Start()
    {
        terrain = GetComponent<Terrain>();

        terrain.terrainData.SetHeights(0, 0, GenerateNoise());

        groundTextureFiles = Resources.LoadAll<Texture2D>("GroundTextures");

        RandomizeTexture();
    }

    public float[,] GenerateNoise()
    {
        //x and y offsets allow us to "navegate" into the perlin noise texture, like scrolling, witch means we can have a diferent terrain for each function call
        float x_offset = Random.Range(0, 10000);
        float y_offset = Random.Range(0, 10000);

        float[,] noise = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float xCoord = ((float)x / width) * scale + x_offset;
                float yCoord = ((float)y / height) * scale + y_offset;
                noise[x, y] = Mathf.Pow(Mathf.PerlinNoise(xCoord, yCoord), peakHeights); // peakHeights controla a altura máxima dos picos
            }
        }
        return noise;
    }

    public void RandomizeTexture()
    {
        if (groundTextureFiles == null || groundTextureFiles.Length == 0) return;
        Material terrainMaterial = terrain.materialTemplate;
        int randomfilePosition = Random.Range(0, groundTextureFiles.Length);
        terrainMaterial.mainTexture = groundTextureFiles[randomfilePosition];
        lastTextureName = groundTextureFiles[randomfilePosition].name;
    }
}
