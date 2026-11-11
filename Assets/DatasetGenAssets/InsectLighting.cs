using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GaussianSplatting.Runtime;

// Faz o inseto em 3DGS "conversar" com a luz da cena, já que o 3DGS guarda a iluminação da captura
// e não reage às luzes da Unity. Duas partes independentes (cada uma liga/desliga no Inspector):
//
//  1. Cor e intensidade: a cada frame, compara o inseto com o chão ao redor dele na imagem e ajusta
//     o brilho e a cor do inseto para acompanharem a cena (holofote, céu, luz geral, sem luz).
//     O ajuste é aplicado no shader GaussianComposite do pacote (única alteração fora deste script).
//
//  2. Sombra no chão: desenha esferas invisíveis nos centros dos splats que só projetam sombra.
//     A luz da cena gera uma sombra com o formato do inseto no plano ou no terreno.
//
// Para usar: adicionar este componente em qualquer GameObject da cena (por exemplo, no mesmo do
// DatasetGenerator). Não precisa mexer em nenhum outro script.
public class InsectLighting : MonoBehaviour
{
    [Header("Referências (preenchidas automaticamente se vazias)")]
    public Camera sceneCamera;                  // câmera usada para medir a cena (a shaded)
    public GaussianSplatRenderer splatRenderer; // inseto em 3DGS
    public Light sceneLight;                    // luz da cena (geral ou holofote)

    [Header("1. Cor e intensidade")]
    public bool adjustColor = true;
    [Tooltip("Quanto o brilho do inseto acompanha o brilho do chão ao redor. 0 = nada, 1 = igual.")]
    [Range(0f, 1f)] public float brightnessMatch = 0.6f;
    [Tooltip("Quanto a cor do inseto puxa para a cor do ambiente e da luz. 0 = nada, 1 = total.")]
    [Range(0f, 1f)] public float colorMatch = 0.35f;
    public float minBrightness = 0.25f;
    public float maxBrightness = 2.0f;
    [Tooltip("Largura do anel ao redor do inseto usado para medir o chão, em fração do tamanho da caixa.")]
    public float ringSize = 0.5f;

    [Header("2. Sombra no chão")]
    public bool castShadow = true;
    [Tooltip("Quantidade de esferas que formam a sombra.")]
    public int shadowPoints = 3000;
    [Tooltip("Tamanho de cada esfera, em fração do raio do inseto.")]
    public float shadowPointSize = 0.04f;

    [Header("Estado atual (somente leitura)")]
    public Color currentTint = Color.white;
    public float currentBrightness = 1f;

    // nuvem de pontos do inseto atual (centros dos splats, espaço local do asset)
    private string loadedAsset;
    private Vector3[] points;
    private float insectRadius = 1f;

    // leitura da imagem
    private const int kReadSize = 128;
    private RenderTexture smallRT;
    private Texture2D readTex;

    // sombra
    private Mesh sphereMesh;
    private Material shadowMaterial;
    private Matrix4x4[] shadowMatrices;

    void Start()
    {
        var generator = FindObjectOfType<DatasetGenerator>();
        if (sceneCamera == null && generator != null) sceneCamera = generator.cameraShaded;
        if (sceneLight == null && generator != null && generator.lightSource != null) sceneLight = generator.lightSource.GetComponent<Light>();
        if (splatRenderer == null) splatRenderer = FindObjectOfType<GaussianSplatRenderer>();

        // esfera usada como "tijolo" da sombra
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        shadowMaterial = new Material(Shader.Find("HDRP/Lit"));
        shadowMaterial.enableInstancing = true;
    }

    void LateUpdate()
    {
        bool active = splatRenderer != null && splatRenderer.isActiveAndEnabled && splatRenderer.m_Asset != null;
        if (!active)
        {
            Shader.SetGlobalFloat("_InsectTintEnabled", 0f);
            return;
        }

        LoadPointsIfNeeded();

        if (castShadow) DrawShadow();

        if (adjustColor && sceneCamera != null && sceneCamera.targetTexture != null) UpdateTint();
        else Shader.SetGlobalFloat("_InsectTintEnabled", 0f);
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat("_InsectTintEnabled", 0f);
    }

    // ---------------------------------------------------------------------------------------
    // Pontos do inseto
    // ---------------------------------------------------------------------------------------

    // Lê os centros dos splats do .ply de origem (mesmo arquivo usado na bounding box).
    // Se o .ply não for encontrado, usa os 8 cantos da caixa do asset (sombra fica um bloco).
    void LoadPointsIfNeeded()
    {
        string assetName = splatRenderer.m_Asset.name;
        if (assetName == loadedAsset) return;
        loadedAsset = assetName;
        points = null;

        var import = FindObjectOfType<InsectImport>();
        string ply = import != null ? import.FindPlyForAsset(assetName) : null;
        if (ply != null)
        {
            try
            {
                var cloud = SplatPointCloud.Load(ply, 0.1f, Mathf.Max(100, shadowPoints));
                if (cloud.MatchAssetBounds(splatRenderer.m_Asset.boundsMin, splatRenderer.m_Asset.boundsMax, out Vector3 signs))
                {
                    cloud.ApplySigns(signs);
                    points = cloud.points;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[InsectLighting] Falha ao ler " + ply + ": " + e.Message);
            }
        }

        if (points == null)
        {
            var b = new Bounds();
            b.SetMinMax(splatRenderer.m_Asset.boundsMin, splatRenderer.m_Asset.boundsMax);
            points = Logger.BoundsCorners(b);
        }

        Vector3 mn = points[0], mx = points[0];
        foreach (var p in points) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        insectRadius = Mathf.Max(1e-4f, (mx - mn).magnitude * 0.5f);
    }

    // ---------------------------------------------------------------------------------------
    // 1. Cor e intensidade
    // ---------------------------------------------------------------------------------------

    void UpdateTint()
    {
        // Lê o último frame renderizado numa resolução pequena (barato e suficiente para médias).
        RenderTexture rt = sceneCamera.targetTexture;
        if (smallRT == null)
        {
            smallRT = new RenderTexture(kReadSize, kReadSize, 0, RenderTextureFormat.ARGBHalf);
            readTex = new Texture2D(kReadSize, kReadSize, TextureFormat.RGBAHalf, false, true);
        }
        Graphics.Blit(rt, smallRT);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = smallRT;
        readTex.ReadPixels(new Rect(0, 0, kReadSize, kReadSize), 0, 0, false);
        RenderTexture.active = prev;
        Color[] pixels = readTex.GetPixels();

        // Projeta os pontos do inseto: pixels do inseto e caixa do inseto na imagem pequena.
        Matrix4x4 m = splatRenderer.transform.localToWorldMatrix;
        Matrix4x4 vp = sceneCamera.projectionMatrix * sceneCamera.worldToCameraMatrix;
        var insectPixels = new HashSet<int>();
        int x0 = kReadSize, y0 = kReadSize, x1 = -1, y1 = -1;
        foreach (var p in points)
        {
            Vector3 w = m.MultiplyPoint3x4(p);
            Vector4 c = vp * new Vector4(w.x, w.y, w.z, 1f);
            if (c.w <= 0) continue;
            int px = (int)((c.x / c.w * 0.5f + 0.5f) * kReadSize);
            int py = (int)((c.y / c.w * 0.5f + 0.5f) * kReadSize);
            if (px < 0 || py < 0 || px >= kReadSize || py >= kReadSize) continue;
            insectPixels.Add(py * kReadSize + px);
            x0 = Mathf.Min(x0, px); x1 = Mathf.Max(x1, px);
            y0 = Mathf.Min(y0, py); y1 = Mathf.Max(y1, py);
        }
        if (insectPixels.Count == 0) return; // inseto fora da imagem: mantém o ajuste anterior

        // Cor média do inseto (sem o ajuste aplicado no frame anterior)
        Color insect = Color.black;
        foreach (int idx in insectPixels) insect += pixels[idx];
        insect /= insectPixels.Count;
        insect = new Color(insect.r / Mathf.Max(currentTint.r, 1e-3f),
                           insect.g / Mathf.Max(currentTint.g, 1e-3f),
                           insect.b / Mathf.Max(currentTint.b, 1e-3f));

        // Cor média do chão num anel ao redor do inseto (onde bate o holofote, a sombra, o céu...)
        int marginX = Mathf.Max(2, (int)((x1 - x0) * ringSize));
        int marginY = Mathf.Max(2, (int)((y1 - y0) * ringSize));
        Color ground = Color.black;
        int count = 0;
        for (int y = Mathf.Max(0, y0 - marginY); y <= Mathf.Min(kReadSize - 1, y1 + marginY); y++)
        {
            for (int x = Mathf.Max(0, x0 - marginX); x <= Mathf.Min(kReadSize - 1, x1 + marginX); x++)
            {
                bool insideBox = x >= x0 && x <= x1 && y >= y0 && y <= y1;
                if (insideBox) continue;
                ground += pixels[y * kReadSize + x];
                count++;
            }
        }
        if (count == 0) return;
        ground /= count;

        // Brilho: o inseto acompanha (parcialmente) o brilho do chão ao redor.
        float insectLum = Mathf.Max(Luminance(insect), 1e-4f);
        float groundLum = Mathf.Max(Luminance(ground), 1e-4f);
        currentBrightness = Mathf.Clamp(Mathf.Pow(groundLum / insectLum, brightnessMatch), minBrightness, maxBrightness);

        // Cor: puxa para a cor do ambiente e, se a luz estiver ligada, para a cor dela.
        Color ambientColor = ground / groundLum;
        Color tint = Color.Lerp(Color.white, ambientColor, colorMatch);
        if (sceneLight != null && sceneLight.enabled)
            tint *= Color.Lerp(Color.white, sceneLight.color / Mathf.Max(Luminance(sceneLight.color), 1e-4f), colorMatch);

        currentTint = new Color(tint.r * currentBrightness, tint.g * currentBrightness, tint.b * currentBrightness, 1f);
        Shader.SetGlobalVector("_InsectTint", currentTint);
        Shader.SetGlobalFloat("_InsectTintEnabled", 1f);
    }

    static float Luminance(Color c)
    {
        return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }

    // ---------------------------------------------------------------------------------------
    // 2. Sombra no chão
    // ---------------------------------------------------------------------------------------

    // Esferas que só aparecem no mapa de sombras (invisíveis na imagem), uma por ponto do inseto.
    void DrawShadow()
    {
        if (sphereMesh == null || shadowMaterial == null) return;

        int n = Mathf.Min(points.Length, Mathf.Max(1, shadowPoints));
        if (shadowMatrices == null || shadowMatrices.Length != n) shadowMatrices = new Matrix4x4[n];

        int step = Mathf.Max(1, points.Length / n);
        float size = shadowPointSize * insectRadius * splatRenderer.transform.lossyScale.x;
        Vector3 scale = new Vector3(size, size, size);
        for (int i = 0; i < n; i++)
        {
            Vector3 w = splatRenderer.transform.TransformPoint(points[Mathf.Min(i * step, points.Length - 1)]);
            shadowMatrices[i] = Matrix4x4.TRS(w, Quaternion.identity, scale);
        }

        var rp = new RenderParams(shadowMaterial)
        {
            shadowCastingMode = ShadowCastingMode.ShadowsOnly,
            receiveShadows = false,
            layer = splatRenderer.gameObject.layer
        };
        // em blocos de 1023 (limite seguro do instancing)
        for (int start = 0; start < n; start += 1023)
            Graphics.RenderMeshInstanced(rp, sphereMesh, 0, shadowMatrices, Mathf.Min(1023, n - start), start);
    }

    void OnDestroy()
    {
        Shader.SetGlobalFloat("_InsectTintEnabled", 0f);
        if (smallRT != null) smallRT.Release();
        if (shadowMaterial != null) Destroy(shadowMaterial);
    }
}
