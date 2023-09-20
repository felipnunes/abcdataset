using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

public class PlaneGenerator : MonoBehaviour
{
    Texture2D [] groundTextures; 
    // Start is called before the first frame update
    void Start()
    {
        groundTextures = Resources.LoadAll<Texture2D>("GroundTextures");
    }
    public void RandomizeTexture()
    {
        Renderer renderer = gameObject.GetComponent<Renderer>();
        Material planeMaterial = renderer.material;
        planeMaterial.mainTexture = groundTextures[UnityEngine.Random.Range(0, groundTextures.Length)];

    }
}

    
