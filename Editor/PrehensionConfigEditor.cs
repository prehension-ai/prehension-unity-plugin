using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR.OpenXR.NativeTypes;
using UnityEngine.Rendering;
using UnityEditor.PackageManager;

[CustomEditor(typeof(PrehensionConfig))]
internal class PrehensionConfigEditor : Editor
{
    private ReorderableList gestureList;
    private PrehensionAPIClient apiClient;
    private Dictionary<string, ReorderableList> sampleLists = new Dictionary<string, ReorderableList>();

    // Cached dropdown option lists — rebuilt only when a gesture is added, removed, renamed, or changes handedness
    private bool _optionListsDirty = true;
    private List<string> _rightGestureNames = new List<string>();
    private List<string> _rightGestureUuids = new List<string>();
    private List<string> _leftGestureNames  = new List<string>();
    private List<string> _leftGestureUuids  = new List<string>();
    private List<string> _allGestureNames   = new List<string>();
    private List<string> _allGestureUuids   = new List<string>();

    // Foldout state per gesture UUID — collapsed by default
    private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();
    // Foldout state for each gesture's samples list, keyed by samples property path
    private Dictionary<string, bool> _samplesFoldoutStates = new Dictionary<string, bool>();

    private void OnEnable()
    {
        SerializedProperty gesturesProp = serializedObject.FindProperty("gestures");

        gestureList = new ReorderableList(
            serializedObject,
            gesturesProp,
            draggable: true,
            displayHeader: true,
            displayAddButton: true,
            displayRemoveButton: true);

        gestureList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "gestures");
        };

        gestureList.elementHeightCallback = index =>
        {
            SerializedProperty element = gesturesProp.GetArrayElementAtIndex(index);
            string uuid = element.FindPropertyRelative("uuid").stringValue;
            bool expanded = _foldoutStates.TryGetValue(uuid, out bool val) && val;

            float lineH = EditorGUIUtility.singleLineHeight + 6;
            if (!expanded) return lineH;

            SerializedProperty samplesProp = element.FindPropertyRelative("samples");
            bool samplesExpanded = _samplesFoldoutStates.TryGetValue(samplesProp.propertyPath, out bool sv) && sv;
            float samplesHeight = lineH + (samplesExpanded ? GetSampleList(element, samplesProp).GetHeight() : 0f);
            return 10 * lineH + samplesHeight;
        };

        gestureList.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            SerializedProperty element = gesturesProp.GetArrayElementAtIndex(index);
            SerializedProperty uuidProp = element.FindPropertyRelative("uuid");
            string uuid = uuidProp.stringValue;
            bool expanded = _foldoutStates.TryGetValue(uuid, out bool foldVal) && foldVal;

            rect.y += 2;

            // Header row: foldout toggle on the left, editable name field to the right.
            // Offset by drag handle width so the foldout arrow doesn't sit under the hamburger.
            const float dragHandleWidth = 20f;
            const float foldoutWidth = 14f;
            bool newExpanded = EditorGUI.Foldout(
                new Rect(rect.x + dragHandleWidth, rect.y, foldoutWidth, EditorGUIUtility.singleLineHeight),
                expanded, GUIContent.none, true);
            if (newExpanded != expanded)
            {
                _foldoutStates[uuid] = newExpanded;
                Repaint();
            }

            SerializedProperty nameProp = element.FindPropertyRelative("name");
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(
                new Rect(rect.x + dragHandleWidth + foldoutWidth, rect.y, rect.width - dragHandleWidth - foldoutWidth, EditorGUIUtility.singleLineHeight),
                nameProp, GUIContent.none);
            if (EditorGUI.EndChangeCheck()) _optionListsDirty = true;

            if (!expanded) return;

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // UUID
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                uuidProp.stringValue);

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Handedness
            SerializedProperty handednessProp = element.FindPropertyRelative("handedness");
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                handednessProp, new GUIContent("Handedness"));
            if (EditorGUI.EndChangeCheck()) _optionListsDirty = true;

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Active
            SerializedProperty activeProp = element.FindPropertyRelative("active");
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                activeProp, new GUIContent("Active: "));

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Rapid re-fire
            SerializedProperty rapidReFireProp = element.FindPropertyRelative("rapidReFire");
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                rapidReFireProp, new GUIContent("Rapid Re-fire", "If enabled, this gesture can immediately start building again after firing — no null frames required. Opposite gesture protection still applies."));

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Fire and hold
            SerializedProperty isHoldGestureProp = element.FindPropertyRelative("isHoldGesture");
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                isHoldGestureProp, new GUIContent("Fire and Hold", "If enabled, a GestureReleased event fires when the pose drops. Competing gestures must sustain their full build frames to end the hold; brief blips are tolerated."));

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Static pose
            SerializedProperty isStaticPoseProp = element.FindPropertyRelative("isStaticPose");
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                isStaticPoseProp, new GUIContent("Static Pose", "If enabled, this gesture is defined purely by hand shape — position and movement are ignored. The backend augments and trains it as a pose rather than a motion."));

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Build frames override
            SerializedProperty buildFramesOverrideProp = element.FindPropertyRelative("buildFramesOverride");
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                buildFramesOverrideProp, new GUIContent("Build Frames Override", "Override the global build frames for this gesture. 0 uses the global value."));

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Mirror data from — opposite-handedness gestures from cached lists
            SerializedProperty mirrorProp = element.FindPropertyRelative("mirrorFromGestureUuid");
            PrehensionConfig.Handedness currentHandedness = (PrehensionConfig.Handedness)handednessProp.enumValueIndex;
            bool isRight = currentHandedness == PrehensionConfig.Handedness.Right;
            List<string> mirrorSourceNames = isRight ? _leftGestureNames : _rightGestureNames;
            List<string> mirrorSourceUuids = isRight ? _leftGestureUuids : _rightGestureUuids;

            var mirrorOptionNames = new List<string> { "None" };
            var mirrorOptionUuids = new List<string> { "" };
            mirrorOptionNames.AddRange(mirrorSourceNames);
            mirrorOptionUuids.AddRange(mirrorSourceUuids);

            int currentMirrorIdx = mirrorOptionUuids.IndexOf(mirrorProp.stringValue);
            if (currentMirrorIdx < 0)
            {
                currentMirrorIdx = 0;
                mirrorProp.stringValue = "";
            }
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, labelWidth, EditorGUIUtility.singleLineHeight),
                new GUIContent("Mirror data from", "If selected, model will train with mirrored samples from another gesture"));
            int newMirrorIdx = EditorGUI.Popup(
                new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, EditorGUIUtility.singleLineHeight),
                currentMirrorIdx, mirrorOptionNames.ToArray());
            if (newMirrorIdx != currentMirrorIdx)
                mirrorProp.stringValue = mirrorOptionUuids[newMirrorIdx];

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Opposite gesture — same-handedness gestures except self, from cached lists
            SerializedProperty oppositeProp = element.FindPropertyRelative("oppositeGestureUuid");
            List<string> sameHandNames = isRight ? _rightGestureNames : _leftGestureNames;
            List<string> sameHandUuids = isRight ? _rightGestureUuids : _leftGestureUuids;
            var optionNames = new List<string> { "None" };
            var optionUuids = new List<string> { "" };
            for (int i = 0; i < sameHandUuids.Count; i++)
            {
                if (sameHandUuids[i] == uuid) continue;
                optionNames.Add(sameHandNames[i]);
                optionUuids.Add(sameHandUuids[i]);
            }

            int currentOppIdx = optionUuids.IndexOf(oppositeProp.stringValue);
            if (currentOppIdx < 0) currentOppIdx = 0;
            int newOppIdx = EditorGUI.Popup(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                "Opposite", currentOppIdx, optionNames.ToArray());
            if (newOppIdx != currentOppIdx)
                oppositeProp.stringValue = optionUuids[newOppIdx];

            rect.y += EditorGUIUtility.singleLineHeight + 4;

            // Samples sub-list with collapsible header
            SerializedProperty samplesProp = element.FindPropertyRelative("samples");
            string samplesKey = samplesProp.propertyPath;
            bool samplesExpanded = _samplesFoldoutStates.TryGetValue(samplesKey, out bool sv) && sv;
            bool newSamplesExpanded = EditorGUI.Foldout(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                samplesExpanded, $"Samples ({samplesProp.arraySize})", true);
            if (newSamplesExpanded != samplesExpanded)
            {
                _samplesFoldoutStates[samplesKey] = newSamplesExpanded;
                Repaint();
            }
            if (newSamplesExpanded)
            {
                rect.y += EditorGUIUtility.singleLineHeight + 4;
                GetSampleList(element, samplesProp).DoList(rect);
            }
        };

        gestureList.onAddCallback = list =>
        {
            int index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            list.index = index;
            InitializeNewGesture(list.serializedProperty.GetArrayElementAtIndex(index));
            _optionListsDirty = true;
        };

        gestureList.onRemoveCallback = list =>
        {
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            _optionListsDirty = true;
        };

        GameObject prehensionAPI = GameObject.Find("PrehensionAPIGameObject");
        if(prehensionAPI)
        {
            apiClient = prehensionAPI.GetComponent<PrehensionAPIClient>();
        }
        else
        {
            prehensionAPI = new GameObject("PrehensionAPIGameObject");
            apiClient = prehensionAPI.AddComponent<PrehensionAPIClient>();
            apiClient.config = (PrehensionConfig)target;
        }
    }

    private void OnDisable()
    {
        if (apiClient != null)
        {
            // this can only be used in Editor so you probably want to check that somehow
            DestroyImmediate(apiClient.gameObject);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (_optionListsDirty)
            RebuildOptionLists(serializedObject.FindProperty("gestures"));

        gestureList.DoLayoutList();

        DrawNullGestureDropdown(serializedObject.FindProperty("nullRightGestureUuid"), PrehensionConfig.Handedness.Right, "Null Gesture (Right)");
        DrawNullGestureDropdown(serializedObject.FindProperty("nullLeftGestureUuid"),  PrehensionConfig.Handedness.Left,  "Null Gesture (Left)");

        serializedObject.ApplyModifiedProperties();

        if(GUILayout.Button("Print Config Values"))
        {
            PrintConfigValues();
        }

        if(GUILayout.Button("Sync Gestures To Database"))
        {
            Debug.Log("[Prehension] Beginning gesture sync");
            apiClient.PrehensionAPI_SyncGesturesToDatabase();
        }

        bool jobInProgress = !string.IsNullOrEmpty(SessionState.GetString(PrehensionAPIClient.PendingJobSessionKey, ""));
        EditorGUI.BeginDisabledGroup(jobInProgress);
        if(GUILayout.Button(jobInProgress ? "Generate Model (In Progress...)" : "Generate Model"))
        {
            PrehensionConfig config = (PrehensionConfig)target;
            List<string> dirtyGestureNames = new List<string>();
            foreach (PrehensionConfig.Gesture gesture in config.gestures)
            {
                foreach (PrehensionConfig.Sample sample in gesture.samples)
                {
                    if (sample.dirtyToDB)
                    {
                        dirtyGestureNames.Add(gesture.name);
                        break;
                    }
                }
            }

            if (dirtyGestureNames.Count > 0)
            {
                Debug.LogWarning(
                    "[Prehension] The following gestures have samples that haven't been synced to the database: " +
                    string.Join(", ", dirtyGestureNames) +
                    ". Consider syncing before generating a model."
                );
            }

            apiClient.PrehensionAPI_GenerateModelFromDatabase();
        }
        EditorGUI.EndDisabledGroup();
    }

    // UTILITY
    private void RebuildOptionLists(SerializedProperty gesturesProp)
    {
        _rightGestureNames.Clear(); _rightGestureUuids.Clear();
        _leftGestureNames.Clear();  _leftGestureUuids.Clear();
        _allGestureNames.Clear();   _allGestureUuids.Clear();

        for (int i = 0; i < gesturesProp.arraySize; i++)
        {
            SerializedProperty g = gesturesProp.GetArrayElementAtIndex(i);
            string name = g.FindPropertyRelative("name").stringValue;
            string uuid = g.FindPropertyRelative("uuid").stringValue;
            var handedness = (PrehensionConfig.Handedness)g.FindPropertyRelative("handedness").enumValueIndex;

            _allGestureNames.Add(name);
            _allGestureUuids.Add(uuid);

            if (handedness == PrehensionConfig.Handedness.Right)
            {
                _rightGestureNames.Add(name);
                _rightGestureUuids.Add(uuid);
            }
            else
            {
                _leftGestureNames.Add(name);
                _leftGestureUuids.Add(uuid);
            }
        }

        _optionListsDirty = false;
    }

    private void DrawNullGestureDropdown(SerializedProperty nullUuidProp, PrehensionConfig.Handedness handedness, string label)
    {
        List<string> sourceNames = handedness == PrehensionConfig.Handedness.Right ? _rightGestureNames : _leftGestureNames;
        List<string> sourceUuids = handedness == PrehensionConfig.Handedness.Right ? _rightGestureUuids : _leftGestureUuids;

        if (sourceNames.Count == 0)
        {
            EditorGUILayout.LabelField(label, $"No {handedness.ToString().ToLower()}-hand gestures defined");
            return;
        }

        var names = new List<string> { "None" };
        var uuids = new List<string> { "" };
        names.AddRange(sourceNames);
        uuids.AddRange(sourceUuids);

        int currentIdx = uuids.IndexOf(nullUuidProp.stringValue);
        if (currentIdx < 0) currentIdx = 0;
        int newIdx = EditorGUILayout.Popup(label, currentIdx, names.ToArray());
        if (newIdx != currentIdx)
            nullUuidProp.stringValue = uuids[newIdx];
    }

    private void PrintConfigValues()
    {
        Debug.Log($"[Prehension] {apiClient.SerializeConfigToJson()}");
    }

    // HELPERS
    private void InitializeNewGesture(SerializedProperty gestureProp)
    {
        Debug.Log("[Prehension] initializing new gesture");
        gestureProp.FindPropertyRelative("name").stringValue = "New Gesture";
        gestureProp.FindPropertyRelative("uuid").stringValue = System.Guid.NewGuid().ToString();
        gestureProp.FindPropertyRelative("active").boolValue = true;
        gestureProp.FindPropertyRelative("handedness").enumValueIndex = 0; // Right
        gestureProp.FindPropertyRelative("oppositeGestureUuid").stringValue = "";
        gestureProp.FindPropertyRelative("mirrorFromGestureUuid").stringValue = "";
        gestureProp.FindPropertyRelative("samples").arraySize = 0;
    }

    private ReorderableList GetSampleList(SerializedProperty gestureProp, SerializedProperty samplesProp)
    {
        if (sampleLists.TryGetValue(samplesProp.propertyPath, out var list))
            return list;

        list = new ReorderableList(
            samplesProp.serializedObject,
            samplesProp,
            true,
            false,
            true,
            true); // felt like Unity was picking the wrong constructor overload here and thus had a problem with named arguments.
        // absolutely no idea why.

        list.elementHeight = EditorGUIUtility.singleLineHeight;

        list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            SerializedProperty element = samplesProp.GetArrayElementAtIndex(index);
            SerializedProperty activeProp = element.FindPropertyRelative("active");
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                activeProp, new GUIContent($"Sample {index}"));
        };

        list.onAddCallback = l =>
        {
            int index = samplesProp.arraySize;
            samplesProp.arraySize++;

            SerializedProperty sample = samplesProp.GetArrayElementAtIndex(index);
            sample.FindPropertyRelative("active").boolValue = true;
            sample.FindPropertyRelative("uuid").stringValue = System.Guid.NewGuid().ToString();
            sample.FindPropertyRelative("hasData").boolValue = false;
            sample.FindPropertyRelative("dirtyToDB").boolValue = false;
        };

        list.onSelectCallback = (ReorderableList l) =>
        {
            SerializedProperty gestureUuid = gestureProp.FindPropertyRelative("uuid");
            SerializedProperty gestureName = gestureProp.FindPropertyRelative("name");

            SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(l.index);
            SerializedProperty sampleUuid = element.FindPropertyRelative("uuid");

            samplesProp.serializedObject.FindProperty("currentRecordingGestureName").stringValue = gestureName.stringValue;
            samplesProp.serializedObject.FindProperty("currentRecordingGestureUuid").stringValue = gestureUuid.stringValue;
            samplesProp.serializedObject.FindProperty("currentRecordingSampleIndex").intValue = l.index;
            samplesProp.serializedObject.FindProperty("currentRecordingSampleUuid").stringValue = sampleUuid.stringValue;
        };

        sampleLists[samplesProp.propertyPath] = list;
        return list;
    }
}
