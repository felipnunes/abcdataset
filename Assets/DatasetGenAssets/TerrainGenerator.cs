using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    private Texture2D noiseTexture;
    private int width = 256;
    private int height = 256;
    private float scale = 10;
    public float peakHeights = 0f;

    Terrain terrain;
    Texture2D[] groundTextureFiles;

    // Start is called before the first frame update
    void Start()
    {
        Random.InitState(RandomSeedCreator.CreateRandomSeed());

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

        Random.InitState((int)Time.time);

        float[,] noise = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float xCoord = ((float)x / width) * scale + x_offset;
                float yCoord = ((float)y / height) * scale + y_offset;
                //noise[x, y] = Mathf.PerlinNoise(xCoord, yCoord);
                noise[x, y] = Mathf.Pow(Mathf.PerlinNoise(xCoord, yCoord), peakHeights); // Usando a variável peakHeight para controlar a altura máxima dos picos
            }
        }
        return noise;
    }

    public void RandomizeTexture()
    {
        Material terrainMaterial = terrain.materialTemplate;
        int randomfilePosition = Random.Range(0, groundTextureFiles.Length);
        terrainMaterial.mainTexture = groundTextureFiles[randomfilePosition];
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
