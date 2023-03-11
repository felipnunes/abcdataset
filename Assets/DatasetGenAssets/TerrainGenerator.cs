using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    private Texture2D noiseTexture;
    public int width = 256;
    public int height = 256;
    public float scale = 10;
    Color[] colors;
    Terrain terrain;
    // Start is called before the first frame update
    void Start()
    {
        terrain = GetComponent<Terrain>();
        Random.InitState((int)Time.time);
        terrain.terrainData.SetHeights(0, 0, GenerateNoise());
    }

    private float[,] GenerateNoise()
    {
        float[,] noise = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float xCoord = ((float)x / width) * scale;
                float yCoord = ((float)y / height) * scale;
                noise[x, y] = Mathf.PerlinNoise(xCoord * 0.4f, yCoord * 0.4f);
            }
        }
        return noise;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
