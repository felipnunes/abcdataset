using UnityEngine;

public class PlaneGenerator : MonoBehaviour
{
    Texture2D[] groundTextures;

    [HideInInspector] public string lastTextureName = "";

    void Start()
    {
        groundTextures = Resources.LoadAll<Texture2D>("GroundTextures");
    }

    public void RandomizeTexture()
    {
        if (groundTextures == null || groundTextures.Length == 0) return;
        Renderer renderer = gameObject.GetComponent<Renderer>();
        Material planeMaterial = renderer.material;
        int idx = UnityEngine.Random.Range(0, groundTextures.Length);
        planeMaterial.mainTexture = groundTextures[idx];
        lastTextureName = groundTextures[idx].name;
    }
}
