using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Linq;

namespace JaxTools.AnimatorTools
{

    /// Unity EditorWindow UI for building interconnected clone networks and configuring
    /// local/remote sync behaviors. Delegates heavy logic to <see cref="AnimatorCloner"/>.
    public class VRCHATStateSyncTool : EditorWindow
    {
        private AnimatorController animatorController;
        private Vector2 scrollPosition;
        private Vector2 mainScrollPosition;
        private AnimatorControllerLayer selectedLayer;
        private List<AnimatorState> numberedStates = new List<AnimatorState>();
        private Vector2 stateScrollPosition;
        private string[] intParameterNames;
        private int selectedIntParameterIndex = 0;
        private string[] boolParameterNames;
        private List<string> selectedBoolParameters = new List<string>();
        private bool useBinaryMode = false;
        private string clonedStatePrefix = "Remote_";
        private bool removeParameterDriverFromRemote = false;
        private bool addParameterDriverForLocalSync = false;
        private bool packClonedIntoSubStateMachine = false;
        
        private Dictionary<AnimatorState, string> stateNumberInputs = new Dictionary<AnimatorState, string>();
        private Dictionary<int, List<AnimatorState>> numberConflicts = new Dictionary<int, List<AnimatorState>>();
        
        private AnimatorCloner animatorCloner;
        private AnimatorController lastKnownController;
        private AnimatorControllerLayer lastKnownLayer;
        
        private GUIStyle layerButtonStyle;
        private GUIStyle helpBoxStyle;
        private GUIStyle selectedLayerStyle;
        private GUIStyle layerTabStyle;
        
        private static PropertyInfo statePositionProperty;
        
        private bool stylesInitialized = false;

        private const float LayerRowEstimatedHeight = 55f;
        private const float MaxLayersScrollHeight = 250f;
        private const float StateRowEstimatedHeight = 28f;
        private const float MaxStatesScrollHeight = 300f;

        [MenuItem("Jax's Tools/VRChat State Syncer")]
        public static void ShowWindow()
        {
            var window = GetWindow<VRCHATStateSyncTool>("VRChat State Syncer");
            window.minSize = new Vector2(400, 500);
        }


        /// Initializes editor styles and caches reflection properties when the window is enabled.
        private void OnEnable()
        {
            InitializeStyles();
            
            if (statePositionProperty == null)
            {
                statePositionProperty = typeof(AnimatorState).GetProperty("position", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
        }
        

        /// Lazily sets up GUI styles used by the window.
        private void InitializeStyles()
        {
            if (!stylesInitialized)
            {
                layerButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
                layerButtonStyle.alignment = TextAnchor.MiddleLeft;
                layerButtonStyle.padding = new RectOffset(10, 10, 5, 5);
                layerButtonStyle.margin = new RectOffset(2, 2, 2, 2);
                layerButtonStyle.fixedHeight = 30;
            
                selectedLayerStyle = new GUIStyle(layerButtonStyle);
                selectedLayerStyle.normal.background = CreateColorTexture(new Color(0.3f, 0.5f, 0.8f, 0.8f));
                selectedLayerStyle.normal.textColor = Color.white;
                selectedLayerStyle.fontStyle = FontStyle.Bold;

                layerTabStyle = new GUIStyle(EditorStyles.helpBox);
                layerTabStyle.padding = new RectOffset(5, 5, 5, 5);
                layerTabStyle.margin = new RectOffset(0, 0, 0, 5);
                
                helpBoxStyle = EditorStyles.helpBox;
                
                stylesInitialized = true;
            }
        }
        

        /// Helper to generate a 1x1 texture with the specified color.
        private Texture2D CreateColorTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }


        /// Renders the UI and handles user interactions.
        private void OnGUI()
        {
            GUILayout.Label("Sync State Tool", EditorStyles.boldLabel);
            
            GUIStyle tinyTextStyle = new GUIStyle(EditorStyles.miniLabel);
            tinyTextStyle.fontSize = 8;
            tinyTextStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            GUILayout.Label("I failed at graphic design", tinyTextStyle);
            
            GUILayout.Space(5);
            if (GUILayout.Button("Contact me on Discord", GUILayout.Width(150)))
            {
                Application.OpenURL("https://discord.gg/gAvSUqdnnB");
            }
            GUILayout.Space(5);
            
            AnimatorController newController = (AnimatorController)EditorGUILayout.ObjectField(
                "Animator Controller",
                animatorController,
                typeof(AnimatorController),
                false
            );

            if (newController != animatorController)
            {
                animatorController = newController;
                InvalidateCacheIfNeeded();
                ExtractParameters();
                Repaint();
            }

            if (animatorController != null)
            {
                InvalidateCacheIfNeeded();
                ExtractParameters();
                
                mainScrollPosition = GUILayout.BeginScrollView(mainScrollPosition);
                
                GUILayout.Space(10);
                GUILayout.Label("Layers (" + animatorController.layers.Length + ")", EditorStyles.boldLabel);
                
                int layerCount = animatorController.layers != null ? animatorController.layers.Length : 0;
                float desiredLayersHeight = Mathf.Min(
                    Mathf.Max(1, layerCount) * LayerRowEstimatedHeight,
                    MaxLayersScrollHeight
                );
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(desiredLayersHeight));
                
                foreach (var layer in animatorController.layers)
                {
                    DrawLayer(layer);
                }
                
                GUILayout.EndScrollView();

                if (selectedLayer != null && selectedLayer.stateMachine != null)
                {
                    DrawNumberedStatesSection();
                }
                
                GUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("Select an Animator Controller to view its layers", MessageType.Info);
            }
        }

        private void DrawLayer(AnimatorControllerLayer layer)
        {
            InitializeStyles();
            
            GUILayout.BeginVertical(layerTabStyle);
            
            GUIStyle currentStyle = (layer == selectedLayer) ? selectedLayerStyle : layerButtonStyle;
            
            Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(layer.name), currentStyle, GUILayout.Height(30));
            
            if (GUI.Button(buttonRect, layer.name, currentStyle))
            {
                SelectLayer(layer);
            }
            
            GUILayout.BeginHorizontal();
            GUILayout.Space(15);
            GUILayout.Label($"Weight: {layer.defaultWeight:F2}", GUILayout.Width(80));
            GUILayout.Label($"States: {layer.stateMachine.states.Length}", GUILayout.Width(70));
            GUILayout.Label($"Mask: {(layer.avatarMask != null ? layer.avatarMask.name : "None")}", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }


        /// Sets the current layer selection and refreshes cached data and UI.
        private void SelectLayer(AnimatorControllerLayer layer)
        {
            selectedLayer = layer;
            InvalidateCacheIfNeeded();
            
            if (selectedLayer != null && selectedLayer.stateMachine != null)
            {
                FindNumberedStates(selectedLayer);
                Selection.activeObject = animatorController;
                EditorGUIUtility.PingObject(selectedLayer.stateMachine);
            }
            
            Repaint();
        }


        /// Maintains consistency of cached selections and the <see cref="AnimatorCloner"/> instance.
        private void InvalidateCacheIfNeeded()
        {
            bool controllerChanged = lastKnownController != animatorController;
            bool layerChanged = lastKnownLayer != selectedLayer;
            if (controllerChanged)
            {
                animatorCloner = null;
                selectedLayer = null;
                numberedStates.Clear();
                selectedIntParameterIndex = 0;
                intParameterNames = null;
                stateNumberInputs.Clear();
                numberConflicts.Clear();

                lastKnownController = animatorController;
                lastKnownLayer = selectedLayer;
                Repaint();
                return;
            }

            if (animatorCloner != null &&
                (layerChanged ||
                 lastKnownController != animatorCloner.animatorController ||
                 lastKnownLayer != animatorCloner.SelectedLayer))
            {
                animatorCloner = null;
            }

            lastKnownController = animatorController;
            lastKnownLayer = selectedLayer;
        }


        /// Refreshes the list of numbered states via the cloner and repaints the UI.
        private void FindNumberedStates(AnimatorControllerLayer layer)
        {
            numberedStates.Clear();
            
            if (layer == null || layer.stateMachine == null)
            {
                return;
            }
            
            if (animatorCloner == null)
            {
                animatorCloner = new AnimatorCloner(animatorController, selectedLayer, numberedStates, intParameterNames, selectedIntParameterIndex);
            }
            else
            {

                animatorCloner.UpdateProperties(animatorController, selectedLayer, numberedStates, intParameterNames, selectedIntParameterIndex);
            }
            
            animatorCloner.FindNumberedStates(layer);
            
            Repaint();
        }
        

        /// UI helper that resolves a state's number via the cloner.
        private int ExtractStateNumber(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                return int.MaxValue;
            }
            
            if (animatorCloner == null)
            {
                animatorCloner = new AnimatorCloner(animatorController, selectedLayer, numberedStates, intParameterNames, selectedIntParameterIndex);
            }
            else
            {
                animatorCloner.UpdateProperties(animatorController, selectedLayer, numberedStates, intParameterNames, selectedIntParameterIndex);
            }
            return animatorCloner.ExtractStateNumber(stateName);
        }


        /// Loads parameter names from the controller and ensures selection indices are valid.
        private void ExtractParameters()
        {
            List<string> intParamNames = new List<string>();
            List<string> boolParamNames = new List<string>();
            var controllerParameters = animatorController.parameters;
            
            if (controllerParameters != null)
            {
                foreach (var param in controllerParameters)
                {
                    if (param.type == AnimatorControllerParameterType.Int)
                    {
                        intParamNames.Add(param.name);
                    }
                    else if (param.type == AnimatorControllerParameterType.Bool)
                    {
                        boolParamNames.Add(param.name);
                    }
                }
            }
            
            if (intParamNames.Count == 0)
            {
                intParamNames.Add("No Int parameters available");
            }
            
            if (boolParamNames.Count == 0)
            {
                boolParamNames.Add("No Bool parameters available");
            }
            
            intParameterNames = intParamNames.ToArray();
            boolParameterNames = boolParamNames.ToArray();
            
            if (selectedIntParameterIndex >= intParameterNames.Length)
            {
                selectedIntParameterIndex = 0;
            }
            
            if (selectedBoolParameters.Count > 0)
            {
                selectedBoolParameters = selectedBoolParameters
                    .Where(p => boolParameterNames.Contains(p))
                    .ToList();
            }
            
        }


        /// Gathers user assignments, configures the cloner and triggers clone network creation.
        private void CreateInterconnectedCloneNetwork()
        {
            try
            {
                if (animatorCloner == null)
                {
                    animatorCloner = new AnimatorCloner(animatorController, selectedLayer, numberedStates, intParameterNames, selectedIntParameterIndex);
                }
                else
                {
                    animatorCloner.UpdateProperties(animatorController, selectedLayer, numberedStates, intParameterNames, selectedIntParameterIndex);
                }

if (animatorCloner != null)
                {
                    foreach (var kvp in stateNumberInputs)
                    {
                        if (kvp.Key != null && int.TryParse(kvp.Value, out int number))
                        {
                            animatorCloner.SetUserAssignedNumber(kvp.Key, number);
                        }
                    }
                }
                animatorCloner.clonedStatePrefix = clonedStatePrefix;
                animatorCloner.removeParameterDriverFromRemote = removeParameterDriverFromRemote;
                animatorCloner.addParameterDriverForLocalSync = addParameterDriverForLocalSync;
                animatorCloner.PackAfterClone = packClonedIntoSubStateMachine;
                
                if (useBinaryMode)
                {
                    animatorCloner.UseBinaryMode = true;
                    animatorCloner.SelectedBoolParameters = new List<string>(selectedBoolParameters);
                    animatorCloner.BinaryBitDepth = selectedBoolParameters.Count;
                }
                else
                {
                    animatorCloner.UseBinaryMode = false;
                }
                
                animatorCloner.CreateInterconnectedCloneNetwork();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error creating interconnected clone network: {e.Message}");
                EditorUtility.DisplayDialog("Error", "An error occurred while creating the interconnected clone network.", "OK");
                animatorCloner = null;
            }
        }


        /// UI for state listing and the network creation controls.
        private void DrawNumberedStatesSection()
        {
            GUILayout.Space(20);
            GUILayout.Label($"States in '{selectedLayer.name}' ({numberedStates.Count})", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox("States with numeric suffixes are automatically detected. For states without numbers, you can assign a number using the input field.", MessageType.Info);
            
            // Display number conflicts if any exist
            if (numberConflicts.Count > 0)
            {
                EditorGUILayout.HelpBox("Number conflicts detected:", MessageType.Warning);
                foreach (var kvp in numberConflicts)
                {
                    int number = kvp.Key;
                    var conflictingStates = kvp.Value;
                    string stateNames = string.Join(", ", conflictingStates.Select(s => s.name));
                    EditorGUILayout.HelpBox($"Number {number} is assigned to: {stateNames}", MessageType.Error);
                }
                GUILayout.Space(10);
            }
            
            int stateCount = Mathf.Max(1, numberedStates != null ? numberedStates.Count : 0);
            float desiredStatesHeight = Mathf.Min(stateCount * StateRowEstimatedHeight, MaxStatesScrollHeight);
            stateScrollPosition = GUILayout.BeginScrollView(stateScrollPosition, GUILayout.Height(desiredStatesHeight));
            
            if (numberedStates.Count > 0)
            {
                numberedStates.RemoveAll(state => state == null);
                
                foreach (var state in numberedStates)
                {
                    if (state == null) continue;
                    
                    InitializeStyles();
                    
                    GUILayout.BeginHorizontal(helpBoxStyle);
                    
                    try
                    {
                        GUILayout.Label(state.name, GUILayout.Width(200));
                        
                        int stateNumber = 0;
                        
                        string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                        if (int.TryParse(numberStr, out stateNumber))
                        {
                            GUILayout.Label($"Number: {stateNumber}", GUILayout.Width(80));
                        }
                        else
                        {
                            if (!stateNumberInputs.ContainsKey(state))
                            {
                                if (animatorCloner != null && animatorCloner.UserAssignedNumbers.TryGetValue(state, out int existingNumber))
                                {
                                    stateNumberInputs[state] = existingNumber.ToString();
                                }
                                else
                                {
                                    stateNumberInputs[state] = "";
                                }
                            }
                            
                            GUILayout.Label("Assign #:", GUILayout.Width(60));
                            
                            string currentInput = stateNumberInputs[state];
                            
                            string newInput = EditorGUILayout.TextField(currentInput, GUILayout.Width(40));
                            
                            if (newInput != currentInput)
                            {
                                stateNumberInputs[state] = newInput;
                                
                                if (int.TryParse(newInput, out int newNumber) && animatorCloner != null)
                                {
                                    // Check for conflicts before assigning
                                    CheckForNumberConflicts(state, newNumber);
                                    
                                    animatorCloner.SetUserAssignedNumber(state, newNumber);
                                    Debug.Log($"Assigned number {newNumber} to state '{state.name}'");
                                    
                                    Repaint();
                                }
                                else if (string.IsNullOrWhiteSpace(newInput) && animatorCloner != null)
                                {
                                    animatorCloner.RemoveUserAssignedNumber(state);
                                    numberConflicts.Clear(); // Clear conflicts when removing assignments
                                    Repaint();
                                }
                            }
                        }
                        
                        if (GUILayout.Button("Select", GUILayout.Width(60)))
                        {
                            Selection.activeObject = state;
                            EditorGUIUtility.PingObject(state);
                        }
                    }
                    catch (MissingReferenceException)
                    {
                        GUILayout.Label("Destroyed State", GUILayout.Width(200));
                        GUILayout.Label("N/A", GUILayout.Width(80));
                        GUILayout.Label("-", GUILayout.Width(60));
                    }
                    
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No states found in this layer", MessageType.Info);
            }
            
            GUILayout.EndScrollView();
            
            if (numberedStates.Count > 0)
            {
                GUILayout.Space(20);
                GUILayout.Label("Create Interconnected Clone Network", EditorStyles.boldLabel);
                
                int statesWithNumbers = 0;
                int statesThatNeedNumbers = 0;
                
                foreach (var state in numberedStates)
                {
                    if (state == null) continue;
                    
                    string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                    if (int.TryParse(numberStr, out _))
                    {
                        statesWithNumbers++;
                    }
                    else if (animatorCloner != null && animatorCloner.UserAssignedNumbers.ContainsKey(state))
                    {
                        statesWithNumbers++;
                    }
                    else if (stateNumberInputs.TryGetValue(state, out string inputValue) && !string.IsNullOrWhiteSpace(inputValue))
                    {
                        if (int.TryParse(inputValue, out _))
                        {
                            statesWithNumbers++;
                        }
                        else
                        {
                            statesThatNeedNumbers++;
                        }
                    }
                }
                
                if (statesThatNeedNumbers > 0)
                {
                    EditorGUILayout.HelpBox($"{statesThatNeedNumbers} states have input but not valid numbers. Please enter valid numbers or clear the input fields.", MessageType.Warning);
                }
                
                InitializeStyles();
                
                GUILayout.BeginVertical(helpBoxStyle);
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mode:", GUILayout.Width(120));
                useBinaryMode = EditorGUILayout.Toggle("Binary Mode", useBinaryMode);
                GUILayout.EndHorizontal();
                
                if (useBinaryMode)
                {
                    DrawBinaryParameterControls();
                }
                else
                {
                    DrawIntParameterControls();
                }
                
                GUILayout.Space(10);
                GUILayout.Label("Parameter Driver Options", EditorStyles.miniBoldLabel);
                
                GUILayout.BeginHorizontal();
                removeParameterDriverFromRemote = EditorGUILayout.Toggle(removeParameterDriverFromRemote, GUILayout.Width(20));
                GUILayout.Label("Remove parameter driver from remote");
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                addParameterDriverForLocalSync = EditorGUILayout.Toggle(addParameterDriverForLocalSync, GUILayout.Width(20));
                GUILayout.Label("Add Parameter Driver for local sync state");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                packClonedIntoSubStateMachine = EditorGUILayout.Toggle(packClonedIntoSubStateMachine, GUILayout.Width(20));
                GUILayout.Label("Pack into StateMachine");
                GUILayout.EndHorizontal();
                
                if (addParameterDriverForLocalSync)
                {
                    EditorGUILayout.HelpBox("Will add a Parameter Driver with 'Set' mode and 'Local' toggled off to each original state.", MessageType.Info);
                }
                
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                
                if (GUILayout.Button("Create Interconnected Clone Network"))
                {
                    if (animatorCloner != null && animatorCloner.UserAssignedNumbers.Count > 0)
                    {
                        Debug.Log("User-assigned numbers before creating network:");
                        foreach (var kvp in animatorCloner.UserAssignedNumbers)
                        {
                            Debug.Log($"  State '{kvp.Key.name}' has number {kvp.Value}");
                        }
                    }
                    else
                    {
                        Debug.Log("No user-assigned numbers found before creating network");
                    }
                    
                    CreateInterconnectedCloneNetwork();
                }
                
                GUILayout.EndHorizontal();
                
                GUILayout.EndVertical();
            }
        }

        private Vector2 boolParameterScrollPosition = Vector2.zero;
        

        /// UI for selecting bool parameters and viewing binary config details.
        private void DrawBinaryParameterControls()
        {
            GUILayout.Label("Bool Parameters:", EditorStyles.boldLabel);
            
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            if (boolParameterNames != null && boolParameterNames.Length > 0)
            {
                float scrollHeight = Mathf.Min(boolParameterNames.Length * 20f, 150f);
                
                boolParameterScrollPosition = GUILayout.BeginScrollView(
                    boolParameterScrollPosition,
                    GUILayout.Height(scrollHeight)
                );
                
                for (int i = 0; i < boolParameterNames.Length; i++)
                {
                    if (boolParameterNames[i] == "No Bool parameters available") continue;
                    
                    GUILayout.BeginHorizontal();
                    
                    bool isSelected = selectedBoolParameters.Contains(boolParameterNames[i]);
                    bool newSelected = EditorGUILayout.ToggleLeft(boolParameterNames[i], isSelected);
                    
                    if (newSelected != isSelected)
                    {
                        if (newSelected)
                        {
                            selectedBoolParameters.Add(boolParameterNames[i]);
                        }
                        else
                        {
                            selectedBoolParameters.Remove(boolParameterNames[i]);
                        }
                    }
                    
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.EndScrollView();
            }
            
            GUILayout.EndVertical();
            
            if (selectedBoolParameters.Count > 0)
            {
                DrawBinaryCalculator();
            }
            else
            {
                EditorGUILayout.HelpBox("Select at least one boolean parameter to use binary mode.", MessageType.Warning);
            }
        }


        /// UI for choosing the int parameter and cloned state naming.
        private void DrawIntParameterControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sync Parameter:", GUILayout.Width(120));
            selectedIntParameterIndex = EditorGUILayout.Popup(selectedIntParameterIndex, intParameterNames);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("State Prefix:", GUILayout.Width(120));
            clonedStatePrefix = EditorGUILayout.TextField(clonedStatePrefix);
            GUILayout.EndHorizontal();
        }
        

        /// Displays computed limits and examples for the selected binary parameters.
        private void DrawBinaryCalculator()
        {
            GUILayout.Space(10);
            GUILayout.Box("Binary Parameter Calculator", EditorStyles.helpBox);
            
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);
            
            int bitDepth = selectedBoolParameters.Count;
            int maxStates = BinaryEncoder.GetTotalStates(bitDepth);
            int minState = BinaryEncoder.GetMinStateNumber(bitDepth);
            int maxState = BinaryEncoder.GetMaxStateNumber(bitDepth);
            
            GUILayout.Label("Configuration Summary", EditorStyles.boldLabel);
            GUILayout.Label($"Selected Parameters: {selectedBoolParameters.Count}", EditorStyles.label);
            GUILayout.Label($"Max States: {maxStates}", EditorStyles.label);
            GUILayout.Label($"State Range: {minState}-{maxState}", EditorStyles.label);
            
            // Check for states with user-assigned numbers
            int statesWithValidNumbers = 0;
            int statesWithInvalidNumbers = 0;
            int statesWithUserAssignments = 0;
            
            foreach (var state in numberedStates)
            {
                if (state == null) continue;
                
                string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                if (int.TryParse(numberStr, out int suffixNumber))
                {
                    if (suffixNumber >= minState && suffixNumber <= maxState)
                    {
                        statesWithValidNumbers++;
                    }
                    else
                    {
                        statesWithInvalidNumbers++;
                    }
                }
                else if (animatorCloner != null && animatorCloner.UserAssignedNumbers.TryGetValue(state, out int userNumber))
                {
                    statesWithUserAssignments++;
                    if (userNumber >= minState && userNumber <= maxState)
                    {
                        statesWithValidNumbers++;
                    }
                    else
                    {
                        statesWithInvalidNumbers++;
                    }
                }
            }
            
            if (statesWithValidNumbers > maxStates)
            {
                GUILayout.Label($"⚠ Warning: {statesWithValidNumbers} numbered states found but only {maxStates} can be handled", EditorStyles.label);
                GUILayout.Label("Select more parameters or reduce state count", EditorStyles.label);
            }
            else if (statesWithInvalidNumbers > 0)
            {
                GUILayout.Label($"⚠ Warning: {statesWithInvalidNumbers} state numbers are outside the valid range ({minState}-{maxState})", EditorStyles.label);
                GUILayout.Label("These states will be ignored in binary mode", EditorStyles.label);
            }
            else
            {
                int totalStatesWithNumbers = statesWithValidNumbers;
                if (totalStatesWithNumbers == numberedStates.Count)
                {
                    GUILayout.Label($"✓ Can handle all {numberedStates.Count} states", EditorStyles.label);
                }
                else
                {
                    GUILayout.Label($"✓ Can handle {totalStatesWithNumbers} of {numberedStates.Count} states", EditorStyles.label);
                    if (numberedStates.Count - totalStatesWithNumbers > 0)
                    {
                        GUILayout.Label($"  ({numberedStates.Count - totalStatesWithNumbers} states have no numbers assigned)", EditorStyles.label);
                    }
                }
            }
            
            // Show user assignment status
            if (statesWithUserAssignments > 0)
            {
                GUILayout.Space(5);
                GUILayout.Label("User-Assigned Numbers:", EditorStyles.boldLabel);
                foreach (var state in numberedStates)
                {
                    if (state == null) continue;
                    
                    if (animatorCloner != null && animatorCloner.UserAssignedNumbers.TryGetValue(state, out int userNumber))
                    {
                        bool isValid = userNumber >= minState && userNumber <= maxState;
                        string status = isValid ? "✓" : "✗";
                        GUILayout.Label($"  {status} {state.name}: {userNumber}", EditorStyles.label);
                    }
                }
            }
            
            GUILayout.Space(10);
            
            GUILayout.Label("Binary Examples", EditorStyles.boldLabel);
            var examples = ParameterDetectionUtilities.GetBinaryExamples(bitDepth, 4);
            foreach (var example in examples)
            {
                GUILayout.Label(example.Description, EditorStyles.label);
            }
            
            // Add examples for user-assigned numbers if they exist
            if (animatorCloner != null && animatorCloner.UserAssignedNumbers.Count > 0)
            {
                GUILayout.Space(5);
                GUILayout.Label("User-Assigned State Examples:", EditorStyles.boldLabel);
                
                var userAssignedExamples = animatorCloner.UserAssignedNumbers
                    .Where(kvp => kvp.Value >= minState && kvp.Value <= maxState)
                    .OrderBy(kvp => kvp.Value)
                    .Take(4)
                    .ToList();
                
                foreach (var kvp in userAssignedExamples)
                {
                    var binary = BinaryEncoder.StateNumberToBinary(kvp.Value, bitDepth);
                    string binaryStr = string.Join("", binary.Select(b => b ? "1" : "0"));
                    GUILayout.Label($"  State {kvp.Value} ({binaryStr}): {kvp.Key.name}", EditorStyles.label);
                }
            }
            
            GUILayout.Space(10);
            GUILayout.Label("Parameter Mapping", EditorStyles.boldLabel);
            for (int i = 0; i < selectedBoolParameters.Count; i++)
            {
                string bitName = $"Bit {bitDepth - i}";
                GUILayout.Label($"{bitName}: {selectedBoolParameters[i]}", EditorStyles.label);
            }
            
            GUILayout.Space(5);
            GUILayout.EndVertical();
        }
        

        /// Safe accessor for the internal 'position' property on AnimatorState.
        private Vector3 GetStatePosition(AnimatorState state)
        {
            if (statePositionProperty != null)
            {
                return (Vector3)statePositionProperty.GetValue(state);
            }
            return Vector3.zero;
        }
        
        /// Checks for number conflicts when assigning a number to a state
        private void CheckForNumberConflicts(AnimatorState assignedState, int assignedNumber)
        {
            numberConflicts.Clear();
            
            var conflictingStates = new List<AnimatorState>();
            
            // Check all states to find conflicts
            foreach (var state in numberedStates)
            {
                if (state == null || state == assignedState) continue;
                
                int stateNumber = 0;
                
                // Check if state has numeric suffix
                string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                if (int.TryParse(numberStr, out stateNumber))
                {
                    if (stateNumber == assignedNumber)
                    {
                        conflictingStates.Add(state);
                    }
                }
                // Check if state has user-assigned number
                else if (animatorCloner != null && animatorCloner.UserAssignedNumbers.TryGetValue(state, out int userNumber))
                {
                    if (userNumber == assignedNumber)
                    {
                        conflictingStates.Add(state);
                    }
                }
            }
            
            // Add the current state if it conflicts with others
            if (conflictingStates.Count > 0)
            {
                numberConflicts[assignedNumber] = conflictingStates;
                Debug.LogWarning($"Number conflict detected: Number {assignedNumber} is already assigned to {string.Join(", ", conflictingStates.Select(s => s.name))}");
            }
        }
        
        
    }
}