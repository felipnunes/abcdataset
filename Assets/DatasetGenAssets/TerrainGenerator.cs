using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    private Texture2D noiseTexture;
    public static int width = 256;
    public static int height = 256;
    public static float scale = 10;
    Terrain terrain;
    Texture2D[] groundTextureFiles;

    // Start is called before the first frame update
    void Start()
    {
        terrain = GetComponent<Terrain>();
        
        terrain.terrainData.SetHeights(0, 0, GenerateNoise());

        groundTextureFiles = Resources.LoadAll<Texture2D>("GroundTextures");
    }

    public static float[,] GenerateNoise()
    {
        //x and y offsets allow us to "navegate" into the perlin noise texture, like scrolling, witch means we can have a diferent terrain for each function call
        float x_offset = Random.Range(0, 10000);
        float y_offset = Random.Range(0, 10000);

        Random.InitState((int)Time.time);

        float[,] noise = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float xCoord = ((float)x / width) * scale + x_offset;
                float yCoord = ((float)y / height) * scale + y_offset;
                noise[x, y] = Mathf.PerlinNoise(xCoord, yCoord);
            }
        }
        return noise;
    }

    public void RandomizeTexture()
    {
        Material terrainMaterial = terrain.materialTemplate;
        terrainMaterial.mainTexture = groundTextureFiles[Random.Range(0,groundTextureFiles.Length)];
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
