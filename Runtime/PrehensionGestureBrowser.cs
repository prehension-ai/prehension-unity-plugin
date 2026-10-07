using UnityEngine;
using UnityEngine.UI;
using TMPro;

internal class PrehensionGestureBrowser : MonoBehaviour
{
    [Header("UI References")]
    public GameObject gestureButtonPrefab;
    public GameObject sampleButtonPrefab;
    public Transform listParent; // A Vertical Layout Group in your Canvas

    [Header("New Visualization Elements")]
    public Button recordButton;          // Assign your Record button from the Canvas
    public GameObject handVisualizer;    // A rigged hand model in your scene
    public TextMeshProUGUI statusText;   // A text field for "No data recorded"

    private PrehensionConfig config;
    private GameObject selectedSampleButton;

    void Awake()
    {
        config = GetComponentInParent<PrehensionDataRecorder>().config;
    }

    void Start()
    {
        // Hide everything on start
        recordButton.gameObject.SetActive(false);
        handVisualizer.SetActive(false);
        statusText.text = "";

        ShowGestures();
    }

    public void ShowGestures()
    {
        selectedSampleButton = null;
        handVisualizer.GetComponent<PrehensionSampleVisualizer>().ClearSample();
        handVisualizer.SetActive(false);
        ClearList();
        foreach (var gesture in config.gestures)
        {
            GameObject btn = Instantiate(gestureButtonPrefab, listParent);
            btn.GetComponentInChildren<TextMeshProUGUI>().text = gesture.name;

            // When clicked, show this gesture's samples
            btn.GetComponent<Button>().onClick.AddListener(() => ShowSamples(gesture));
        }
    }

    public void ShowSamples(PrehensionConfig.Gesture gesture)
    {
        ClearList();
        // Add a 'Back' button
        GameObject backBtn = Instantiate(gestureButtonPrefab, listParent);
        backBtn.GetComponentInChildren<TextMeshProUGUI>().text = "< Back";
        backBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            ClearSelection();
            ShowGestures();
        });

        for (int i = 0; i < gesture.samples.Count; i++)
        {
            int index = i; // Local copy for the closure
            var sample = gesture.samples[i];

            GameObject btn = Instantiate(sampleButtonPrefab, listParent);
            btn.GetComponentInChildren<TextMeshProUGUI>().text = $"Sample {index} ({(sample.active ? "Active" : "Inactive")})";

            btn.GetComponent<Button>().onClick.AddListener(() => SelectSample(btn, gesture, sample, index));
        }
    }

    void ClearSelection()
    {
        config.currentRecordingGestureName = null;
        config.currentRecordingGestureUuid = null;
        config.currentRecordingSampleIndex = 0;
        config.currentRecordingSampleUuid = null;

        recordButton.gameObject.SetActive(false);
    }

    void SelectSample(GameObject button, PrehensionConfig.Gesture gesture, PrehensionConfig.Sample sample, int index)
    {
        // Update selection border
        if (selectedSampleButton != null)
        {
            Outline prevOutline = selectedSampleButton.GetComponent<Outline>();
            if (prevOutline != null)
            {
                prevOutline.enabled = false;
            }
        }
        selectedSampleButton = button;
        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = true;
        }

        // mark current gesture and sample in config
        config.currentRecordingGestureName = gesture.name;
        config.currentRecordingGestureUuid = gesture.uuid;
        config.currentRecordingSampleIndex = index;
        config.currentRecordingSampleUuid = sample.uuid;

        Debug.Log($"[Prehension] VR Selection Updated: {gesture.name} - Sample {index}");
        
        // show the Record Button
        recordButton.gameObject.SetActive(true);

        // handle hand visualization
        UpdateHandVisualization(sample);
    }

    void ClearList()
    {
        foreach (Transform child in listParent)
        {
            // TODO fix 
            // point here is just to destroy our buttons and not the canvas stuff
            string childName = child.gameObject.name;
            if(childName.Contains("Clone") && (childName.Contains(gestureButtonPrefab.name) || childName.Contains(sampleButtonPrefab.name)))
            {
                Destroy(child.gameObject);
            }
        }
    }

    void UpdateHandVisualization(PrehensionConfig.Sample sample)
    {
        // Check if data exists
        if (sample.hasData)
        {
            statusText.text = ""; // Clear "No data"
            handVisualizer.SetActive(true);

            // Pass the data to a helper script on the hand model
            handVisualizer.GetComponent<PrehensionSampleVisualizer>().SetSampleData(sample.uuid);
        }
        else
        {
            statusText.text = "No data recorded";
            handVisualizer.SetActive(false);
        }
    }
}