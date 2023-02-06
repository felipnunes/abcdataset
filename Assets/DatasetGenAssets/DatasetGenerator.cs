using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

public class DatasetGenerator : MonoBehaviour
{
    // public enum CameraMode { Random, Step };

    [Header("Config")]
    public string datasetPath;

    private string shadedPath = "/Sketch";
    private string sketchPath = "/Shaded";

    public Camera cameraShaded;
    public Camera cameraSketch;

    public Transform cameraTarget;
    // public CameraMode cameraUpdateMode; // Can now be read/written from/to toggleRandomizeCamPos.isOn

    // public float hCamStep = 20, vCamStep = 45; //Vertical Step angle [0-90]; horizontal Step angle[0-180]
    // public bool camHalfSphere = false; //Render only the top view of a half sphere
    // public float radius = 4.0f; // radius (distance) around object

    // hCamStep, vCamStep, camHalfSphere and radius are now sliderVCamStep.value, sliderHCamStep.value,
    // toggleCamHalfSphere.isOn, and sliderRadius.value

    private float hCamAngle = 0, vCamAngle = 0;

    [Header("UI elements")]
    public Slider sliderRadius;
    public Slider sliderHCamStep;
    public Slider sliderVCamStep;
    public Toggle toggleCamHalfSphere;
    public Toggle toggleRandomizeCamPos;
    public Toggle toggleRandomizeLightPos;
    public Slider sliderDatasetSize;
    public Slider sliderDelay;
    public Button buttonGenerateDataset;
    public Button buttonAdvancedOptions;
    public Text progressText;
    public GameObject panelCenter;

    [Header("UI elements advanced options")]
    public Toggle toggleRandomizeModel;
    public Button buttonNormalizeModels;

    private RenderTexture renderTextureShaded;
    private RenderTexture renderTextureSketch;
    private Texture2D bufferedTexShaded;
    private Texture2D bufferedTexSketch;

    private bool isGenerating;
    private bool shouldReplaceModel;
    private int indexOfCurrentImage;
    private float timeOfLastSave;

    [Header("Light elements")]
    public GameObject lightSource;

    private GameObject actualModel;


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

    // List of camera positions/rotations that will be used to generate the dataset
    [HideInInspector]
    public List<PositionRotation> cameraPositionRotation = new List<PositionRotation>();
    // Whether cameraPositionRotation need to be updated
    private bool isDirty = true;

    [Header("Camera/light gizmos")]
    public Transform gizmoParent;
    public GameObject gizmoPrefab;
    private List<Transform> cameraGizmos = new List<Transform>();

    // Start is called before the first frame update
    void Start()
    {
        if (cameraShaded == null)
        {
            Debug.LogError("Invalid camera for shaded object");
        }
        renderTextureShaded = cameraShaded.targetTexture;
        bufferedTexShaded = new Texture2D(renderTextureShaded.width, renderTextureShaded.height, TextureFormat.RGB24, false);

        if (cameraSketch == null)
        {
            Debug.LogError("Invalid camera for sketch object");
        }
        renderTextureSketch = cameraSketch.targetTexture;
        bufferedTexSketch = new Texture2D(renderTextureSketch.width, renderTextureSketch.height, TextureFormat.RGB24, false);

        logger = GetComponent<Logger>();

        // Set up callbacks for UI events
        sliderRadius.onValueChanged.AddListener(delegate { OnValueChangedRadius(); });
        sliderHCamStep.onValueChanged.AddListener(delegate { OnValueChangedHCamStep(); });
        sliderVCamStep.onValueChanged.AddListener(delegate { OnValueChangedVCamStep(); });
        toggleCamHalfSphere.onValueChanged.AddListener(delegate { OnValueChangedCamHalfSphere(); });
        toggleRandomizeCamPos.onValueChanged.AddListener(delegate { OnValueChangedRandomizeCamPos(); });
        sliderDatasetSize.onValueChanged.AddListener(delegate { OnValueChangeDatasetSize(); });
        sliderDelay.onValueChanged.AddListener(delegate { OnValueChangeDelay(); });
        buttonGenerateDataset.onClick.AddListener(delegate { OnClickButtonGenerate(); });
        buttonAdvancedOptions.onClick.AddListener(delegate { OnClickButtonAdvancedOptions(); });
        buttonNormalizeModels.onClick.AddListener(delegate { OnClickButtonNormalizeModels();  });

        // Force updating the labels of the sliders
        OnValueChangedRadius();
        OnValueChangedHCamStep();
        OnValueChangedVCamStep();
        OnValueChangeDatasetSize();
        OnValueChangeDelay();
    }

    IEnumerator TakePhoto()
    {
        // Wait for two frames to allow the object to finish instantiating
        yield return null;
        yield return null;

        // Take the photo
        SaveTexture();
    }

    void SaveTexture()
    {
        DateTime localDate = DateTime.Now;
        string timeStamp = localDate.ToString("yyyy-MM-dd HH-mm-ss-ffff");

        RenderTexture.active = renderTextureShaded;
        bufferedTexShaded.ReadPixels(new Rect(0, 0, RenderTexture.active.width, RenderTexture.active.height), 0, 0);
        bufferedTexShaded.Apply();
        RenderTexture.active = null;
        var imgPathShaded = Path.Combine(datasetPath + shadedPath, timeStamp + "-shaded.png");
        File.WriteAllBytes(imgPathShaded, bufferedTexShaded.EncodeToPNG());

        RenderTexture.active = renderTextureSketch;
        bufferedTexSketch.ReadPixels(new Rect(0, 0, RenderTexture.active.width, RenderTexture.active.height), 0, 0);
        bufferedTexSketch.Apply();
        RenderTexture.active = null;
        var imgPathSketch = Path.Combine(datasetPath + sketchPath, timeStamp + "-sketch.png");
        File.WriteAllBytes(imgPathSketch, bufferedTexSketch.EncodeToPNG());

        progressText.text = "Generating " + (indexOfCurrentImage + 1) +
                            " of " + sliderDatasetSize.value + " ...\n";
        progressText.text += "Shaded image: " + imgPathShaded + "\n" +
                             "Sketch image: " + imgPathSketch + "\n";

        if (logger)
        {
            logger.LogSample(Path.GetFileName(imgPathShaded), "shaded", cameraShaded.transform);
            logger.LogSample(Path.GetFileName(imgPathSketch), "sketch", cameraSketch.transform);
        }
    }

    void RebuildTransforms()
    {
        hCamAngle = 0;
        vCamAngle = 0;
        cameraPositionRotation.Clear();


        GameObject dummyGO = new GameObject();
        dummyGO.name = "teste";

        if (actualModel != null)
        {
            cameraTarget = actualModel.transform;
        }

        for (int i = 0; i < sliderDatasetSize.value; ++i)
        {
            Transform curTransform = dummyGO.transform;

            if (toggleRandomizeCamPos.isOn)
            {
                curTransform.position = new Vector3(
                    UnityEngine.Random.Range(-sliderRadius.value, sliderRadius.value),
                    UnityEngine.Random.Range(toggleCamHalfSphere.isOn ? 0f : -sliderRadius.value, sliderRadius.value),
                    UnityEngine.Random.Range(-sliderRadius.value, sliderRadius.value));
                curTransform.LookAt(cameraTarget);
                if (cameraTarget == null)
                {
                    Debug.LogError("CameraTarget is null");
                }
            }
            else
            {
                curTransform.position = cameraTarget.transform.position + new Vector3(0.0f, 0.0f, -sliderRadius.value);
                curTransform.LookAt(cameraTarget);
                curTransform.RotateAround(cameraTarget.transform.position, Vector3.up, hCamAngle);
                curTransform.RotateAround(cameraTarget.transform.position, curTransform.right, vCamAngle);

                hCamAngle += sliderHCamStep.value;
                if (hCamAngle >= 360.0f)
                {
                    if (toggleCamHalfSphere.isOn)
                    {
                        vCamAngle += sliderVCamStep.value;
                    }
                    else if (vCamAngle > 0)
                    {
                        vCamAngle = -vCamAngle;
                    }
                    else
                    {
                        vCamAngle = -vCamAngle + sliderVCamStep.value;
                    }
                    hCamAngle = 0;
                }

                if (vCamAngle > 90.0f || vCamAngle < -90.0f)
                { //reset angle to prevent "upside down" camera
                    vCamAngle = 0;
                }
            }

            cameraPositionRotation.Add(new PositionRotation(curTransform.position, curTransform.rotation));
        }

        Destroy(dummyGO);
        hCamAngle = 0;
        vCamAngle = 0;

        // Update gizmos
        if (gizmoParent != null && gizmoPrefab != null)
        {
            // Destroy existing gizmos
            // TODO: reuse existing gizmos for better efficiency
            foreach (Transform child in gizmoParent.transform)
                Destroy(child.gameObject);

            // Instantiate new gizmos
            for (int i = 0; i < cameraPositionRotation.Count; ++i)
            {
                var t = cameraPositionRotation[i];
                GameObject GO = GameObject.Instantiate(gizmoPrefab, t.Position, t.Rotation, gizmoParent);
                GO.name = "Gizmo_" + i;
            }
        }
    }

    // Set the transform of the shaded and sketch cameras to the given transform
    void SetCameraTransform(PositionRotation pr)
    {
        cameraSketch.transform.position = pr.Position;
        cameraSketch.transform.rotation = pr.Rotation;

        cameraShaded.transform.position = pr.Position;
        cameraShaded.transform.rotation = pr.Rotation;
    }

    private void RandomizeModel()
    {
        
        gameObject.GetComponent<InsectImport>().destroyActualModel();
        gameObject.GetComponent<InsectImport>().InstantiateRandomModel();
        if (actualModel != null)
        {
            cameraTarget = actualModel.transform;
        }
    }

    private void RandomizeLight()
    {
        if (lightSource.GetComponent<Light>().type.ToString().Equals("Spot"))
        {
            //lightSource.GetComponent<Light>().colorTemperature = UnityEngine.Random.Range(1500f, 20000f);
            lightSource.GetComponent<Light>().intensity = UnityEngine.Random.Range(50f, 200f);
            lightSource.transform.position = new Vector3(UnityEngine.Random.Range(-2, 2), lightSource.transform.position.y, UnityEngine.Random.Range(-2, 2));
            lightSource.transform.LookAt(actualModel.transform);
        }
    }

    // Update is called once per frame
    void Update()
    {
        actualModel = GameObject.FindGameObjectWithTag("Model");

        if (isDirty)
        {
            
            RebuildTransforms();
            isDirty = false;

            
        }

        if (isGenerating)
        {
            // Check delay between images
            if ((Time.time - timeOfLastSave) * 1000f > sliderDelay.value)
            {
                
                SetCameraTransform(cameraPositionRotation[indexOfCurrentImage]);


                if (toggleRandomizeLightPos.isOn)
                {
                    //randomize light position
                    RandomizeLight();
                    
           

                }

                StartCoroutine(TakePhoto());

                //Instantiate a new model and set it as cameraTarget
                if (toggleRandomizeModel.isOn)
                {
                    RandomizeModel();
                }

                // Go to next image
                indexOfCurrentImage++;
                if (indexOfCurrentImage == sliderDatasetSize.value)
                {
                    Reset();
                }

                timeOfLastSave = Time.time;

            }

            
        }
        
    }

    private void LateUpdate()
    {
        
    }

    void Reset()
    {
        isGenerating = false;
        SetEnabledUIElements(true);
        buttonGenerateDataset.GetComponentInChildren<Text>().text =
            "Generate dataset";
        progressText.text = "";

        hCamAngle = 0;
        vCamAngle = 0;

        logger.CloseLog();
    }

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

    // OnValueChange event of "Distance from target" slider
    public void OnValueChangedRadius()
    {
        sliderRadius.value = Mathf.Round(sliderRadius.value / .1f) * .1f;

        Text textComponent = sliderRadius.transform.GetChild(4).GetComponent<Text>();
        if (textComponent == null)
        {
            Debug.LogError("Invalid text component");
        }
        textComponent.text = sliderRadius.value.ToString();

        isDirty = true;
    }

    // OnValueChange event of "Horizontal step angle" slider
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

    // OnValueChange event of "Vertical step angle" slider
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


    // OnValueChange event of "Render half sphere only" checkbox
    public void OnValueChangedCamHalfSphere()
    {
        isDirty = true;
    }

    // OnValueChange event of "Randomize camera position" checkbox
    public void OnValueChangedRandomizeCamPos()
    {
        sliderRadius.gameObject.SetActive(!toggleRandomizeCamPos.isOn);
        sliderHCamStep.gameObject.SetActive(!toggleRandomizeCamPos.isOn);
        sliderVCamStep.gameObject.SetActive(!toggleRandomizeCamPos.isOn);

        isDirty = true;
    }

    // OnValueChange event of "Dataset size" slider
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

    // OnValueChange event of "Delay" slider
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

    // OnClick event of "Generate dataset" button
    public void OnClickButtonGenerate()
    {
        if (!isGenerating)
        {
            buttonGenerateDataset.GetComponentInChildren<Text>().text = "Stop";
            isGenerating = true;
            indexOfCurrentImage = 0;
            SetEnabledUIElements(false);
        }
        else
        {
            buttonGenerateDataset.GetComponentInChildren<Text>().text = "Generate dataset";
            isGenerating = false;
            SetEnabledUIElements(true);
            progressText.text = "";
        }
    }

    public void OnClickButtonAdvancedOptions()
    {
        if (!panelCenter.activeSelf)
        {
            panelCenter.SetActive(true);
        }
        else
        {
            panelCenter.SetActive(false);
        }
    }

    public void OnClickButtonNormalizeModels()
    {
        //Normalize non normalized Models
        gameObject.GetComponent<ModelNormalizer>().NormilizeResourcesModels();
    }

}
