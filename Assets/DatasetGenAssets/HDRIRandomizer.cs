using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class HDRIRandomizer : MonoBehaviour
{
    VolumeProfile volumeComponent;
    UnityEngine.Rendering.HighDefinition.HDRISky hdriSkyComponent;
    Cubemap[] skyList;

    [HideInInspector] public string lastSkyName = "";

    // Obs.: o RNG não é mais re-semeado aqui (a seed é definida pelo DatasetGenerator).
    void Start()
    {
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
        if (skyList == null || skyList.Length == 0) return;
        int randomFilePosition = Random.Range(0, skyList.Length);
        hdriSkyComponent.hdriSky.Override(skyList[randomFilePosition]);
        lastSkyName = skyList[randomFilePosition].name;
    }
}
