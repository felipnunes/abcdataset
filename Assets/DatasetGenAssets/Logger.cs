using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Calcula as bounding boxes e grava as anotações do dataset (labels YOLO, samples.csv, data.yaml).
public class Logger : MonoBehaviour
{
    public bool isLogging = true;

    private DatasetGenerator datasetGen;
    private InsectImport insectImport;

    [SerializeField]
    public GameObject boundingBox;          // retângulo de pré-visualização na UI (opcional)

    [Header("Bounding box")]
    [Tooltip("Splats com sigmoid(opacity) abaixo disso não entram na caixa (floaters quase invisíveis).")]
    [Range(0f, 1f)] public float minSplatOpacity = 0.1f;
    [Tooltip("Máximo de pontos projetados por amostra (subamostragem uniforme + extremos de cada eixo).")]
    public int maxBBoxPoints = 30000;
    [Tooltip("Margem em pixels: os pontos são centros de gaussianas, sem a extensão de cada splat.")]
    public float bboxPaddingPx = 2f;
    [Tooltip("Fração mínima dos pontos dentro da imagem para a amostra ser aceita.")]
    [Range(0f, 1f)] public float minVisibleFraction = 0.6f;
    public bool checkOcclusion = true;
    [Tooltip("Fração máxima de pontos ocultos (terreno/plano) para a amostra ser aceita.")]
    [Range(0f, 1f)] public float maxOccludedFraction = 0.05f;
    public int occlusionSamples = 200;
    [Tooltip("Raio que bate no chão a menos que (este valor × raio do inseto) do ponto não conta como oclusão.")]
    public float occlusionContactTolerance = 0.05f;
    [Tooltip("Fração máxima do inseto abaixo do chão (plano/terreno) para a amostra ser aceita.")]
    [Range(0f, 1f)] public float maxBuriedFraction = 0.5f;
    public LayerMask occluderMask = ~0;

    public struct BBoxResult
    {
        public bool valid;
        public string reason;
        public Rect rect;               // pixels, origem no canto INFERIOR esquerdo (convenção Unity), já recortada
        public float visibleFraction;
        public float occludedFraction;
        public float buriedFraction;    // fração dos pontos abaixo do chão (não aparecem na imagem)
        public string method;
        public int pointCount;
    }

    public BBoxResult lastResult;

    // cache da nuvem de pontos do modelo atual
    private string cachedKey;
    private Vector3[] cachedPoints;
    private string cachedMethod;

    // escrita do dataset
    private StreamWriter samplesWriter;
    private string runDir;

    public static readonly string[] SampleColumns =
    {
        "index", "image", "split", "model", "specimen", "class_id", "condition",
        "cam_px", "cam_py", "cam_pz", "cam_qx", "cam_qy", "cam_qz", "cam_qw", "cam_distance",
        "bbox_xmin_px", "bbox_ymin_px_top", "bbox_w_px", "bbox_h_px",
        "yolo_xc", "yolo_yc", "yolo_w", "yolo_h",
        "visible_fraction", "occluded_fraction", "bbox_method", "bbox_points", "pose_attempts",
        "sky", "ground"
    };

    void Start()
    {
        datasetGen = GetComponent<DatasetGenerator>();
        insectImport = GetComponent<InsectImport>();
        if (datasetGen == null)
        {
            Debug.Log("logger requires a dataset generator component");
            isLogging = false;
        }

        // O retângulo fica no Canvas original da cena (ligado à câmera shaded, mesmo tamanho da imagem),
        // então as coordenadas em pixels da imagem servem direto, como no código original.
        if (boundingBox != null)
        {
            boundingBoxTransform = boundingBox.GetComponent<RectTransform>();
            boundingBoxTransform.pivot = Vector2.zero;
            boundingBoxTransform.anchorMin = Vector2.zero;
            boundingBoxTransform.anchorMax = Vector2.zero;
        }
    }

    private RectTransform boundingBoxTransform;

    // ---------------------------------------------------------------------------------------
    // BOUNDING BOX
    // ---------------------------------------------------------------------------------------

    // Mantido por compatibilidade: calcula a caixa para a câmera shaded e atualiza a UI.
    public void CalculatesBoundingBox2DPoints()
    {
        var r = ComputeBBox(datasetGen.cameraShaded);
        UpdateOverlay(r, datasetGen.cameraShaded);
    }

    public BBoxResult ComputeBBox(Camera cam)
    {
        var res = new BBoxResult { valid = false, reason = "", method = "" };
        GameObject model = datasetGen.actualModel;
        if (model == null || cam == null) { res.reason = "sem_modelo"; lastResult = res; return res; }

        Matrix4x4 localToWorld;
        Vector3[] pts = GetModelPoints(model, out localToWorld, out res.method);
        if (pts == null || pts.Length == 0) { res.reason = "sem_pontos"; lastResult = res; return res; }

        float W = cam.pixelWidth, H = cam.pixelHeight;
        Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;
        float near = cam.nearClipPlane;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        int inside = 0, behind = 0, buried = 0;
        var insideWorld = new List<Vector3>(Mathf.Min(pts.Length, 4096));
        int insideStride = Mathf.Max(1, pts.Length / Mathf.Max(1, occlusionSamples * 4));
        Vector3 wMin = Vector3.positiveInfinity, wMax = Vector3.negativeInfinity;

        // Chão ativo nesta amostra (plano ou terreno). Partes do inseto abaixo dele não aparecem na imagem:
        // ficam fora da caixa e não contam como oclusão (antes o plano em y=0.5 "ocultava" a parte de baixo
        // do inseto, todas as poses com plano eram descartadas e só sobravam imagens com terreno).
        Terrain groundTerrain = (datasetGen.terrain != null && datasetGen.terrain.gameObject.activeInHierarchy) ? datasetGen.terrain : null;
        float planeY = (datasetGen.plane != null && datasetGen.plane.activeInHierarchy) ? datasetGen.plane.transform.position.y : float.NegativeInfinity;

        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 w = localToWorld.MultiplyPoint3x4(pts[i]);
            if (IsBelowGround(w, groundTerrain, planeY)) { buried++; continue; }
            wMin = Vector3.Min(wMin, w);
            wMax = Vector3.Max(wMax, w);
            Vector4 c = vp * new Vector4(w.x, w.y, w.z, 1f);
            if (c.w <= near) { behind++; continue; }
            float sx = (c.x / c.w * 0.5f + 0.5f) * W;
            float sy = (c.y / c.w * 0.5f + 0.5f) * H;
            if (sx < minX) minX = sx;
            if (sx > maxX) maxX = sx;
            if (sy < minY) minY = sy;
            if (sy > maxY) maxY = sy;
            if (sx >= 0 && sx <= W && sy >= 0 && sy <= H)
            {
                inside++;
                if (i % insideStride == 0) insideWorld.Add(w);
            }
        }

        res.pointCount = pts.Length;
        res.buriedFraction = buried / (float)pts.Length;
        int aboveGround = pts.Length - buried;
        if (aboveGround == 0 || res.buriedFraction > maxBuriedFraction)
        {
            res.reason = "abaixo_do_chao";
            lastResult = res;
            return res;
        }
        res.visibleFraction = inside / (float)aboveGround;

        if (behind > 0)
        {
            res.reason = "camera_dentro_ou_muito_perto";
            lastResult = res;
            return res;
        }

        // recorte aos limites da imagem + margem
        float x0 = Mathf.Clamp(minX - bboxPaddingPx, 0, W), x1 = Mathf.Clamp(maxX + bboxPaddingPx, 0, W);
        float y0 = Mathf.Clamp(minY - bboxPaddingPx, 0, H), y1 = Mathf.Clamp(maxY + bboxPaddingPx, 0, H);
        res.rect = new Rect(x0, y0, x1 - x0, y1 - y0);

        if (res.rect.width < 1f || res.rect.height < 1f) { res.reason = "fora_do_quadro"; lastResult = res; return res; }
        if (res.visibleFraction < minVisibleFraction) { res.reason = "pouco_visivel"; lastResult = res; return res; }

        // oclusão por terreno/plano (raycast da câmera até uma amostra dos pontos visíveis)
        if (checkOcclusion && insideWorld.Count > 0)
        {
            Physics.SyncTransforms();
            int stride = Mathf.Max(1, insideWorld.Count / Mathf.Max(1, occlusionSamples));
            int tested = 0, occluded = 0;
            Vector3 camPos = cam.transform.position;
            // Tolerância de contato: um raio que bate no chão bem junto do ponto (pata encostada no chão)
            // não é oclusão. Só conta o que está claramente na frente (ex.: um morro do terreno).
            float contactTol = occlusionContactTolerance * Mathf.Max(1e-4f, (wMax - wMin).magnitude * 0.5f);
            for (int i = 0; i < insideWorld.Count; i += stride)
            {
                Vector3 p = insideWorld[i];
                Vector3 d = p - camPos;
                float dist = d.magnitude;
                tested++;
                if (Physics.Raycast(camPos, d / dist, out RaycastHit hit, dist * 0.995f, occluderMask, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider.transform.IsChildOf(model.transform) && dist - hit.distance > contactTol) occluded++;
                }
            }
            res.occludedFraction = tested > 0 ? occluded / (float)tested : 0f;
            if (res.occludedFraction > maxOccludedFraction) { res.reason = "ocluido"; lastResult = res; return res; }
        }

        res.valid = true;
        lastResult = res;
        return res;
    }

    // Retorna os pontos que definem a caixa. Splat: centros das gaussianas lidos do .ply de origem
    // (espaço local do asset). Malha: vértices em espaço de mundo. Fallback: 8 cantos da AABB do asset.
    Vector3[] GetModelPoints(GameObject model, out Matrix4x4 localToWorld, out string method)
    {
        localToWorld = Matrix4x4.identity;
        method = "";

        if (datasetGen.splatMode)
        {
            var gs = model.GetComponentInChildren<GaussianSplatting.Runtime.GaussianSplatRenderer>();
            if (gs == null || gs.asset == null) return null;
            localToWorld = gs.transform.localToWorldMatrix;
            string key = "splat:" + gs.asset.name;
            if (key != cachedKey)
            {
                cachedKey = key;
                cachedPoints = null;
                cachedMethod = "aabb_corners";
                string ply = insectImport != null ? insectImport.FindPlyForAsset(gs.asset.name) : null;
                if (ply != null)
                {
                    try
                    {
                        var cloud = SplatPointCloud.Load(ply, minSplatOpacity, maxBBoxPoints);
                        if (cloud.MatchAssetBounds(gs.asset.boundsMin, gs.asset.boundsMax, out Vector3 signs))
                        {
                            cloud.ApplySigns(signs);
                            cachedPoints = cloud.points;
                            cachedMethod = "splat_centers";
                            if (signs != Vector3.one)
                                Debug.Log($"[BBox] {gs.asset.name}: eixos invertidos pelo importador detectados {signs}.");
                        }
                        else
                        {
                            Debug.LogWarning($"[BBox] A AABB do .ply não bate com os bounds do asset '{gs.asset.name}'. " +
                                             "O .ply não é o mesmo que gerou o asset? Usando a AABB do asset (caixa conservadora).");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[BBox] Falha ao ler '{ply}': {e.Message}. Usando a AABB do asset.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[BBox] .ply de origem de '{gs.asset.name}' não encontrado (InsectImport.plySourceFolder). " +
                                     "Usando a AABB do asset (caixa conservadora).");
                }

                if (cachedPoints == null)
                {
                    var b = new Bounds();
                    b.SetMinMax(gs.asset.boundsMin, gs.asset.boundsMax);
                    cachedPoints = BoundsCorners(b);
                }
            }
            method = cachedMethod;
            return cachedPoints;
        }
        else
        {
            // Malha: o modelo pode mudar de textura/instância a cada amostra, então recalcula sempre.
            var filters = model.GetComponentsInChildren<MeshFilter>();
            var list = new List<Vector3>();
            foreach (var f in filters)
            {
                if (f.sharedMesh == null) continue;
                var verts = f.sharedMesh.vertices;
                int step = Mathf.Max(1, verts.Length / Mathf.Max(1, maxBBoxPoints / Mathf.Max(1, filters.Length)));
                Matrix4x4 m = f.transform.localToWorldMatrix;
                for (int i = 0; i < verts.Length; i += step) list.Add(m.MultiplyPoint3x4(verts[i]));
            }
            cachedKey = null;
            method = "mesh_vertices";
            return list.ToArray();
        }
    }

    public void ClearCache() { cachedKey = null; cachedPoints = null; }

    static bool IsBelowGround(Vector3 w, Terrain terrain, float planeY)
    {
        if (w.y < planeY) return true;
        if (terrain != null)
        {
            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            // só vale dentro da área do terreno
            if (w.x >= tp.x && w.x <= tp.x + size.x && w.z >= tp.z && w.z <= tp.z + size.z)
                if (w.y < terrain.SampleHeight(w) + tp.y) return true;
        }
        return false;
    }

    public static Vector3[] BoundsCorners(Bounds b)
    {
        return new[]
        {
            new Vector3(b.min.x, b.min.y, b.min.z), new Vector3(b.min.x, b.min.y, b.max.z),
            new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.min.x, b.max.y, b.max.z),
            new Vector3(b.max.x, b.min.y, b.min.z), new Vector3(b.max.x, b.min.y, b.max.z),
            new Vector3(b.max.x, b.max.y, b.min.z), new Vector3(b.max.x, b.max.y, b.max.z),
        };
    }

    // Mantido por compatibilidade com código antigo.
    public Vector3[] BoundsToVec3(Bounds b) { return BoundsCorners(b); }

    // Pixels (origem inferior esquerda) -> YOLO normalizado (origem superior esquerda).
    public static Vector4 ToYOLO(Rect r, float W, float H)
    {
        float xc = (r.x + r.width * 0.5f) / W;
        float yc = 1f - (r.y + r.height * 0.5f) / H;
        return new Vector4(Mathf.Clamp01(xc), Mathf.Clamp01(yc), Mathf.Clamp01(r.width / W), Mathf.Clamp01(r.height / H));
    }

    public Vector4 GetYOLOFormat(Vector4 box, int classId)
    {
        float W = datasetGen.cameraShaded.targetTexture.width, H = datasetGen.cameraShaded.targetTexture.height;
        return ToYOLO(new Rect(box.x, box.y, box.z, box.w), W, H);
    }

    // Sempre com ponto decimal (o Windows em pt-BR usaria vírgula e o YOLO não leria o arquivo).
    public static string ConvertToYOLOString(Vector4 yoloBox, int classId)
    {
        var ci = CultureInfo.InvariantCulture;
        return string.Format(ci, "{0} {1:F6} {2:F6} {3:F6} {4:F6}", classId, yoloBox.x, yoloBox.y, yoloBox.z, yoloBox.w);
    }

    // Mesmo esquema do código original: posição e tamanho em pixels da imagem (origem inferior esquerda).
    public void UpdateOverlay(BBoxResult r, Camera cam)
    {
        if (boundingBoxTransform == null) return;
        boundingBox.SetActive(r.valid);
        if (!r.valid) return;
        boundingBoxTransform.anchoredPosition = new Vector2(r.rect.x, r.rect.y);
        boundingBoxTransform.sizeDelta = new Vector2(r.rect.width, r.rect.height);
    }

    // ---------------------------------------------------------------------------------------
    // ESCRITA DO DATASET
    // ---------------------------------------------------------------------------------------

    public void BeginRun(string dir, IList<string> classNames, bool withSketch)
    {
        runDir = dir;
        foreach (string sub in new[] { "images/train", "images/val", "labels/train", "labels/val" })
            Directory.CreateDirectory(Path.Combine(runDir, sub));
        if (withSketch) Directory.CreateDirectory(Path.Combine(runDir, "sketch"));

        // data.yaml (Ultralytics)
        var sb = new StringBuilder();
        sb.AppendLine("path: " + runDir.Replace('\\', '/'));
        sb.AppendLine("train: images/train");
        sb.AppendLine("val: images/val");
        sb.AppendLine("names:");
        for (int i = 0; i < classNames.Count; i++) sb.AppendLine($"  {i}: {classNames[i]}");
        File.WriteAllText(Path.Combine(runDir, "data.yaml"), sb.ToString());

        samplesWriter = new StreamWriter(Path.Combine(runDir, "samples.csv"), false, new UTF8Encoding(false));
        samplesWriter.AutoFlush = true; // uma execução interrompida continua legível
        samplesWriter.WriteLine(string.Join(",", SampleColumns));
    }

    public void WriteLabel(string path, int classId, Vector4 yolo)
    {
        File.WriteAllText(path, ConvertToYOLOString(yolo, classId) + "\n");
    }

    public void WriteSampleRow(params object[] values)
    {
        if (samplesWriter == null || !isLogging) return;
        var ci = CultureInfo.InvariantCulture;
        var cells = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            object v = values[i];
            string s = v is float f ? f.ToString("G7", ci)
                     : v is double d ? d.ToString("G9", ci)
                     : v is IFormattable fm ? fm.ToString(null, ci)
                     : (v?.ToString() ?? "");
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
            cells[i] = s;
        }
        samplesWriter.WriteLine(string.Join(",", cells));
    }

    public void EndRun()
    {
        if (samplesWriter != null)
        {
            samplesWriter.Flush();
            samplesWriter.Dispose();
            samplesWriter = null;
        }
    }

    // Mantidos por compatibilidade (o log JSON foi substituído por samples.csv).
    public void CloseLog() { EndRun(); }
    public void GenerateYOLODatasetFromJSON() { }

    void OnDestroy() { EndRun(); }
}
