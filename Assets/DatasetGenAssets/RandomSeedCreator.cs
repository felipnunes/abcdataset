using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class RandomSeedCreator
{

    //Calculate a unique seed by using DateTime values and returns it as int value
    public static int CreateRandomSeed()
    {
        System.DateTime now = System.DateTime.Now;
        int seed = now.Hour * 3600 + now.Minute * 60 + now.Second + now.Millisecond;
        Random.InitState(seed);
        return seed;
    }

}
