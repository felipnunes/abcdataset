using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

// Gera o dataset: para cada amostra configura modelo, fundo e câmera, espera a cena renderizar
// e só então lê a imagem e grava o label. Tudo acontece numa única coroutine sequencial, então
// imagem, bounding box e nome do arquivo sempre descrevem o mesmo estado da cena,
// independentemente do frame rate.
public class DatasetGenerator : MonoBehaviour
{
    private string[] meshModelFileNames;
    private string[] splatModelFileNames;

    [Header("Config")]
    public string datasetPath;

    private string mainRandomizationParameters = "";

    public Camera cameraShaded;
    public Camera cameraSketch;

    private int charactereLimit = 200;

    public Transform cameraTarget;

    [Header("UI elements")]
    public Slider sliderRadius;
    public Slider sliderHCamStep;
    public Slider sliderVCamStep;
    public Toggle toggleCamHalfSphere;
    public Toggle toggleRandomizeCamPos;
    public Toggle toggleRandomizeLightPos;
    public Toggle toggleLightIsOn;
    public ToggleGroup toggleGroupLightType;
    public Toggle toggleLightTypeGeneral;
    public Toggle toggleLightTypeSpot;
    public Toggle toggleRandomizeTerrain;
    public Toggle toggleMesh;
    public Toggle toggleSplat;
    public Slider sliderPeaksHeight;
    public Slider sliderDatasetSize;
    public Slider sliderDelay;
    public Button buttonGenerateDataset;
    public Button buttonAdvancedOptions;
    public Text progressText;
    public GameObject panelCenter;
    public Dropdown dropdownChooseModel;
    public InputField DirectoryInputField;

    [Header("UI elements advanced options")]
    public Toggle toggleRandomizeModel;
    public Button buttonNormalizeModels;

    private Texture2D readbackTex;   // float, recebe a RenderTexture sem perder precisão
    private Texture2D outputTex;     // RGB24, vai para o PNG

    private bool isGenerating;
    private Coroutine generationRoutine;

    [Header("Light elements")]
    public GameObject lightSource;
    public GameObject skyAndFog;

    [Header("Terrain elements")]
    public Terrain terrain;
    public GameObject plane;

    [HideInInspector]
    public GameObject actualModel;
    public Vector4 actualModelBoundingBox; // pixels (xmin, ymin, w, h), origem inferior esquerda

    public bool splatMode;

    private Logger logger;

    public class PositionRotation
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public PositionRotation(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    // Poses usadas apenas na pré-visualização com gizmos
    [HideInInspector]
    public List<PositionRotation> cameraPositionRotation = new List<PositionRotation>();
    private bool isDirty = true;

    [Header("Camera/light gizmos")]
    public Transform gizmoParent;
    public GameObject gizmoPrefab;
    public int cameraPerturbationMaxAngle;
    public int lightPerturbationMaxAngle;
    public int skyLuxMinValue;
    public int skyLuxMaxValue;

    [Header("Reprodutibilidade")]
    [Tooltip("Seed mestra de toda a geração. 0 = sorteia uma a partir do relógio (fica registrada em run_config.json).")]
    public int seed = 0;

    [Header("Captura")]
    [Tooltip("Mínimo de frames de espera entre configurar a cena e ler a imagem.")]
    public int framesToWait = 4;
    [Tooltip("Durante a geração, a exposição automática se ajusta na hora (sem a adaptação gradual). " +
             "Necessário porque a condição de luz muda a cada foto.")]
    public bool instantExposure = true;
    [Tooltip("Espera mínima (ms) nas condições com plano. Valor do código original: 150.")]
    public int planeWaitMs = 150;
    [Tooltip("Espera mínima (ms) nas condições com terreno. Valor do código original: 300.")]
    public int terrainWaitMs = 300;
    [Tooltip("Modo splat: quantas imagens (poses) por asset. Total = nº de assets × este valor.")]
    public int imagesPerSplatModel = 1;
    public bool saveSketch = true;
    [Tooltip("A RenderTexture é linear: converte para sRGB antes de salvar o PNG.")]
    public bool applyLinearToSRGB = true;
    [Range(0f, 0.9f)] public float valFraction = 0.2f;
    [Tooltip("Modo splat: a cada N modelos carregados, descarrega da RAM os assets que não estão mais em uso.")]
    public int unloadUnusedEveryNModels = 10;

    [Header("Câmera (distância relativa ao tamanho do modelo)")]
    [Tooltip("Distância mínima em múltiplos do raio da esfera envolvente do modelo. " +
             "O slider 'Radius' passa a ser a distância máxima nessa mesma unidade.")]
    public float distanceMinFactor = 2.2f;
    public float minElevationDeg = 5f;
    public float maxElevationDeg = 85f;
    [Tooltip("Tentativas de sortear uma pose válida (inseto visível e não ocluído) antes de pular a amostra.")]
    public int maxPoseAttempts = 30;

    [Header("Classes")]
    [Tooltip("Falso: uma classe só (detecção de inseto). Verdadeiro: uma classe por espécime " +
             "(nome do asset antes de '_Pipe' ou '_Original').")]
    public bool classPerSpecimen = false;
    public string singleClassName = "insect";

    // estado da execução
    private string runDir;
    private int currentSeed;
    private int savedCount, skippedCount;
    private readonly List<string> classNames = new List<string>();
    private Transform poseHelper;

    void Start()
    {
        OnValueChangePrimitive(); //sets splatMode based on actual value of primitive mode toggle

        if (!splatMode)
        {
            this.GetComponent<InsectImport>().InstantiateRandomMeshModel();
        }
        else
        {
            this.GetComponent<InsectImport>().InstantiateRandomSplatModel();
        }

        DirectoryInputField.characterLimit = charactereLimit;
        DirectoryInputField.text = "C:\\Users\\felip\\Documents\\Mestrado\\Insetos\\dataset";
        datasetPath = DirectoryInputField.text;

        if (cameraShaded == null) Debug.LogError("Invalid camera for shaded object");
        if (cameraSketch == null) Debug.LogError("Invalid camera for sketch object");

        logger = GetComponent<Logger>();

        var helperGO = new GameObject("CameraPoseHelper");
        helperGO.hideFlags = HideFlags.HideAndDontSave;
        poseHelper = helperGO.transform;

        // Set up callbacks for UI events
        sliderRadius.onValueChanged.AddListener(delegate { OnValueChangedRadius(); });
        sliderHCamStep.onValueChanged.AddListener(delegate { OnValueChangedHCamStep(); });
        sliderVCamStep.onValueChanged.AddListener(delegate { OnValueChangedVCamStep(); });
        toggleCamHalfSphere.onValueChanged.AddListener(delegate { OnValueChangedCamHalfSphere(); });
        toggleRandomizeCamPos.onValueChanged.AddListener(delegate { OnValueChangedRandomizeCamPos(); });
        toggleLightIsOn.onValueChanged.AddListener(delegate { OnValueChangeLightIsOn(); });
        toggleLightTypeGeneral.onValueChanged.AddListener(delegate { OnValueChangeLightTypeGeneral(); });
        toggleLightTypeSpot.onValueChanged.AddListener(delegate { OnValueChangeLightTypeSpot(); });
        toggleRandomizeTerrain.onValueChanged.AddListener(delegate { OnValueChangeRandomizeTerrain(); });
        toggleMesh.onValueChanged.AddListener(delegate { OnValueChangePrimitive(); });
        toggleSplat.onValueChanged.AddListener(delegate { OnValueChangePrimitive(); });
        sliderPeaksHeight.onValueChanged.AddListener(delegate { OnValueChangesliderPeaksHeight(); });
        sliderDatasetSize.onValueChanged.AddListener(delegate { OnValueChangeDatasetSize(); });
        sliderDelay.onValueChanged.AddListener(delegate { OnValueChangeDelay(); });
        buttonGenerateDataset.onClick.AddListener(delegate { OnClickButtonGenerate(); });
        buttonAdvancedOptions.onClick.AddListener(delegate { OnClickButtonAdvancedOptions(); });
        buttonNormalizeModels.onClick.AddListener(delegate { OnClickButtonNormalizeModels(); });
        dropdownChooseModel.onValueChanged.AddListener(delegate { OnValueChangeDropdownChooseModel(); });
        DirectoryInputField.onValueChanged.AddListener(delegate { OnValueChangeDirectoryInputField(); });

        // Force updating the labels of the sliders
        OnValueChangedRadius();
        OnValueChangedHCamStep();
        OnValueChangedVCamStep();
        OnValueChangeDatasetSize();
        OnValueChangeDelay();
    }

    void Update()
    {
        if (isGenerating) return; // durante a geração, a coroutine controla tudo

        actualModel = GameObject.FindGameObjectWithTag("Model");

        if (isDirty)
        {
            if (dropdownChooseModel.options.Count == 0)
            {
                RebuildDropDownOptions();
            }
            RebuildTransforms();
            isDirty = false;
        }
    }

    // ===========================================================================================
    // GERAÇÃO
    // ===========================================================================================

    public void OnClickButtonGenerate()
    {
        if (!isGenerating) StartGeneration();
        else StopGeneration("Interrompido pelo usuário");
    }

    void StartGeneration()
    {
        if (string.IsNullOrWhiteSpace(datasetPath))
        {
            Debug.LogError("Defina a pasta do dataset antes de gerar.");
            return;
        }

        currentSeed = seed != 0 ? seed : (int)(DateTime.Now.Ticks & 0x7FFFFFFF);
        if (currentSeed == 0) currentSeed = 1;
        UnityEngine.Random.InitState(currentSeed);

        isGenerating = true;
        buttonGenerateDataset.GetComponentInChildren<Text>().text = "Stop";
        SetEnabledUIElements(false);
        if (instantExposure) EnableInstantExposure();
        generationRoutine = StartCoroutine(GenerateDataset());
    }

    void StopGeneration(string reason)
    {
        if (generationRoutine != null) StopCoroutine(generationRoutine);
        FinishRun(reason);
    }

    IEnumerator GenerateDataset()
    {
        var importer = GetComponent<InsectImport>();
        List<string> splatList = splatMode ? importer.AllSplatModelNames.ToList() : null;
        if (splatMode && splatList.Count == 0)
        {
            Debug.LogError("Nenhum asset de splat em Assets/GaussianAssets.");
            FinishRun("Sem assets");
            yield break;
        }

        int perModel = Mathf.Max(1, imagesPerSplatModel);
        int total = splatMode ? splatList.Count * perModel : Mathf.RoundToInt(sliderDatasetSize.value);

        BuildClassNames(splatList);
        runDir = Path.Combine(datasetPath, $"run_{DateTime.Now:yyyyMMdd_HHmmss}_seed{currentSeed}");
        logger.BeginRun(runDir, classNames, saveSketch && cameraSketch != null);
        savedCount = 0;
        skippedCount = 0;
        WriteRunConfig(total, "em andamento");

        string loadedSplat = null;
        int modelsLoaded = 0;

        for (int i = 0; i < total; i++)
        {
            // ---- 1. Condição de cena (plano/terreno, luz) ----
            ActualizeRandomizationParameters(i, total);

            // ---- 2. Modelo ----
            string modelName;
            if (splatMode)
            {
                modelName = splatList[i / perModel];
                if (modelName != loadedSplat)
                {
                    if (!importer.LoadSplatModel(modelName)) { skippedCount++; continue; }
                    loadedSplat = modelName;
                    modelsLoaded++;
                    // Assets carregados com LoadAssetAtPath (e os .bytes de cada um) ficam na RAM até serem
                    // descarregados: sem isso, a memória cresce a cada modelo ao longo da geração.
                    if (unloadUnusedEveryNModels > 0 && modelsLoaded % unloadUnusedEveryNModels == 0)
                        yield return Resources.UnloadUnusedAssets();
                }
                actualModel = importer.splatRenderer.gameObject;
                modelName = Path.GetFileNameWithoutExtension(modelName);
            }
            else
            {
                if (i > 0)
                {
                    if (toggleRandomizeModel.isOn) RandomizeMeshModel();
                    else importer.AddMaterial(actualModel);
                }
                if (actualModel == null) actualModel = GameObject.FindGameObjectWithTag("Model");
                if (actualModel == null) { Debug.LogError("Nenhum modelo de malha na cena."); skippedCount++; continue; }
                modelName = actualModel.name.Replace("(Clone)", "");
            }
            cameraTarget = actualModel.transform;

            // ---- 3. Fundo e luz ----
            if (toggleRandomizeLightPos.isOn) RandomizeLight();
            if (toggleRandomizeTerrain.isOn) RandomizeTerrain();
            else RandomizePlane();
            var hdri = skyAndFog.GetComponent<HDRIRandomizer>();
            hdri.RandomizeHDRISky();
            Physics.SyncTransforms();

            // ---- 4. Pose da câmera (com validação da bounding box antes de renderizar) ----
            if (!GetModelSphere(out Vector3 center, out float radius))
            {
                Debug.LogWarning($"[{i}] Não foi possível obter os bounds do modelo {modelName}. Amostra pulada.");
                skippedCount++;
                continue;
            }

            bool randomMode = toggleRandomizeCamPos.isOn;
            int maxAttempts = randomMode ? Mathf.Max(1, maxPoseAttempts) : 1;
            Logger.BBoxResult bbox = default;
            PositionRotation pose = null;
            int attempts = 0;
            while (attempts < maxAttempts)
            {
                attempts++;
                pose = SampleCameraPose(i, center, radius, randomMode);
                ApplyPose(pose, center);
                bbox = logger.ComputeBBox(cameraShaded);
                if (bbox.valid) break;
            }
            if (!bbox.valid)
            {
                Debug.LogWarning($"[{i}] {modelName}: nenhuma pose válida em {attempts} tentativa(s) (último motivo: {bbox.reason}). Amostra pulada.");
                skippedCount++;
                UpdateProgress(i, total);
                continue;
            }

            string split = UnityEngine.Random.value < valFraction ? "val" : "train";

            // ---- 5. Espera a cena renderizar com esse estado ----
            // A moldura da prévia é atualizada depois do primeiro frame renderizado com a nova pose
            // (o modelo novo só aparece no frame seguinte à troca do asset).
            // Tempo de espera por condição, como no código original: terreno é mais pesado (heightmap novo)
            // e precisa de mais tempo para a exposição automática e o desfoque de movimento estabilizarem.
            // A espera é em tempo real (não em frames), então se adapta à velocidade do computador.
            sliderDelay.value = toggleRandomizeTerrain.isOn ? terrainWaitMs : planeWaitMs; // como antes: o slider mostra o delay da condição
            float waitSec = sliderDelay.value / 1000f;
            float setupTime = Time.realtimeSinceStartup;
            yield return null;
            logger.UpdateOverlay(bbox, cameraShaded);
            int framesWaited = 1;
            while (framesWaited < Mathf.Max(2, framesToWait) - 1 || Time.realtimeSinceStartup - setupTime < waitSec)
            {
                yield return null;
                framesWaited++;
            }

            // A moldura está na camada "Moldura", excluída da câmera shaded, então não vai para a imagem.
            // Só é escondida no frame da captura se a câmera estiver configurada para desenhar essa camada.
            bool overlayInImage = logger.boundingBox != null &&
                                  (cameraShaded.cullingMask & (1 << logger.boundingBox.layer)) != 0;
            if (overlayInImage) logger.boundingBox.SetActive(false);
            yield return null; // a imagem lida a seguir é a deste frame

            // ---- 6. Grava imagem, label e linha do CSV ----
            string baseName = $"{i:D6}_{Sanitize(modelName)}";
            string imgRel = Path.Combine("images", split, baseName + ".png");
            SaveCameraImage(cameraShaded, Path.Combine(runDir, imgRel));
            if (saveSketch && cameraSketch != null)
                SaveCameraImage(cameraSketch, Path.Combine(runDir, "sketch", baseName + ".png"));

            float W = cameraShaded.pixelWidth, H = cameraShaded.pixelHeight;
            Vector4 yolo = Logger.ToYOLO(bbox.rect, W, H);
            string specimen = SpecimenOf(modelName);
            int classId = GetClassId(specimen);
            logger.WriteLabel(Path.Combine(runDir, "labels", split, baseName + ".txt"), classId, yolo);

            actualModelBoundingBox = new Vector4(bbox.rect.x, bbox.rect.y, bbox.rect.width, bbox.rect.height);
            if (overlayInImage) logger.boundingBox.SetActive(true);

            Vector3 cp = cameraShaded.transform.position;
            Quaternion cq = cameraShaded.transform.rotation;
            string ground = toggleRandomizeTerrain.isOn
                ? terrain.GetComponent<TerrainGenerator>().lastTextureName
                : plane.GetComponent<PlaneGenerator>().lastTextureName;
            logger.WriteSampleRow(
                i, imgRel.Replace('\\', '/'), split, modelName, specimen, classId, mainRandomizationParameters,
                cp.x, cp.y, cp.z, cq.x, cq.y, cq.z, cq.w, (cp - center).magnitude,
                bbox.rect.x, H - (bbox.rect.y + bbox.rect.height), bbox.rect.width, bbox.rect.height,
                yolo.x, yolo.y, yolo.z, yolo.w,
                bbox.visibleFraction, bbox.occludedFraction, bbox.method, bbox.pointCount, attempts,
                hdri.lastSkyName, ground);

            savedCount++;
            UpdateProgress(i, total);
        }

        FinishRun("Concluído");
    }

    // A exposição da cena é automática e PROGRESSIVA (ajusta 1 a 3 EV por segundo). Com a condição de
    // luz mudando a cada foto, ela nunca chegava ao valor certo em 150–300 ms e as fotos sem luz e com
    // holofote saíam escuras. Durante a geração, um Volume global de alta prioridade troca só o modo de
    // adaptação para instantâneo: o alvo da exposição automática continua o mesmo, mas é atingido em
    // um frame. Tudo volta ao normal quando a geração termina.
    private GameObject exposureVolumeGO;
    private VolumeProfile exposureProfile;

    void EnableInstantExposure()
    {
        DisableInstantExposure();
        exposureVolumeGO = new GameObject("DatasetInstantExposure") { hideFlags = HideFlags.HideAndDontSave };
        exposureVolumeGO.layer = 0;
        var volume = exposureVolumeGO.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f;
        exposureProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        var exposure = exposureProfile.Add<Exposure>(false);
        exposure.adaptationMode.Override(AdaptationMode.Fixed);
        volume.sharedProfile = exposureProfile;
    }

    void DisableInstantExposure()
    {
        if (exposureVolumeGO != null) Destroy(exposureVolumeGO);
        if (exposureProfile != null) Destroy(exposureProfile);
        exposureVolumeGO = null;
        exposureProfile = null;
    }

    void FinishRun(string status)
    {
        if (logger != null) logger.EndRun();
        DisableInstantExposure();
        if (!string.IsNullOrEmpty(runDir) && Directory.Exists(runDir))
        {
            WriteRunConfig(-1, status);
            Debug.Log($"[Dataset] {status}: {savedCount} amostras salvas, {skippedCount} puladas. Pasta: {runDir}");
        }

        isGenerating = false;
        generationRoutine = null;
        SetEnabledUIElements(true);
        buttonGenerateDataset.GetComponentInChildren<Text>().text = "Generate dataset";
        progressText.text = !string.IsNullOrEmpty(runDir)
            ? $"{status}: {savedCount} salvas, {skippedCount} puladas\n{runDir}"
            : "";
    }

    void UpdateProgress(int i, int total)
    {
        progressText.text = $"Gerando {i + 1} de {total} ...\nSalvas: {savedCount}  Puladas: {skippedCount}\n{runDir}";
    }

    // Lê a RenderTexture em float, converte linear -> sRGB e salva PNG RGB (sem alpha).
    void SaveCameraImage(Camera cam, string path)
    {
        RenderTexture rt = cam.targetTexture;
        if (readbackTex == null || readbackTex.width != rt.width || readbackTex.height != rt.height)
        {
            readbackTex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            outputTex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false, false);
        }

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        readbackTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        readbackTex.Apply(false);
        RenderTexture.active = prev;

        Color[] px = readbackTex.GetPixels();
        for (int k = 0; k < px.Length; k++)
        {
            Color c = px[k];
            if (applyLinearToSRGB)
            {
                c.r = Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r));
                c.g = Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g));
                c.b = Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b));
            }
            c.a = 1f;
            px[k] = c;
        }
        outputTex.SetPixels(px);
        outputTex.Apply(false);

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, outputTex.EncodeToPNG());
    }

    // ===========================================================================================
    // CÂMERA
    // ===========================================================================================

    // Centro e raio da esfera envolvente do modelo em coordenadas de mundo.
    bool GetModelSphere(out Vector3 center, out float radius)
    {
        center = Vector3.zero;
        radius = 1f;
        if (actualModel == null) return false;

        Bounds b;
        if (splatMode)
        {
            var gs = actualModel.GetComponentInChildren<GaussianSplatting.Runtime.GaussianSplatRenderer>();
            if (gs == null || gs.asset == null) return false;
            var lb = new Bounds();
            lb.SetMinMax(gs.asset.boundsMin, gs.asset.boundsMax);
            b = TransformBounds(gs.transform.localToWorldMatrix, lb);
        }
        else
        {
            var rends = actualModel.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
        }

        center = b.center;
        radius = Mathf.Max(1e-4f, b.extents.magnitude);
        return true;
    }

    static Bounds TransformBounds(Matrix4x4 m, Bounds lb)
    {
        var corners = Logger.BoundsCorners(lb);
        var b = new Bounds(m.MultiplyPoint3x4(corners[0]), Vector3.zero);
        for (int k = 1; k < corners.Length; k++) b.Encapsulate(m.MultiplyPoint3x4(corners[k]));
        return b;
    }

    // Modo aleatório: direção uniforme numa calota esférica em torno do centro do modelo, distância em
    // [distanceMinFactor, sliderRadius] × raio, e perturbação angular. Modo step: mesma lógica de antes
    // (anéis horizontais/verticais), agora centrada no modelo e com distância relativa ao tamanho.
    PositionRotation SampleCameraPose(int index, Vector3 center, float radius, bool randomMode)
    {
        float maxFactor = Mathf.Max(distanceMinFactor, sliderRadius.value);

        if (randomMode)
        {
            float minEl = toggleCamHalfSphere.isOn ? minElevationDeg : -maxElevationDeg;
            float y = UnityEngine.Random.Range(Mathf.Sin(minEl * Mathf.Deg2Rad), Mathf.Sin(maxElevationDeg * Mathf.Deg2Rad));
            float az = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
            float rxz = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            Vector3 dir = new Vector3(rxz * Mathf.Cos(az), y, rxz * Mathf.Sin(az));
            float dist = UnityEngine.Random.Range(distanceMinFactor, maxFactor) * radius;

            poseHelper.position = center + dir * dist;
            poseHelper.LookAt(center);
            AddCameraPerturbtion(poseHelper);
        }
        else
        {
            float dist = sliderRadius.value * radius;
            StepAngles(index, out float h, out float v);
            poseHelper.position = center + new Vector3(0f, 0f, -dist);
            poseHelper.LookAt(center);
            poseHelper.RotateAround(center, Vector3.up, h);
            poseHelper.RotateAround(center, poseHelper.right, v);
        }

        return new PositionRotation(poseHelper.position, poseHelper.rotation);
    }

    // Reproduz a sequência de ângulos do modo step original para o índice dado.
    void StepAngles(int index, out float h, out float v)
    {
        h = 0f;
        v = 0f;
        for (int k = 0; k < index; k++)
        {
            h += sliderHCamStep.value;
            if (h >= 360.0f)
            {
                if (toggleCamHalfSphere.isOn) v += sliderVCamStep.value;
                else if (v > 0) v = -v;
                else v = -v + sliderVCamStep.value;
                h = 0;
            }
            if (v > 90.0f || v < -90.0f) v = 0; // evita câmera de cabeça para baixo
        }
    }

    // Posiciona as duas câmeras e ajusta o near clip à distância (modelos pequenos em unidades do COLMAP).
    void ApplyPose(PositionRotation pr, Vector3 center)
    {
        float dist = (pr.Position - center).magnitude;
        float near = Mathf.Clamp(0.02f * dist, 0.0001f, 0.3f);
        foreach (var cam in new[] { cameraShaded, cameraSketch })
        {
            if (cam == null) continue;
            cam.transform.SetPositionAndRotation(pr.Position, pr.Rotation);
            cam.nearClipPlane = near;
        }
    }

    private void AddCameraPerturbtion(Transform curTransform)
    {
        curTransform.Rotate(UnityEngine.Random.Range((float)-cameraPerturbationMaxAngle, (float)cameraPerturbationMaxAngle),
                            UnityEngine.Random.Range((float)-cameraPerturbationMaxAngle, (float)cameraPerturbationMaxAngle),
                            UnityEngine.Random.Range((float)-cameraPerturbationMaxAngle, (float)cameraPerturbationMaxAngle));
    }

    // Pré-visualização das poses com gizmos (não é usada na geração).
    void RebuildTransforms()
    {
        cameraPositionRotation.Clear();
        if (poseHelper == null || !GetModelSphere(out Vector3 center, out float radius)) return;

        int n = Mathf.Min(Mathf.RoundToInt(sliderDatasetSize.value), 300);
        for (int i = 0; i < n; ++i)
            cameraPositionRotation.Add(SampleCameraPose(i, center, radius, toggleRandomizeCamPos.isOn));

        if (gizmoParent != null && gizmoPrefab != null)
        {
            foreach (Transform child in gizmoParent.transform)
                Destroy(child.gameObject);

            for (int i = 0; i < cameraPositionRotation.Count; ++i)
            {
                var t = cameraPositionRotation[i];
                GameObject GO = GameObject.Instantiate(gizmoPrefab, t.Position, t.Rotation, gizmoParent);
                GO.name = "Gizmo_" + i;
            }
        }
    }

    // ===========================================================================================
    // CLASSES, NOMES E CONFIGURAÇÃO DA EXECUÇÃO
    // ===========================================================================================

    static string SpecimenOf(string modelName)
    {
        int k = modelName.IndexOf("_Pipe", StringComparison.Ordinal);
        if (k < 0) k = modelName.IndexOf("_Original", StringComparison.Ordinal);
        return k > 0 ? modelName.Substring(0, k) : modelName;
    }

    void BuildClassNames(List<string> splatList)
    {
        classNames.Clear();
        if (classPerSpecimen && splatList != null)
            classNames.AddRange(splatList.Select(n => SpecimenOf(Path.GetFileNameWithoutExtension(n)))
                                         .Distinct().OrderBy(s => s, StringComparer.Ordinal));
        else
            classNames.Add(singleClassName);
    }

    int GetClassId(string specimen)
    {
        if (!classPerSpecimen) return 0;
        int id = classNames.IndexOf(specimen);
        return id < 0 ? 0 : id;
    }

    static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(invalid.Contains(c) || c == ' ' ? '_' : c);
        return sb.ToString();
    }

    void WriteRunConfig(int total, string status)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(float x) => x.ToString("G7", ci);
        string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"status\": \"{Esc(status)}\",");
        sb.AppendLine($"  \"seed\": {currentSeed},");
        if (total >= 0) sb.AppendLine($"  \"planned_samples\": {total},");
        sb.AppendLine($"  \"saved_samples\": {savedCount},");
        sb.AppendLine($"  \"skipped_samples\": {skippedCount},");
        sb.AppendLine($"  \"mode\": \"{(splatMode ? "splat" : "mesh")}\",");
        sb.AppendLine($"  \"images_per_splat_model\": {imagesPerSplatModel},");
        sb.AppendLine($"  \"random_camera\": {(toggleRandomizeCamPos.isOn ? "true" : "false")},");
        sb.AppendLine($"  \"half_sphere\": {(toggleCamHalfSphere.isOn ? "true" : "false")},");
        sb.AppendLine($"  \"distance_min_factor\": {F(distanceMinFactor)},");
        sb.AppendLine($"  \"distance_max_factor\": {F(sliderRadius.value)},");
        sb.AppendLine($"  \"elevation_deg\": [{F(minElevationDeg)}, {F(maxElevationDeg)}],");
        sb.AppendLine($"  \"camera_perturbation_deg\": {cameraPerturbationMaxAngle},");
        sb.AppendLine($"  \"h_step_deg\": {F(sliderHCamStep.value)},");
        sb.AppendLine($"  \"v_step_deg\": {F(sliderVCamStep.value)},");
        sb.AppendLine($"  \"frames_to_wait\": {framesToWait},");
        sb.AppendLine($"  \"plane_wait_ms\": {planeWaitMs},");
        sb.AppendLine($"  \"terrain_wait_ms\": {terrainWaitMs},");
        sb.AppendLine($"  \"val_fraction\": {F(valFraction)},");
        sb.AppendLine($"  \"linear_to_srgb\": {(applyLinearToSRGB ? "true" : "false")},");
        sb.AppendLine($"  \"bbox_min_splat_opacity\": {F(logger.minSplatOpacity)},");
        sb.AppendLine($"  \"bbox_padding_px\": {F(logger.bboxPaddingPx)},");
        sb.AppendLine($"  \"bbox_min_visible_fraction\": {F(logger.minVisibleFraction)},");
        sb.AppendLine($"  \"bbox_check_occlusion\": {(logger.checkOcclusion ? "true" : "false")},");
        sb.AppendLine($"  \"bbox_max_occluded_fraction\": {F(logger.maxOccludedFraction)},");
        sb.AppendLine($"  \"image_size\": [{cameraShaded.pixelWidth}, {cameraShaded.pixelHeight}],");
        sb.AppendLine($"  \"classes\": [{string.Join(", ", classNames.Select(c => "\"" + Esc(c) + "\""))}],");
        sb.AppendLine($"  \"git_commit\": \"{Esc(GetGitCommit())}\"");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(runDir, "run_config.json"), sb.ToString());
    }

    static string GetGitCommit()
    {
        try
        {
            string gitDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".git");
            string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            if (!head.StartsWith("ref:")) return head;
            string refPath = head.Substring(4).Trim();
            string full = Path.Combine(gitDir, refPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return File.ReadAllText(full).Trim();
            string packed = Path.Combine(gitDir, "packed-refs");
            if (File.Exists(packed))
                foreach (var line in File.ReadAllLines(packed))
                    if (line.EndsWith(" " + refPath)) return line.Split(' ')[0];
        }
        catch (Exception) { }
        return "";
    }

    // ===========================================================================================
    // MODELOS, LUZ E FUNDO
    // ===========================================================================================

    private void RebuildDropDownOptions()
    {
        meshModelFileNames = this.GetComponent<InsectImport>().meshModelNames;
        splatModelFileNames = this.GetComponent<InsectImport>().splatModelNames.ToArray();

        dropdownChooseModel.options.Clear();
        foreach (string modelFileName in meshModelFileNames)
        {
            dropdownChooseModel.options.Add(new Dropdown.OptionData() { text = modelFileName });
        }
    }

    private void RandomizeMeshModel()
    {
        gameObject.GetComponent<InsectImport>().destroyActualModel();
        gameObject.GetComponent<InsectImport>().InstantiateRandomMeshModel();
        if (actualModel != null)
        {
            cameraTarget = actualModel.transform;
        }
    }

    public void RandomizeSplatModel()
    {
        gameObject.GetComponent<InsectImport>().InstantiateRandomSplatModel();
    }

    private void RandomizeLight()
    {
        UnityEngine.Light light = lightSource.GetComponent<UnityEngine.Light>();
        if (light.type == UnityEngine.LightType.Spot)
        {
            light.intensity = UnityEngine.Random.Range(500f, 1200f);
            lightSource.transform.position = new Vector3(UnityEngine.Random.Range(-2, 2), lightSource.transform.position.y, UnityEngine.Random.Range(-2, 2));
            lightSource.transform.LookAt(actualModel.transform);
        }
        else if (light.type == UnityEngine.LightType.Directional)
        {
            lightSource.transform.LookAt(actualModel.transform);
            lightSource.transform.Rotate(UnityEngine.Random.Range((float)-lightPerturbationMaxAngle, (float)lightPerturbationMaxAngle),
                                         UnityEngine.Random.Range((float)-lightPerturbationMaxAngle, (float)lightPerturbationMaxAngle),
                                         UnityEngine.Random.Range((float)-lightPerturbationMaxAngle, (float)lightPerturbationMaxAngle));
        }
    }

    private void RandomizeTerrain()
    {
        if (terrain.enabled == true)
        {
            terrain.terrainData.SetHeights(0, 0, terrain.GetComponent<TerrainGenerator>().GenerateNoise());
            terrain.GetComponent<TerrainGenerator>().RandomizeTexture();
        }
    }

    private void RandomizePlane()
    {
        plane.GetComponent<PlaneGenerator>().RandomizeTexture();
    }

    // Seis condições de cena em rodízio (foto 0 = P-NL, foto 1 = T-NL, ..., foto 6 = P-NL de novo).
    // Antes eram blocos de 1/6 do total na ordem das fotos: como os modelos são percorridos em ordem
    // alfabética (todos os _PipeA_ antes dos _PipeB_), o fundo ficava atrelado ao pipeline.
    // Com o rodízio, cada condição aparece a mesma quantidade de vezes ao longo de todo o dataset,
    // em qualquer trecho da lista de modelos.
    private void ActualizeRandomizationParameters(int index, int total)
    {
        switch (index % 6)
        {
            case 0: //Plane + No Light
                toggleLightIsOn.isOn = false;
                toggleRandomizeTerrain.isOn = false;
                mainRandomizationParameters = "P-NL";
                break;

            case 1: //Terrain + No Light
                toggleLightIsOn.isOn = false;
                toggleRandomizeTerrain.isOn = true;
                mainRandomizationParameters = "T-NL";
                break;

            case 2: //Plane + General Light
                toggleLightIsOn.isOn = true;
                toggleLightTypeGeneral.isOn = true;
                toggleRandomizeTerrain.isOn = false;
                mainRandomizationParameters = "P-GL";
                break;

            case 3: //Terrain + General Light
                toggleLightIsOn.isOn = true;
                toggleLightTypeGeneral.isOn = true;
                toggleRandomizeTerrain.isOn = true;
                mainRandomizationParameters = "T-GL";
                break;

            case 4: //Plane + Spot Light
                toggleLightIsOn.isOn = true;
                toggleLightTypeSpot.isOn = true;
                toggleRandomizeTerrain.isOn = false;
                mainRandomizationParameters = "P-SL";
                break;

            default: //Terrain + Spot Light
                toggleLightIsOn.isOn = true;
                toggleLightTypeSpot.isOn = true;
                toggleRandomizeTerrain.isOn = true;
                mainRandomizationParameters = "T-SL";
                break;
        }
    }

    // ===========================================================================================
    // UI
    // ===========================================================================================

    // Set enabled state of all UI elements (except "generate dataset" button)
    void SetEnabledUIElements(bool enabled)
    {
        sliderRadius.enabled = enabled;
        sliderHCamStep.enabled = enabled;
        sliderVCamStep.enabled = enabled;
        toggleCamHalfSphere.enabled = enabled;
        toggleRandomizeCamPos.enabled = enabled;
        toggleRandomizeLightPos.enabled = enabled;
        sliderDatasetSize.enabled = enabled;
        sliderDelay.enabled = enabled;
    }

    // OnValueChange event of "Distance from target" slider (agora em múltiplos do raio do modelo)
    public void OnValueChangedRadius()
    {
        sliderRadius.value = Mathf.Round(sliderRadius.value / .1f) * .1f;

        Text textComponent = sliderRadius.transform.GetChild(4).GetComponent<Text>();
        if (textComponent == null)
        {
            Debug.LogError("Invalid text component");
        }
        textComponent.text = sliderRadius.value.ToString(CultureInfo.InvariantCulture);

        isDirty = true;
    }

    public void OnValueChangedHCamStep()
    {
        sliderHCamStep.value = Mathf.Round(sliderHCamStep.value / 5) * 5;

        Text textComponent = sliderHCamStep.transform.GetChild(4).GetComponent<Text>();
        if (textComponent == null)
        {
            Debug.LogError("Invalid text component");
        }
        textComponent.text = sliderHCamStep.value.ToString();

        isDirty = true;
    }

    public void OnValueChangedVCamStep()
    {
        sliderVCamStep.value = Mathf.Round(sliderVCamStep.value / 5) * 5;

        Text textComponent = sliderVCamStep.transform.GetChild(4).GetComponent<Text>();
        if (textComponent == null)
        {
            Debug.LogError("Invalid text component");
        }
        textComponent.text = sliderVCamStep.value.ToString();

        isDirty = true;
    }

    public void OnValueChangedCamHalfSphere()
    {
        isDirty = true;
    }

    public void OnValueChangedRandomizeCamPos()
    {
        sliderRadius.gameObject.SetActive(!toggleRandomizeCamPos.isOn);
        sliderHCamStep.gameObject.SetActive(!toggleRandomizeCamPos.isOn);
        sliderVCamStep.gameObject.SetActive(!toggleRandomizeCamPos.isOn);

        isDirty = true;
    }

    public void OnValueChangeLightIsOn()
    {
        toggleGroupLightType.transform.gameObject.SetActive(toggleLightIsOn.isOn);
        lightSource.GetComponent<UnityEngine.Light>().enabled = toggleLightIsOn.isOn;
    }

    public void OnValueChangeLightTypeGeneral()
    {
        if (toggleLightTypeGeneral.isOn == true)
        {
            lightSource.GetComponent<UnityEngine.Light>().type = UnityEngine.LightType.Directional;
        }
    }

    public void OnValueChangeLightTypeSpot()
    {
        if (toggleLightTypeSpot.isOn == true)
        {
            lightSource.GetComponent<UnityEngine.Light>().type = UnityEngine.LightType.Spot;
        }
    }

    public void OnValueChangeRandomizeTerrain()
    {
        terrain.gameObject.SetActive(toggleRandomizeTerrain.isOn);
        plane.SetActive(!toggleRandomizeTerrain.isOn);
    }

    public void OnValueChangePrimitive()
    {
        // Corrigido: antes comparava o objeto toggleSplat (sempre verdadeiro) em vez de .isOn
        if (toggleMesh.isOn) splatMode = false;
        else if (toggleSplat.isOn) splatMode = true;
        if (logger != null) logger.ClearCache();
        isDirty = true;
    }

    public void OnValueChangesliderPeaksHeight()
    {
        terrain.GetComponent<TerrainGenerator>().peakHeights = sliderPeaksHeight.value;
        sliderPeaksHeight.transform.GetChild(4).GetComponent<Text>().text = Math.Round(sliderPeaksHeight.value, 2).ToString(CultureInfo.InvariantCulture);
    }

    public void OnValueChangeDatasetSize()
    {
        sliderDatasetSize.value = Mathf.Round(sliderDatasetSize.value / 5) * 5;

        Text textComponent = sliderDatasetSize.transform.GetChild(4).GetComponent<Text>();
        if (textComponent == null)
        {
            Debug.LogError("Invalid text component");
        }
        textComponent.text = sliderDatasetSize.value.ToString();

        isDirty = true;
    }

    // Delay extra (opcional) após a espera de frames.
    public void OnValueChangeDelay()
    {
        sliderDelay.value = Mathf.Round(sliderDelay.value / 50) * 50;

        Text textComponent = sliderDelay.transform.GetChild(4).GetComponent<Text>();
        if (textComponent == null)
        {
            Debug.LogError("Invalid text component");
        }
        textComponent.text = sliderDelay.value.ToString() + " ms";
    }

    public void OnClickButtonAdvancedOptions()
    {
        panelCenter.SetActive(!panelCenter.activeSelf);
    }

    public void OnClickButtonNormalizeModels()
    {
        //Normalize non normalized Models
        gameObject.GetComponent<ModelNormalizer>().NormilizeResourcesModels();
    }

    public void OnValueChangeDropdownChooseModel()
    {
        RebuildDropDownOptions();

        if (toggleRandomizeModel.isOn == false && !splatMode)
        {
            gameObject.GetComponent<InsectImport>().destroyActualModel();
            gameObject.GetComponent<InsectImport>().InstantiateMeshModel(dropdownChooseModel.captionText.text);

            actualModel = GameObject.FindGameObjectWithTag("Model");

            if (actualModel != null)
            {
                cameraTarget = actualModel.transform;
            }
        }
        isDirty = true;
    }

    public void OnValueChangeDirectoryInputField()
    {
        datasetPath = DirectoryInputField.text;
    }

    void OnDestroy()
    {
        if (poseHelper != null) DestroyImmediate(poseHelper.gameObject);
    }
}
