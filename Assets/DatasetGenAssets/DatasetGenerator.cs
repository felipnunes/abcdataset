using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

public class DatasetGenerator : MonoBehaviour
{
    private string[] modelFileNames;

    // public enum CameraMode { Random, Step };

    [Header("Config")]
    public string datasetPath;

    private string shadedPath = "/Sketch";
    private string sketchPath = "/Shaded";

    private string mainRandomizationParameters;

    public Camera cameraShaded;
    public Camera cameraSketch;

    private int charactereLimit = 200;

    public Transform cameraTarget;

    private float hCamAngle = 0, vCamAngle = 0;

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
    public GameObject skyAndFog;

    [Header("Terrain elements")]
    public Terrain terrain;
    public GameObject plane;

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
    public int cameraPerturbationMaxAngle;
    private List<Transform> cameraGizmos = new List<Transform>();
    public int lightPerturbationMaxAngle;
    public int skyLuxMinValue;
    public int skyLuxMaxValue;

    // Start is called before the first frame update
    void Start()
    {

        DirectoryInputField.characterLimit = charactereLimit;



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
        toggleLightIsOn.onValueChanged.AddListener(delegate { OnValueChangeLightIsOn(); });
        toggleLightTypeGeneral.onValueChanged.AddListener(delegate { OnValueChangeLightTypeGeneral(); });
        toggleLightTypeSpot.onValueChanged.AddListener(delegate { OnValueChangeLightTypeSpot(); });
        toggleRandomizeTerrain.onValueChanged.AddListener(delegate { OnValueChangeRandomizeTerrain(); });
        sliderPeaksHeight.onValueChanged.AddListener(delegate { OnValueChangesliderPeaksHeight(); });
        sliderDatasetSize.onValueChanged.AddListener(delegate { OnValueChangeDatasetSize(); });
        sliderDelay.onValueChanged.AddListener(delegate { OnValueChangeDelay(); });
        buttonGenerateDataset.onClick.AddListener(delegate { OnClickButtonGenerate(); });
        buttonAdvancedOptions.onClick.AddListener(delegate { OnClickButtonAdvancedOptions(); });
        buttonNormalizeModels.onClick.AddListener(delegate { OnClickButtonNormalizeModels(); });
        dropdownChooseModel.onValueChanged.AddListener(delegate { OnValueChangeDropdownChooseModel(); });
        DirectoryInputField.onValueChanged.AddListener(delegate { OnValueChangeDirectoryInputField(); } );

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


        var imgPathShaded = Path.Combine(datasetPath + shadedPath, actualModel.name.Replace("(Clone)", "-") + mainRandomizationParameters + timeStamp + ".png");
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

    private void RebuildDropDownOptions()
    {
        modelFileNames = this.GetComponent<InsectImport>().modelNames;

        dropdownChooseModel.options.Clear();
        foreach (string modelFileName in modelFileNames)
        {
            dropdownChooseModel.options.Add(new Dropdown.OptionData() { text = modelFileName });
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
                AddCameraPerturbtion(curTransform);
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

    private void AddCameraPerturbtion(Transform curTransform)
    {
        curTransform.Rotate(UnityEngine.Random.Range((float) - cameraPerturbationMaxAngle, (float) cameraPerturbationMaxAngle),
                            UnityEngine.Random.Range((float) - cameraPerturbationMaxAngle, (float)cameraPerturbationMaxAngle),
                            UnityEngine.Random.Range((float) - cameraPerturbationMaxAngle, (float)cameraPerturbationMaxAngle));
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
        //skyAndFog.GetComponent<HDRISky>().desiredLuxValue.value = UnityEngine.Random.Range((float)skyLuxMinValue, (float)skyLuxMaxValue);
       

        if (lightSource.GetComponent<Light>().type.ToString().Equals("Spot"))
        {
            //lightSource.GetComponent<Light>().colorTemperature = UnityEngine.Random.Range(1500f, 20000f);
            lightSource.GetComponent<Light>().intensity = UnityEngine.Random.Range(500f, 1200f);
            lightSource.transform.position = new Vector3(UnityEngine.Random.Range(-2, 2), lightSource.transform.position.y, UnityEngine.Random.Range(-2, 2));
            lightSource.transform.LookAt(actualModel.transform);
        }
        else if (lightSource.GetComponent<Light>().type.ToString().Equals("Directional"))
        {
            lightSource.transform.LookAt(actualModel.transform);
            lightSource.transform.Rotate(UnityEngine.Random.Range((float) - lightPerturbationMaxAngle, (float)lightPerturbationMaxAngle),
                                         UnityEngine.Random.Range((float) - lightPerturbationMaxAngle, (float)lightPerturbationMaxAngle),
                                         UnityEngine.Random.Range((float) - lightPerturbationMaxAngle, (float)lightPerturbationMaxAngle));
            
        }
    }

    private void RandomizeTerrain()
    {
        if (terrain.enabled == true)
        {
            terrain.terrainData.SetHeights(0, 0, terrain.GetComponent<TerrainGenerator>().GenerateNoise());
            terrain.GetComponent<TerrainGenerator>().RandomizeTexture();
        }
        else if (plane.activeSelf == true)
        {
            
        }
        
    }
    private void RandomizePlane()
    {
        plane.GetComponent<PlaneGenerator>().RandomizeTexture();
    }

    private void ActualizeRandomizationParameters()
    {

        //Debug.Log("light is on = " + toggleLightIsOn + "  RandomizeTerrain = " + toggleRandomizeTerrain.isOn);
        //Debug.Log(Mathf.Floor((3f / 6f * sliderDatasetSize.value)) + " " + indexOfCurrentImage);

        //Plane + No Light
        if (indexOfCurrentImage == 0 || indexOfCurrentImage == sliderDatasetSize.value)
        {
            toggleLightIsOn.isOn = false;
            toggleRandomizeTerrain.isOn = false;
            Debug.Log("light is on = " + toggleLightIsOn + "  RandomizeTerrain = " + toggleRandomizeTerrain.isOn);

            sliderDelay.value = 150;

            mainRandomizationParameters = "P-NL";
        }

        //Terrain + No Light
        if (Mathf.Floor((1f / 6f * sliderDatasetSize.value)) == indexOfCurrentImage)
        {
            toggleLightIsOn.isOn = false;
            toggleRandomizeTerrain.isOn = true;

            sliderDelay.value = 850;

            mainRandomizationParameters = "T-NL";
        }

        //Plane + General Light
        if (Mathf.Floor((2f / 6f * sliderDatasetSize.value)) == indexOfCurrentImage)
        {
            toggleLightIsOn.isOn = true;
            toggleLightTypeGeneral.isOn = true;
            toggleRandomizeTerrain.isOn = false;

            sliderDelay.value = 200;

            mainRandomizationParameters = "P-GL";
        }

        //Terrain + General Light
        if (Mathf.Floor((3f / 6f * sliderDatasetSize.value)) == indexOfCurrentImage)
        {
            toggleLightIsOn.isOn = true;
            toggleLightTypeGeneral.isOn = true;
            toggleRandomizeTerrain.isOn = true;

            sliderDelay.value = 800;

            mainRandomizationParameters = "T-GL";
        }

        //Plane + Spot Light
        if (Mathf.Floor((4f / 6f * sliderDatasetSize.value)) == indexOfCurrentImage)
        {
            toggleLightIsOn.isOn = true;
            toggleLightTypeSpot.isOn = true;
            toggleRandomizeTerrain.isOn = false;

            sliderDelay.value = 200;

            mainRandomizationParameters = "P-SL";

        }

        //Terrain + Spot Light
        if (Mathf.Floor((5f / 6f * sliderDatasetSize.value)) == indexOfCurrentImage)
        {
            toggleLightIsOn.isOn = true;
            toggleLightTypeSpot.isOn = true;
            toggleRandomizeTerrain.isOn = true;

            sliderDelay.value = 800;

            mainRandomizationParameters = "T-SL";
        }
    }

    // Update is called once per frame
    void Update()
    {
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

        if (isGenerating)
        {
            // Check delay between images
            if ((Time.time - timeOfLastSave) * 1000f > sliderDelay.value)
            {
                ActualizeRandomizationParameters();
                
                SetCameraTransform(cameraPositionRotation[indexOfCurrentImage]);


                if (toggleRandomizeLightPos.isOn)
                {
                    //randomize light position
                    RandomizeLight();
                }

                if (toggleRandomizeTerrain.isOn)
                {
                    RandomizeTerrain();
               
                }
                else
                {
                    RandomizePlane();
                }

                //randomize current HDRI image
                skyAndFog.GetComponent<HDRIRandomizer>().RandomizeHDRISky();



                StartCoroutine(TakePhoto());
                //Instantiate a new model and set it as cameraTarget
                if (toggleRandomizeModel.isOn)
                {
                    RandomizeModel();
                }
                else
                {
                    this.GetComponent<InsectImport>().AddMaterial(actualModel);
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

    public void OnValueChangeLightIsOn()
    {
        if(toggleLightIsOn.isOn == true)
        {
            toggleGroupLightType.transform.gameObject.SetActive(true);
            lightSource.GetComponent<Light>().enabled = true;
            return;
        }
        else
        {
            toggleGroupLightType.transform.gameObject.SetActive(false);
        }
        lightSource.GetComponent<Light>().enabled = false;
    }


    public void OnValueChangeLightTypeGeneral()
    {
        if (toggleLightTypeGeneral.isOn == true)
        {
            lightSource.GetComponent<Light>().type = LightType.Directional;
        }
    }

    public void OnValueChangeLightTypeSpot()
    {
        if (toggleLightTypeSpot.isOn == true)
        {
            lightSource.GetComponent<Light>().type = LightType.Spot;
        }
    }

    public void OnValueChangeRandomizeTerrain()
    {
        if (toggleRandomizeTerrain.isOn == true)
        {
            terrain.gameObject.SetActive(true);
            plane.SetActive(false);
        }
        else
        {
            terrain.gameObject.SetActive(false);
            plane.SetActive(true);
        }
    }

    public void OnValueChangesliderPeaksHeight()
    {
        
        terrain.GetComponent<TerrainGenerator>().peakHeights = sliderPeaksHeight.value;
        sliderPeaksHeight.transform.GetChild(4).GetComponent<Text>().text = Math.Round(sliderPeaksHeight.value, 2).ToString();
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

    public void OnValueChangeDropdownChooseModel()
    {

        RebuildDropDownOptions();

        if (toggleRandomizeModel.isOn == false)
        {


            gameObject.GetComponent<InsectImport>().destroyActualModel();
            
            gameObject.GetComponent<InsectImport>().InstantiateModel(dropdownChooseModel.captionText.text);

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
        datasetPath = DirectoryInputField.textComponent.text;
    }

    
}
