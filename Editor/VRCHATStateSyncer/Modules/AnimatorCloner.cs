using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Linq;

namespace JaxTools.AnimatorTools
{
    /// Builds a network of cloned states and transitions for remote sync.
    /// Supports two modes:
    /// - Int mode: transitions key off a single int parameter.
    /// - Binary mode: transitions key off multiple boolean parameters encoding a state number.
    /// Also supports optional packing into a sub-state machine and copying behaviours.
    public class AnimatorCloner
    {
        public AnimatorController animatorController;
        private AnimatorControllerLayer selectedLayer;
        private List<AnimatorState> numberedStates;
        private string[] intParameterNames;
        private int selectedIntParameterIndex = 0;
        public string clonedStatePrefix = "Remote_";
        public bool removeParameterDriverFromRemote = false;
        public bool addParameterDriverForLocalSync = false;
        public bool PackAfterClone { get; set; } = false;
        
        public Dictionary<AnimatorState, int> UserAssignedNumbers { get; set; } = new Dictionary<AnimatorState, int>();
        
        public bool UseBinaryMode { get; set; } = false;
        public List<string> SelectedBoolParameters { get; set; } = new List<string>();
        public int BinaryBitDepth { get; set; } = 3;

        public AnimatorControllerLayer SelectedLayer { get { return selectedLayer; } set { selectedLayer = value; } }
        public List<AnimatorState> NumberedStates { get { return numberedStates; } set { numberedStates = value; } }
        public string[] IntParameterNames { get { return intParameterNames; } set { intParameterNames = value; } }
        public int SelectedIntParameterIndex { get { return selectedIntParameterIndex; } set { selectedIntParameterIndex = value; } }

        private static readonly Regex NumberPatternRegex = new Regex(@"\s\d{1,3}$", RegexOptions.Compiled);
        private static readonly Regex StateNumberRegex = new Regex(@"\d{1,3}$", RegexOptions.Compiled);


        /// Constructs a cloner bound to a controller, layer and working state list.
        public AnimatorCloner(AnimatorController controller, AnimatorControllerLayer layer, List<AnimatorState> states, string[] parameters, int paramIndex)
        {
            animatorController = controller;
            selectedLayer = layer;
            numberedStates = states;
            intParameterNames = parameters;
            selectedIntParameterIndex = paramIndex;
        }


        /// Groups all cloned states (by prefix) into a new sub-state machine and reconnects transitions within it.
        public void PackClonedStatesIntoSubStateMachine()
        {
            if (selectedLayer == null || selectedLayer.stateMachine == null)
            {
                EditorUtility.DisplayDialog("No Layer Selected", "Please select a valid layer with a state machine.", "OK");
                return;
            }

            var rootStateMachine = selectedLayer.stateMachine;

            var childStates = rootStateMachine.states;
            List<ChildAnimatorState> clonedChildStates = new List<ChildAnimatorState>();
            foreach (var child in childStates)
            {
                if (child.state != null && !string.IsNullOrEmpty(clonedStatePrefix) && child.state.name.StartsWith(clonedStatePrefix, StringComparison.Ordinal))
                {
                    clonedChildStates.Add(child);
                }
            }

            if (clonedChildStates.Count == 0)
            {
                EditorUtility.DisplayDialog("No Cloned States", $"No states found with prefix '{clonedStatePrefix}'.",
                    "OK");
                return;
            }

            Vector3 avg = Vector3.zero;
            foreach (var child in clonedChildStates)
            {
                avg += child.position;
            }
            avg /= clonedChildStates.Count;

            string subMachineName = $"{(string.IsNullOrEmpty(clonedStatePrefix) ? "Cloned" : clonedStatePrefix.TrimEnd('_'))} Group";
            var subStateMachine = rootStateMachine.AddStateMachine(subMachineName, avg);

            Dictionary<AnimatorState, AnimatorState> oldToNewStateMap = new Dictionary<AnimatorState, AnimatorState>();

            foreach (var child in clonedChildStates)
            {
                var originalState = child.state;
                var newState = subStateMachine.AddState(originalState.name, child.position);

                newState.motion = originalState.motion;
                newState.speed = originalState.speed;
                newState.cycleOffset = originalState.cycleOffset;
                newState.mirror = originalState.mirror;
                newState.writeDefaultValues = originalState.writeDefaultValues;

                if (originalState.behaviours != null && originalState.behaviours.Length > 0)
                {
                    foreach (var behaviour in originalState.behaviours)
                    {
                        try
                        {
                            System.Type behaviourType = behaviour.GetType();
                            StateMachineBehaviour newBehaviour = newState.AddStateMachineBehaviour(behaviourType);

                            SerializedObject source = new SerializedObject(behaviour);
                            SerializedObject dest = new SerializedObject(newBehaviour);

                            SerializedProperty property = source.GetIterator();
                            while (property.NextVisible(true))
                            {
                                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                    property.objectReferenceValue == null)
                                {
                                    continue;
                                }

                                dest.CopyFromSerializedProperty(property);
                            }

                            dest.ApplyModifiedProperties();
                        }
                        catch (System.Exception)
                        {
                        }
                    }
                }

                oldToNewStateMap[originalState] = newState;
            }

            foreach (var kv in oldToNewStateMap)
            {
                AnimatorState oldState = kv.Key;
                AnimatorState newState = kv.Value;

                foreach (var oldTransition in oldState.transitions)
                {
                    var oldDest = oldTransition.destinationState;
                    if (oldDest != null && oldToNewStateMap.TryGetValue(oldDest, out AnimatorState newDest))
                    {
                        var newTransition = newState.AddTransition(newDest);
                        newTransition.hasExitTime = oldTransition.hasExitTime;
                        newTransition.exitTime = oldTransition.exitTime;
                        newTransition.hasFixedDuration = oldTransition.hasFixedDuration;
                        newTransition.duration = oldTransition.duration;
                        newTransition.offset = oldTransition.offset;
                        newTransition.interruptionSource = oldTransition.interruptionSource;
                        newTransition.orderedInterruption = oldTransition.orderedInterruption;
                        newTransition.canTransitionToSelf = oldTransition.canTransitionToSelf;

                        foreach (var cond in oldTransition.conditions)
                        {
                            newTransition.AddCondition(cond.mode, cond.threshold, cond.parameter);
                        }
                    }
                }
            }

            foreach (var child in clonedChildStates)
            {
                try
                {
                    rootStateMachine.RemoveState(child.state);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to remove original state '{child.state?.name}': {e.Message}");
                }
            }

            EditorUtility.SetDirty(animatorController);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(subStateMachine);
            EditorUtility.DisplayDialog("Packed", $"Moved {oldToNewStateMap.Count} cloned states into sub-state machine '{subMachineName}'.", "OK");
        }


        /// Keeps the cloner in sync with latest selections and data providers.
        public void UpdateProperties(AnimatorController controller, AnimatorControllerLayer layer, List<AnimatorState> states, string[] parameters, int paramIndex)
        {
            animatorController = controller;
            selectedLayer = layer;
            numberedStates = states;
            intParameterNames = parameters;
            selectedIntParameterIndex = paramIndex;
        }


        /// Entry point that creates the clone network in the selected mode and optionally packs it.
        public void CreateInterconnectedCloneNetwork()
        {
            try
            {
                Debug.Log($"[SYNCER] Starting interconnected clone network creation...");
                Debug.Log($"[SYNCER] Mode: {(UseBinaryMode ? "Binary" : "Int")}");
                Debug.Log($"[SYNCER] States to process: {numberedStates.Count}");
                Debug.Log($"[SYNCER] Add parameter drivers for local sync: {addParameterDriverForLocalSync}");
                
                if (UseBinaryMode)
                {
                    Debug.Log($"[SYNCER] Binary mode - Parameters: {string.Join(", ", SelectedBoolParameters)}");
                    CreateBinaryInterconnectedCloneNetwork();
                }
                else
                {
                    Debug.Log($"[SYNCER] Int mode - Parameter: {intParameterNames[selectedIntParameterIndex]}");
                    CreateIntInterconnectedCloneNetwork();
                }

                if (PackAfterClone)
                {
                    PackClonedStatesIntoSubStateMachine();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SYNCER] Error creating interconnected clone network: {e.Message}");
                EditorUtility.DisplayDialog("Error", "An error occurred while creating the interconnected clone network.", "OK");
            }
        }
        

        /// Clones states and wires transitions that compare an int parameter to destination state numbers.
        private void CreateIntInterconnectedCloneNetwork()
        {
            Debug.Log($"[SYNCER] Starting Int mode interconnected clone network...");
            
            if (intParameterNames == null || intParameterNames.Length == 0 || intParameterNames[0] == "No Int parameters available")
            {
                EditorUtility.DisplayDialog("No Int Parameters", "Please create an Int parameter first before creating the interconnected clone network.", "OK");
                return;
            }
            
            if (numberedStates == null || numberedStates.Count == 0)
            {
                EditorUtility.DisplayDialog("No Numbered States", "No numbered states found in the selected layer.", "OK");
                return;
            }
            
            if (selectedLayer == null || selectedLayer.stateMachine == null)
            {
                EditorUtility.DisplayDialog("No Layer Selected", "Please select a valid layer with a state machine.", "OK");
                return;
            }
            
            Debug.Log($"[SYNCER] Int parameter: {intParameterNames[selectedIntParameterIndex]}");
            Debug.Log($"[SYNCER] Checking which states need parameter drivers...");
            
            string selectedParameterName = intParameterNames[selectedIntParameterIndex];
            
            Vector3 originalAnyStatePosition = Vector3.zero;
            try
            {
                var anyStatePositionProperty = typeof(AnimatorStateMachine).GetProperty("anyStatePosition",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (anyStatePositionProperty != null)
                {
                    originalAnyStatePosition = (Vector3)anyStatePositionProperty.GetValue(selectedLayer.stateMachine);
                }
            }
            catch (System.Exception)
            {
            }
            
            AnimatorController workingController = animatorController;
            List<AnimatorState> clonedStates = new List<AnimatorState>();
            
            const int statesPerRow = 5;
            const float horizontalSpacing = 200f;
            const float verticalSpacing = 100f;
            const float startX = -400f;
            const float startY = 200f;
            
            List<AnimatorState> statesToClone = new List<AnimatorState>();
            
            Debug.Log($"User-assigned numbers count: {UserAssignedNumbers.Count}");
            foreach (var kvp in UserAssignedNumbers)
            {
                Debug.Log($"  User-assigned: State '{kvp.Key.name}' has number {kvp.Value}");
            }
            
            foreach (var state in numberedStates)
            {
                if (state == null) continue;
                
                string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                bool hasNumericSuffix = int.TryParse(numberStr, out int suffixNumber);
                bool hasUserAssignedNumber = UserAssignedNumbers.ContainsKey(state);
                
                if (hasNumericSuffix)
                {
                    Debug.Log($"State '{state.name}' has numeric suffix: {suffixNumber}");
                    statesToClone.Add(state);
                }
                else if (hasUserAssignedNumber)
                {
                    Debug.Log($"State '{state.name}' has user-assigned number: {UserAssignedNumbers[state]}");
                    statesToClone.Add(state);
                }
                else
                {
                    Debug.Log($"State '{state.name}' has no number, skipping");
                }
            }
            
            for (int i = 0; i < statesToClone.Count; i++)
            {
                var originalState = statesToClone[i];
                string clonedStateName = clonedStatePrefix + originalState.name;
                
                int row = i / statesPerRow;
                int col = i % statesPerRow;
                
                Vector3 newPosition = new Vector3(
                    startX + (col * horizontalSpacing),
                    startY - (row * verticalSpacing),
                    0f
                );
                
                AnimatorState clonedState = selectedLayer.stateMachine.AddState(clonedStateName, newPosition);
                clonedState.motion = originalState.motion;
                
                if (originalState.behaviours != null && originalState.behaviours.Length > 0)
                {
                    foreach (var behaviour in originalState.behaviours)
                    {
                        if (removeParameterDriverFromRemote &&
                            (behaviour.GetType().Name.Contains("AvatarParameterDriver") ||
                             behaviour.GetType().FullName.Contains("VRC.AvatarParametersDriver")))
                        {
                            continue;
                        }
                        
                        try
                        {
                            System.Type behaviourType = behaviour.GetType();
                            StateMachineBehaviour newBehaviour = clonedState.AddStateMachineBehaviour(behaviourType);
                            
                            SerializedObject source = new SerializedObject(behaviour);
                            SerializedObject dest = new SerializedObject(newBehaviour);
                            
                            SerializedProperty property = source.GetIterator();
                            while (property.NextVisible(true))
                            {
                                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                    property.objectReferenceValue == null)
                                {
                                    continue;
                                }
                                
                                dest.CopyFromSerializedProperty(property);
                            }
                            
                            dest.ApplyModifiedProperties();
                        }
                        catch (System.Exception)
                        {
                        }
                    }
                }
                
                clonedStates.Add(clonedState);
            }
            
            try
            {
                var anyStatePositionProperty = typeof(AnimatorStateMachine).GetProperty("anyStatePosition",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (anyStatePositionProperty != null)
                {
                    anyStatePositionProperty.SetValue(selectedLayer.stateMachine, originalAnyStatePosition);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not restore Any State position: {e.Message}");
            }
            
            if (addParameterDriverForLocalSync)
            {
                for (int i = 0; i < numberedStates.Count; i++)
                {
                    AnimatorState originalState = numberedStates[i];
                    
                    string originalStateName = originalState.name;
                    int stateNumber;
                    
                    string numberStr = Regex.Match(originalStateName, @"\d{1,3}$").Value;
                    if (int.TryParse(numberStr, out stateNumber))
                    {
                        Debug.Log($"[SYNCER] Adding parameter driver to original state '{originalState.name}' for state number {stateNumber}");
                        ParameterDriverUtilities.AddParameterDriverToState(originalState, stateNumber, intParameterNames[selectedIntParameterIndex]);
                    }

                    else if (UserAssignedNumbers.TryGetValue(originalState, out stateNumber))
                    {
                        Debug.Log($"[SYNCER] Adding parameter driver to original state '{originalState.name}' for state number {stateNumber}");
                        ParameterDriverUtilities.AddParameterDriverToState(originalState, stateNumber, intParameterNames[selectedIntParameterIndex]);
                    }
                }
            }

            for (int i = 0; i < clonedStates.Count; i++)
            {
                AnimatorState sourceState = clonedStates[i];
                
                int sourceStateNumber;
                string sourceNumberStr = Regex.Match(sourceState.name, @"\d{1,3}$").Value;
                
                if (int.TryParse(sourceNumberStr, out sourceStateNumber))
                {
                }
                else
                {
                    string originalStateName = sourceState.name.Replace(clonedStatePrefix, "");
                    
                    AnimatorState originalState = numberedStates.Find(s => s.name == originalStateName);
                    
                    if (originalState != null && UserAssignedNumbers.TryGetValue(originalState, out sourceStateNumber))
                    {
                    }
                    else
                    {
                        Debug.LogWarning($"Could not determine number for state: {sourceState.name}");
                        continue;
                    }
                }
                
                for (int j = 0; j < clonedStates.Count; j++)
                {
                    if (i != j)
                    {
                        AnimatorState destinationState = clonedStates[j];
                        AnimatorStateTransition transition = sourceState.AddTransition(destinationState);
                        
                        int destinationStateNumber;
                        string destinationNumberStr = Regex.Match(destinationState.name, @"\d{1,3}$").Value;
                        
                        if (int.TryParse(destinationNumberStr, out destinationStateNumber))
                        {
                            transition.AddCondition(AnimatorConditionMode.Equals, destinationStateNumber, selectedParameterName);
                        }
                        else
                        {
                            string originalStateName = destinationState.name.Replace(clonedStatePrefix, "");
                            
                            AnimatorState originalState = numberedStates.Find(s => s.name == originalStateName);
                            
                            if (originalState != null && UserAssignedNumbers.TryGetValue(originalState, out destinationStateNumber))
                            {
                                transition.AddCondition(AnimatorConditionMode.Equals, destinationStateNumber, selectedParameterName);
                            }
                            else
                            {
                                transition.AddCondition(AnimatorConditionMode.Equals, sourceStateNumber, selectedParameterName);
                                Debug.LogWarning($"Could not determine number for destination state: {destinationState.name}, using source state number: {sourceStateNumber}");
                            }
                        }
                        
                        transition.hasExitTime = false;
                        transition.exitTime = 0;
                        transition.hasFixedDuration = true;
                        transition.duration = 0.1f;
                        transition.offset = 0;
                    }
                }
            }
            
            EditorUtility.SetDirty(workingController);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log($"[SYNCER] Successfully created int mode clone network with {clonedStates.Count} states");
            Debug.Log($"[SYNCER] Used parameter: {selectedParameterName}");
            
            string successMessage = $"Created interconnected clone network with {clonedStates.Count} states using parameter: {selectedParameterName}";
            EditorUtility.DisplayDialog("Success", successMessage, "OK");
        }
        

        /// Clones states and wires transitions using binary conditions across selected boolean parameters.
        private void CreateBinaryInterconnectedCloneNetwork()
        {
            Debug.Log($"[SYNCER] Starting Binary mode interconnected clone network...");
            
            if (SelectedBoolParameters == null || SelectedBoolParameters.Count == 0)
            {
                EditorUtility.DisplayDialog("No Bool Parameters", "Please select at least one boolean parameter to use binary mode.", "OK");
                return;
            }
            
            if (numberedStates == null || numberedStates.Count == 0)
            {
                EditorUtility.DisplayDialog("No Numbered States", "No numbered states found in the selected layer.", "OK");
                return;
            }
            
            if (selectedLayer == null || selectedLayer.stateMachine == null)
            {
                EditorUtility.DisplayDialog("No Layer Selected", "Please select a valid layer with a state machine.", "OK");
                return;
            }
            
            Debug.Log($"[SYNCER] Binary parameters: {string.Join(", ", SelectedBoolParameters)}");
            Debug.Log($"[SYNCER] Checking which states need binary parameter drivers...");
            
            if (BinaryBitDepth > SelectedBoolParameters.Count)
            {
                EditorUtility.DisplayDialog("Insufficient Parameters",
                    $"Selected bit depth ({BinaryBitDepth}) requires at least {BinaryBitDepth} boolean parameters, but only {SelectedBoolParameters.Count} are selected.",
                    "OK");
                return;
            }
            
            int maxStates = (int)Mathf.Pow(2, BinaryBitDepth);
            
            // Count only states that have actual numbers (auto or user-assigned)
            int statesWithNumbers = 0;
            foreach (var state in numberedStates)
            {
                if (state == null) continue;
                
                string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                if (int.TryParse(numberStr, out _) || UserAssignedNumbers.ContainsKey(state))
                {
                    statesWithNumbers++;
                }
            }
            
            Debug.Log($"[SYNCER] Binary validation: {statesWithNumbers} states with numbers, max capacity: {maxStates}");
            
            if (statesWithNumbers > maxStates)
            {
                EditorUtility.DisplayDialog("Too Many States",
                    $"Selected bit depth ({BinaryBitDepth}) can only handle {maxStates} states, but {statesWithNumbers} numbered states were found.",
                    "OK");
                return;
            }
            
            Vector3 originalAnyStatePosition = Vector3.zero;
            try
            {
                var anyStatePositionProperty = typeof(AnimatorStateMachine).GetProperty("anyStatePosition",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (anyStatePositionProperty != null)
                {
                    originalAnyStatePosition = (Vector3)anyStatePositionProperty.GetValue(selectedLayer.stateMachine);
                }
            }
            catch (System.Exception)
            {
            }
            
            AnimatorController workingController = animatorController;
            List<AnimatorState> clonedStates = new List<AnimatorState>();
            
            const int statesPerRow = 5;
            const float horizontalSpacing = 200f;
            const float verticalSpacing = 100f;
            const float startX = -400f;
            const float startY = 200f;
            
            List<AnimatorState> statesToClone = new List<AnimatorState>();
            foreach (var state in numberedStates)
            {
                if (state == null) continue;
                
                string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                if (int.TryParse(numberStr, out _) || UserAssignedNumbers.ContainsKey(state))
                {
                    statesToClone.Add(state);
                }
            }
            
            // Log the states that will actually be processed
            Debug.Log($"[SYNCER] States to clone in binary mode: {statesToClone.Count}");
            foreach (var state in statesToClone)
            {
                string numberStr = Regex.Match(state.name, @"\d{1,3}$").Value;
                if (int.TryParse(numberStr, out int num))
                {
                    Debug.Log($"[SYNCER]  - {state.name} (auto-numbered: {num})");
                }
                else if (UserAssignedNumbers.TryGetValue(state, out int userNum))
                {
                    Debug.Log($"[SYNCER]  - {state.name} (user-assigned: {userNum})");
                }
            }
            
            for (int i = 0; i < statesToClone.Count; i++)
            {
                var originalState = statesToClone[i];
                string clonedStateName = clonedStatePrefix + originalState.name;
                
                int row = i / statesPerRow;
                int col = i % statesPerRow;
                
                Vector3 newPosition = new Vector3(
                    startX + (col * horizontalSpacing),
                    startY - (row * verticalSpacing),
                    0f
                );
                
                AnimatorState clonedState = selectedLayer.stateMachine.AddState(clonedStateName, newPosition);
                clonedState.motion = originalState.motion;
                
                if (originalState.behaviours != null && originalState.behaviours.Length > 0)
                {
                    foreach (var behaviour in originalState.behaviours)
                    {
                        if (removeParameterDriverFromRemote &&
                            (behaviour.GetType().Name.Contains("AvatarParameterDriver") ||
                             behaviour.GetType().FullName.Contains("VRC.AvatarParametersDriver")))
                        {
                            continue;
                        }
                        
                        try
                        {
                            System.Type behaviourType = behaviour.GetType();
                            StateMachineBehaviour newBehaviour = clonedState.AddStateMachineBehaviour(behaviourType);
                            
                            SerializedObject source = new SerializedObject(behaviour);
                            SerializedObject dest = new SerializedObject(newBehaviour);
                            
                            SerializedProperty property = source.GetIterator();
                            while (property.NextVisible(true))
                            {
                                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                    property.objectReferenceValue == null)
                                {
                                    continue;
                                }
                                
                                dest.CopyFromSerializedProperty(property);
                            }
                            
                            dest.ApplyModifiedProperties();
                        }
                        catch (System.Exception)
                        {
                        }
                    }
                }
                
                clonedStates.Add(clonedState);
            }
            
            try
            {
                var anyStatePositionProperty = typeof(AnimatorStateMachine).GetProperty("anyStatePosition",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (anyStatePositionProperty != null)
                {
                    anyStatePositionProperty.SetValue(selectedLayer.stateMachine, originalAnyStatePosition);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not restore Any State position: {e.Message}");
            }
            
            if (addParameterDriverForLocalSync)
            {
                Debug.Log($"[SYNCER] Adding binary parameter drivers for local sync...");
                for (int i = 0; i < numberedStates.Count; i++)
                {
                    AnimatorState originalState = numberedStates[i];
                    if (originalState == null) continue;
                    
                    string originalStateName = originalState.name;
                    int stateNumber;
                    
                    string numberStr = Regex.Match(originalStateName, @"\d{1,3}$").Value;
                    if (int.TryParse(numberStr, out stateNumber))
                    {
                        Debug.Log($"[SYNCER] Adding binary parameter drivers to original state '{originalState.name}' for state number {stateNumber}");
                        ParameterDriverUtilities.AddBinaryParameterDriversToState(originalState, stateNumber, SelectedBoolParameters.ToArray());
                    }
                    else if (UserAssignedNumbers.TryGetValue(originalState, out stateNumber))
                    {
                        Debug.Log($"[SYNCER] Adding binary parameter drivers to original state '{originalState.name}' for state number {stateNumber}");
                        ParameterDriverUtilities.AddBinaryParameterDriversToState(originalState, stateNumber, SelectedBoolParameters.ToArray());
                    }
                    else
                    {
                        Debug.Log($"[SYNCER] Skipping state '{originalState.name}' - no number assigned");
                    }
                }
            }

            for (int i = 0; i < clonedStates.Count; i++)
            {
                AnimatorState sourceState = clonedStates[i];
                
                int sourceStateNumber;
                string sourceNumberStr = Regex.Match(sourceState.name, @"\d{1,3}$").Value;
                
                if (int.TryParse(sourceNumberStr, out sourceStateNumber))
                {
                }
                else
                {
                    string originalStateName = sourceState.name.Replace(clonedStatePrefix, "");
                    
                    AnimatorState originalState = numberedStates.Find(s => s.name == originalStateName);
                    
                    if (originalState != null && UserAssignedNumbers.TryGetValue(originalState, out sourceStateNumber))
                    {
                    }
                    else
                    {
                        Debug.LogWarning($"Could not determine number for state: {sourceState.name}");
                        continue;
                    }
                }
                
                bool[] sourceBinary = BinaryEncoder.StateNumberToBinary(sourceStateNumber, SelectedBoolParameters.Count);
                string sourceBinaryStr = string.Join("", sourceBinary.Select(b => b ? "1" : "0"));
                Debug.Log($"Source state {sourceState.name} (Number: {sourceStateNumber}) Binary: {sourceBinaryStr}");
                
                for (int j = 0; j < clonedStates.Count; j++)
                {
                    if (i != j)
                    {
                        AnimatorState destinationState = clonedStates[j];
                        AnimatorStateTransition transition = sourceState.AddTransition(destinationState);
                        
                        int destinationStateNumber;
                        string destinationNumberStr = Regex.Match(destinationState.name, @"\d{1,3}$").Value;
                        bool hasDestinationNumber = false;
                        
                        if (int.TryParse(destinationNumberStr, out destinationStateNumber))
                        {
                            hasDestinationNumber = true;
                        }
                        else
                        {
                            string originalStateName = destinationState.name.Replace(clonedStatePrefix, "");
                            
                            AnimatorState originalState = numberedStates.Find(s => s.name == originalStateName);
                            
                            if (originalState != null && UserAssignedNumbers.TryGetValue(originalState, out destinationStateNumber))
                            {
                                hasDestinationNumber = true;
                            }
                        }
                        
                        if (hasDestinationNumber)
                        {
                            bool[] destBinary = BinaryEncoder.StateNumberToBinary(destinationStateNumber, SelectedBoolParameters.Count);
                            string destBinaryStr = string.Join("", destBinary.Select(b => b ? "1" : "0"));
                            Debug.Log($"Destination state {destinationState.name} (Number: {destinationStateNumber}) Binary: {destBinaryStr}");
                            
                            var binaryConditions = BinaryEncoder.CreateBinaryConditions(destinationStateNumber, SelectedBoolParameters.ToArray());
                            
                            transition.conditions = new AnimatorCondition[0];
                            
                            foreach (var condition in binaryConditions)
                            {
                                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
                                
                                string modeStr = condition.mode == AnimatorConditionMode.If ? "If" :
                                                 condition.mode == AnimatorConditionMode.IfNot ? "IfNot" :
                                                 condition.mode.ToString();
                                
                                Debug.Log($"  Adding condition: {condition.parameter} {modeStr} {condition.threshold}");
                            }
                            
                            Debug.Log($"Transition from {sourceState.name} to {destinationState.name} has {transition.conditions.Length} conditions:");
                            foreach (var condition in transition.conditions)
                            {
                                string modeStr = condition.mode == AnimatorConditionMode.If ? "If" :
                                                 condition.mode == AnimatorConditionMode.IfNot ? "IfNot" :
                                                 condition.mode.ToString();
                                
                                Debug.Log($"  Condition: {condition.parameter} {modeStr} {condition.threshold}");
                            }
                        }
                        else
                        {
                            var binaryConditions = BinaryEncoder.CreateBinaryConditions(sourceStateNumber, SelectedBoolParameters.ToArray());
                            
                            transition.conditions = new AnimatorCondition[0];
                            
                            foreach (var condition in binaryConditions)
                            {
                                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
                                
                                string modeStr = condition.mode == AnimatorConditionMode.If ? "If" :
                                                 condition.mode == AnimatorConditionMode.IfNot ? "IfNot" :
                                                 condition.mode.ToString();
                                
                                Debug.Log($"  Adding fallback condition: {condition.parameter} {modeStr} {condition.threshold}");
                            }
                            
                            Debug.LogWarning($"Could not determine number for destination state: {destinationState.name}, using source state number: {sourceStateNumber}");
                        }
                        
                        transition.hasExitTime = false;
                        transition.exitTime = 0;
                        transition.hasFixedDuration = true;
                        transition.duration = 0.1f;
                        transition.offset = 0;
                    }
                }
            }
            
            EditorUtility.SetDirty(workingController);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log($"[SYNCER] Successfully created binary mode clone network with {clonedStates.Count} states");
            Debug.Log($"[SYNCER] Used {BinaryBitDepth} boolean parameters: {string.Join(", ", SelectedBoolParameters)}");
            
            string successMessage = $"Created binary interconnected clone network with {clonedStates.Count} numbered states using {BinaryBitDepth} boolean parameters";
            EditorUtility.DisplayDialog("Success", successMessage, "OK");
        }


        /// Populates and sorts the working state list. States with numeric suffixes are prioritized.
        public void FindNumberedStates(AnimatorControllerLayer layer)
        {
            numberedStates.Clear();
            
            List<AnimatorState> statesWithNumbers = new List<AnimatorState>();
            List<AnimatorState> statesWithoutNumbers = new List<AnimatorState>();
            
            foreach (var state in layer.stateMachine.states)
            {
                if (NumberPatternRegex.IsMatch(state.state.name))
                {
                    statesWithNumbers.Add(state.state);
                }
                else
                {
                    statesWithoutNumbers.Add(state.state);
                }
            }
            
            numberedStates.AddRange(statesWithNumbers);
            
            numberedStates.AddRange(statesWithoutNumbers);
            
            numberedStates.Sort((a, b) => {
                int numA = ExtractStateNumber(a.name);
                int numB = ExtractStateNumber(b.name);
                return numA.CompareTo(numB);
            });
        }
        

        /// Attempts to read a numeric suffix from state name, else falls back to user-assigned mapping.
        public int ExtractStateNumber(string stateName)
        {
            Match match = StateNumberRegex.Match(stateName);
            if (match.Success && int.TryParse(match.Value, out int stateNumber))
            {
                return stateNumber;
            }
            
            foreach (var entry in UserAssignedNumbers)
            {
                if (entry.Key.name == stateName)
                {
                    return entry.Value;
                }
            }
            
            return int.MaxValue;
        }
        

        /// Gets the effective number for a state (suffix or user-assigned), used for wiring transitions.
        public int GetStateNumber(AnimatorState state)
        {
            if (state == null)
            {
                return int.MaxValue;
            }
            
            Match match = StateNumberRegex.Match(state.name);
            if (match.Success && int.TryParse(match.Value, out int stateNumber))
            {
                return stateNumber;
            }
            
            if (UserAssignedNumbers.TryGetValue(state, out int userNumber))
            {
                return userNumber;
            }
            
            return int.MaxValue;
        }
        

        /// Assigns a manual number to a state for cases where no suffix exists.
        public void SetUserAssignedNumber(AnimatorState state, int number)
        {
            if (state != null)
            {
                UserAssignedNumbers[state] = number;
                Debug.Log($"AnimatorCloner: Set user-assigned number {number} for state '{state.name}'");
                Debug.Log($"UserAssignedNumbers now contains {UserAssignedNumbers.Count} entries");
                
                foreach (var kvp in UserAssignedNumbers)
                {
                    Debug.Log($"  State '{kvp.Key.name}' has number {kvp.Value}");
                }
            }
        }
        

        /// Removes a manual number assignment for the given state.
        public void RemoveUserAssignedNumber(AnimatorState state)
        {
            if (state != null && UserAssignedNumbers.ContainsKey(state))
            {
                UserAssignedNumbers.Remove(state);
            }
        }
        

        /// True if the state's name ends with a numeric suffix.
        public bool HasNumericSuffix(AnimatorState state)
        {
            if (state == null)
            {
                return false;
            }
            
            return NumberPatternRegex.IsMatch(state.name);
        }
    }
}