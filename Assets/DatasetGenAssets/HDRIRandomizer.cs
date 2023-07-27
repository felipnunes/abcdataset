using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;

public class HDRIRandomizer : MonoBehaviour
{
    VolumeProfile volumeComponent;
    UnityEngine.Rendering.HighDefinition.HDRISky hdriSkyComponent;
    Cubemap [] skyList;
    // Start is called before the first frame update
    void Start()
    {
        skyList = Resources.LoadAll<Cubemap>("HDRISkys");


        volumeComponent = this.GetComponent<Volume>().sharedProfile;

        if (!volumeComponent.TryGet<HDRISky>(out hdriSkyComponent))
        {
            hdriSkyComponent = volumeComponent.Add<HDRISky>(false);
        }
        if (hdriSkyComponent == null)
        {
            Debug.Log("hdrSkyComponent = NULL");
        }
        else
        {
            Debug.Log("hdrSkyComponent = " + hdriSkyComponent);
        }

        //hdriSky.hdriSky.Override(skyList[Random.Range(0, skyList.Length - 1)]);
    }

    public void RandomizeHDRISky()
    {
        hdriSkyComponent.hdriSky.Override(skyList[Random.Range(0, skyList.Length)]);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
