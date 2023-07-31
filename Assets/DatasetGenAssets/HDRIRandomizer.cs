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
        Random.InitState(RandomSeedCreator.CreateRandomSeed());
        skyList = Resources.LoadAll<Cubemap>("HDRISkys");


        volumeComponent = this.GetComponent<Volume>().sharedProfile;

        if (!volumeComponent.TryGet<HDRISky>(out hdriSkyComponent))
        {
            hdriSkyComponent = volumeComponent.Add<HDRISky>(false);
        }

        RandomizeHDRISky();
       
    }

    public void RandomizeHDRISky()
    {
        int randomFilePosition = Random.Range(0, skyList.Length);
        hdriSkyComponent.hdriSky.Override(skyList[randomFilePosition]);
        //Debug.Log(randomFilePosition);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
