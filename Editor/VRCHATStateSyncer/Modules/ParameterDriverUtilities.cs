using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using static VRC.SDKBase.VRC_AvatarParameterDriver;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Linq;

namespace JaxTools.AnimatorTools
{
    /// Utilities to add and verify VRChat Avatar Parameter Drivers on animator states.
    /// Supports simple int-based drivers and binary (multi-bool) driver sets.
    public static class ParameterDriverUtilities
    {
        private static System.Type[] cachedVRChatTypes;
        private static bool typesResolved = false;

        /// Adds a VRChat Avatar Parameter Driver to the given state that sets the specified
        /// int parameter to the provided state number if such a driver/entry does not already exist.
        /// <param name="state">Animator state to attach the driver to.</param>
        /// <param name="stateNumber">Desired int value to set.</param>
        /// <param name="parameterName">Animator parameter name to modify.</param>
        public static void AddParameterDriverToState(AnimatorState state, int stateNumber, string parameterName)
        {
            try
            {
                if (!ValidateParameterDriverInput(state, stateNumber, parameterName, out string errorMessage))
                {
                    Debug.LogError($"Validation failed: {errorMessage}");
                    return;
                }

                // Use comprehensive analysis to determine if we should add a driver
                var analysis = AnalyzeExistingParameterDrivers(state, stateNumber, parameterName);
                
                if (!analysis.IsValid)
                {
                    Debug.LogError($"Parameter driver analysis failed: {analysis.ErrorMessage}");
                    return;
                }

                Debug.Log($"Parameter Driver Analysis for state '{state.name}':\n{analysis.GetSummary()}");

                if (analysis.ShouldSkipAddingDriver())
                {
                    if (analysis.HasMatchingDriver)
                    {
                        Debug.Log($"Skipping Parameter Driver addition - matching driver already exists on state: {state.name}");
                    }
                    return;
                }

                if (!TryResolveVRChatTypes(out System.Type parameterDriverType, out System.Type parameterEntryType, out System.Type changeTypeEnum, out errorMessage))
                {
                    Debug.LogError($"Failed to resolve VRChat types: {errorMessage}");
                    return;
                }

                StateMachineBehaviour parameterDriver = state.AddStateMachineBehaviour(parameterDriverType);
                
                ConfigureParameterDriverProperties(parameterDriver, parameterDriverType);
                
                if (!TryAddParameterEntry(parameterDriver, parameterDriverType, parameterEntryType, changeTypeEnum,
                                         parameterName, stateNumber, out errorMessage))
                {
                    Debug.LogError($"Failed to add parameter entry: {errorMessage}");
                    return;
                }

                Debug.Log($"Successfully added Parameter Driver to state: {state.name} with parameter: {parameterName} = {stateNumber}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error adding Parameter Driver to state {state?.name}: {e.Message}");
            }
        }

        /// Checks whether a matching Parameter Driver entry already exists on the state
        /// to prevent duplicate entries for the same target value.
        private static bool HasExistingParameterDriver(AnimatorState state, int stateNumber, string parameterName)
        {
            if (state?.behaviours == null) return false;

            System.Type parameterDriverType = typeof(VRCAvatarParameterDriver);
            
            if (state.behaviours.Length == 0) return false;
            
            foreach (StateMachineBehaviour behaviour in state.behaviours)
            {
                if (behaviour != null && behaviour.GetType() == parameterDriverType)
                {
                    try
                    {
                        using (SerializedObject serializedDriver = new SerializedObject(behaviour))
                        {
                            // Check IsLocal property - if true, we should ignore this driver
                            SerializedProperty localOnlyProp = serializedDriver.FindProperty("localOnly");
                            if (localOnlyProp != null && localOnlyProp.boolValue)
                            {
                                Debug.Log($"Found existing Parameter Driver with IsLocal=true on state: {state.name}. Ignoring for state syncing.");
                                continue;
                            }
                            
                            SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                            
                            if (parametersArrayProp != null && parametersArrayProp.isArray)
                            {
                                if (parametersArrayProp.arraySize == 0) continue;
                                
                                for (int i = 0; i < parametersArrayProp.arraySize; i++)
                                {
                                    SerializedProperty parameterProp = parametersArrayProp.GetArrayElementAtIndex(i);
                                    SerializedProperty nameProp = parameterProp.FindPropertyRelative("name");
                                    SerializedProperty typeProp = parameterProp.FindPropertyRelative("type");
                                    SerializedProperty valueProp = parameterProp.FindPropertyRelative("value");
                                    
                                    if (nameProp != null && typeProp != null && valueProp != null &&
                                        nameProp.stringValue == parameterName &&
                                        typeProp.enumValueIndex == (int)ChangeType.Set &&
                                        Math.Abs(valueProp.floatValue - (float)stateNumber) < 0.001f)
                                    {
                                        Debug.Log($"Found existing Parameter Driver with parameter '{parameterName}' = {stateNumber} on state: {state.name}");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }
                }
            }
            
            return false;
        }

        /// Validates inputs for adding a single parameter driver entry.
        private static bool ValidateParameterDriverInput(AnimatorState state, int stateNumber, string parameterName, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (state == null)
            {
                errorMessage = "Animator state cannot be null";
                return false;
            }

            if (string.IsNullOrEmpty(parameterName))
            {
                errorMessage = "Parameter name cannot be null or empty";
                return false;
            }

            if (stateNumber < 0)
            {
                errorMessage = "State number cannot be negative";
                return false;
            }

            return true;
        }
        
        /// Resolves VRChat SDK types required for interacting with Avatar Parameter Driver via reflection.
        private static bool TryResolveVRChatTypes(out System.Type parameterDriverType, out System.Type parameterEntryType,
                                           out System.Type changeTypeEnum, out string errorMessage)
        {
            parameterDriverType = null;
            parameterEntryType = null;
            changeTypeEnum = null;
            errorMessage = string.Empty;
            
            if (!typesResolved)
            {
                cachedVRChatTypes = ResolveVRChatSDKTypes();
                typesResolved = true;
            }
            
            if (cachedVRChatTypes[0] == null)
            {
                errorMessage = "VRChat AvatarParameterDriver type not found. Make sure VRChat SDK is installed.";
                return false;
            }

            if (cachedVRChatTypes[1] == null)
            {
                errorMessage = "VRChat ParameterEntry type not found.";
                return false;
            }

            if (cachedVRChatTypes[2] == null)
            {
                errorMessage = "VRChat ChangeType enum not found.";
                return false;
            }

            parameterDriverType = cachedVRChatTypes[0];
            parameterEntryType = cachedVRChatTypes[1];
            changeTypeEnum = cachedVRChatTypes[2];
            
            return true;
        }
        
        /// Returns the concrete VRChat SDK types used by this utility. Split for caching.
        private static System.Type[] ResolveVRChatSDKTypes()
        {
            return new System.Type[]
            {
                typeof(VRCAvatarParameterDriver),
                typeof(Parameter),
                typeof(ChangeType)
            };
        }
        
        /// Sets initial driver properties (e.g., disables Local Only if available) for consistent behavior.
        private static void ConfigureParameterDriverProperties(StateMachineBehaviour parameterDriver, System.Type parameterDriverType)
        {
            var localOnlyField = parameterDriverType.GetField("localOnly", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (localOnlyField != null)
            {
                try
                {
                    localOnlyField.SetValue(parameterDriver, false);
                }
                catch (System.Exception)
                {
                }
            }
        }
        
        /// Appends a new parameter entry to the driver via SerializedObject, setting it to the desired value.
        private static bool TryAddParameterEntry(StateMachineBehaviour parameterDriver, System.Type parameterDriverType,
                                          System.Type parameterEntryType, System.Type changeTypeEnum,
                                          string parameterName, int stateNumber, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                Parameter newParameter = new Parameter
                {
                    name = parameterName,
                    type = ChangeType.Set,
                    value = (float)stateNumber
                };
                
                using (SerializedObject serializedDriver = new SerializedObject(parameterDriver))
                {
                    SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                    
                    if (parametersArrayProp == null)
                    {
                        errorMessage = "Parameters array property not found on Parameter Driver";
                        return false;
                    }
                    
                    int newIndex = parametersArrayProp.arraySize;
                    parametersArrayProp.arraySize++;
                    
                    SerializedProperty newElement = parametersArrayProp.GetArrayElementAtIndex(newIndex);
                    
                    SerializedProperty nameProperty = newElement.FindPropertyRelative("name");
                    SerializedProperty typeProperty = newElement.FindPropertyRelative("type");
                    SerializedProperty valueProperty = newElement.FindPropertyRelative("value");
                    
                    if (nameProperty != null) nameProperty.stringValue = newParameter.name;
                    if (typeProperty != null) typeProperty.enumValueIndex = (int)newParameter.type;
                    if (valueProperty != null) valueProperty.floatValue = newParameter.value;
                    
                    serializedDriver.ApplyModifiedProperties();
                }
                
                return true;
            }
            catch (System.Exception e)
            {
                errorMessage = $"Exception while adding parameter entry: {e.Message}";
                Debug.LogError($"Error adding parameter entry: {errorMessage}");
                return false;
            }
        }
        /// Adds a set of Parameter Driver entries that encode the state number into a binary pattern
        /// across multiple boolean parameters.
        public static void AddBinaryParameterDriversToState(AnimatorState state, int stateNumber, string[] parameterNames)
        {
            try
            {
                if (!ValidateBinaryParameterDriverInput(state, stateNumber, parameterNames, out string errorMessage))
                {
                    Debug.LogError($"Binary parameter driver validation failed: {errorMessage}");
                    return;
                }

                // Use comprehensive binary analysis to determine if we should add binary drivers
                var binaryAnalysis = AnalyzeExistingBinaryParameterDrivers(state, stateNumber, parameterNames);
                
                if (!binaryAnalysis.IsValid)
                {
                    Debug.LogError($"Binary parameter driver analysis failed: {binaryAnalysis.ErrorMessage}");
                    return;
                }

                Debug.Log($"Binary Parameter Driver Analysis for state '{state.name}':\n{binaryAnalysis.GetSummary()}");

                if (binaryAnalysis.ShouldSkipAddingBinaryDrivers())
                {
                    if (binaryAnalysis.BinaryParameters.Count > 0)
                    {
                        Debug.Log($"Skipping Binary Parameter Driver addition - all binary parameters are correctly set for state {stateNumber} on state: {state.name}");
                    }
                    return;
                }

                // Additional check specifically for binary drivers
                if (HasExistingBinaryParameterDrivers(state, stateNumber, parameterNames))
                {
                    Debug.Log($"Binary Parameter Drivers with state {stateNumber} already exist on state: {state.name}");
                    return;
                }

                if (!TryResolveVRChatTypes(out System.Type parameterDriverType, out System.Type parameterEntryType, out System.Type changeTypeEnum, out errorMessage))
                {
                    Debug.LogError($"Failed to resolve VRChat types: {errorMessage}");
                    return;
                }

                var binaryConditions = BinaryEncoder.GenerateParameterConditions(stateNumber, parameterNames);
                
                bool[] binary = BinaryEncoder.StateNumberToBinary(stateNumber, parameterNames.Length);
                string binaryStr = string.Join("", binary.Select(b => b ? "1" : "0"));
                Debug.Log($"Adding parameter drivers for state {state.name} (Number: {stateNumber}) Binary: {binaryStr}");

                StateMachineBehaviour parameterDriver = state.AddStateMachineBehaviour(parameterDriverType);
                ConfigureParameterDriverProperties(parameterDriver, parameterDriverType);

                foreach (var condition in binaryConditions)
                {
                    bool value = condition.Value;
                    float floatValue = value ? 1f : 0f;
                    
                    Debug.Log($"  Adding parameter driver: {condition.ParameterName} = {(value ? "true" : "false")} ({floatValue})");
                    
                    if (!TryAddParameterEntry(parameterDriver, parameterDriverType, parameterEntryType, changeTypeEnum,
                                             condition.ParameterName, (int)floatValue, out errorMessage))
                    {
                        Debug.LogWarning($"Failed to add parameter entry for {condition.ParameterName}: {errorMessage}");
                    }
                }

                Debug.Log($"Successfully added Binary Parameter Drivers to state: {state.name} for state number: {stateNumber}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error adding Binary Parameter Drivers to state {state?.name}: {e.Message}");
            }
        }

        /// Verifies if the state already has a binary-encoded set of driver entries matching the target state number.
        /// Enhanced to check IsLocal property and validate parameters.
        public static bool HasExistingBinaryParameterDrivers(AnimatorState state, int stateNumber, string[] parameterNames)
        {
            if (state?.behaviours == null || parameterNames == null || parameterNames.Length == 0)
            {
                return false;
            }

            System.Type parameterDriverType = typeof(VRCAvatarParameterDriver);
            
            if (state.behaviours.Length == 0) return false;
            
            foreach (StateMachineBehaviour behaviour in state.behaviours)
            {
                if (behaviour != null && behaviour.GetType() == parameterDriverType)
                {
                    try
                    {
                        using (SerializedObject serializedDriver = new SerializedObject(behaviour))
                        {
                            // Check IsLocal property - if true, we should ignore this driver
                            SerializedProperty localOnlyProp = serializedDriver.FindProperty("localOnly");
                            if (localOnlyProp != null && localOnlyProp.boolValue)
                            {
                                Debug.Log($"Found existing binary Parameter Driver with IsLocal=true on state: {state.name}. Ignoring for state syncing.");
                                continue;
                            }
                            
                            SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                            
                            if (parametersArrayProp != null && parametersArrayProp.isArray)
                            {
                                if (parametersArrayProp.arraySize == 0) continue;
                                
                                var parameterConditions = new List<string>();
                                
                                for (int i = 0; i < parametersArrayProp.arraySize; i++)
                                {
                                    SerializedProperty parameterProp = parametersArrayProp.GetArrayElementAtIndex(i);
                                    SerializedProperty nameProp = parameterProp.FindPropertyRelative("name");
                                    SerializedProperty typeProp = parameterProp.FindPropertyRelative("type");
                                    SerializedProperty valueProp = parameterProp.FindPropertyRelative("value");
                                    
                                    if (nameProp != null && typeProp != null && valueProp != null &&
                                        typeProp.enumValueIndex == (int)ChangeType.Set)
                                    {
                                        parameterConditions.Add($"{nameProp.stringValue}={valueProp.floatValue}");
                                    }
                                }
                                
                                var expectedConditions = BinaryEncoder.GenerateParameterConditions(stateNumber, parameterNames);
                                var expectedConditionsString = expectedConditions
                                    .OrderBy(c => c.ParameterName)
                                    .Select(c => $"{c.ParameterName}={(c.Value ? 1f : 0f)}")
                                    .ToList();
                                
                                var actualConditionsString = parameterConditions.OrderBy(c => c).ToList();
                                
                                if (actualConditionsString.SequenceEqual(expectedConditionsString))
                                {
                                    Debug.Log($"Found existing binary Parameter Driver with state {stateNumber} on state: {state.name}");
                                    return true;
                                }
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }
                }
            }
            
            return false;
        }

        /// Comprehensive analysis of existing parameter drivers on a state.
        /// Returns detailed information about existing drivers for smart decision making.
        public static ParameterDriverAnalysis AnalyzeExistingParameterDrivers(AnimatorState state, int expectedStateNumber, string expectedParameterName)
        {
            var analysis = new ParameterDriverAnalysis();
            
            if (state?.behaviours == null)
            {
                analysis.IsValid = false;
                analysis.ErrorMessage = "State or behaviours is null";
                return analysis;
            }

            System.Type parameterDriverType = typeof(VRCAvatarParameterDriver);
            
            foreach (StateMachineBehaviour behaviour in state.behaviours)
            {
                if (behaviour != null && behaviour.GetType() == parameterDriverType)
                {
                    try
                    {
                        using (SerializedObject serializedDriver = new SerializedObject(behaviour))
                        {
                            // Check IsLocal property
                            SerializedProperty localOnlyProp = serializedDriver.FindProperty("localOnly");
                            bool isLocal = localOnlyProp != null && localOnlyProp.boolValue;
                            
                            // Skip this driver if it's local only, as it can't be a network driver
                            if (isLocal)
                            {
                                continue;
                            }
                            
                            // Check parameters
                            SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                            
                            if (parametersArrayProp != null && parametersArrayProp.isArray && parametersArrayProp.arraySize > 0)
                            {
                                for (int i = 0; i < parametersArrayProp.arraySize; i++)
                                {
                                    SerializedProperty parameterProp = parametersArrayProp.GetArrayElementAtIndex(i);
                                    SerializedProperty nameProp = parameterProp.FindPropertyRelative("name");
                                    SerializedProperty typeProp = parameterProp.FindPropertyRelative("type");
                                    SerializedProperty valueProp = parameterProp.FindPropertyRelative("value");
                                    
                                    if (nameProp != null && typeProp != null && valueProp != null)
                                    {
                                        var parameterInfo = new ParameterInfo
                                        {
                                            Name = nameProp.stringValue,
                                            Type = (ChangeType)typeProp.enumValueIndex,
                                            Value = valueProp.floatValue,
                                            IsLocal = isLocal
                                        };
                                        
                                        analysis.AllParameters.Add(parameterInfo);
                                        
                                        // Check if this matches our expected sync parameter
                                        if (parameterInfo.Name == expectedParameterName &&
                                            parameterInfo.Type == ChangeType.Set &&
                                            Math.Abs(parameterInfo.Value - (float)expectedStateNumber) < 0.001f)
                                        {
                                            analysis.MatchingParameter = parameterInfo;
                                            analysis.HasMatchingDriver = true;
                                        }
                                    }
                                }
                            }
                            
                            analysis.HasDriver = true;
                            // Only set IsLocal to true if this is the first driver and it's local
                            // Since we skip local drivers above, this should remain false for network drivers
                        }
                    }
                    catch (System.Exception e)
                    {
                        analysis.IsValid = false;
                        analysis.ErrorMessage = $"Exception while analyzing driver: {e.Message}";
                        return analysis;
                    }
                }
            }
            
            analysis.IsValid = true;
            return analysis;
        }

        /// Comprehensive analysis of existing parameter drivers on a state for binary mode.
        /// Returns detailed information about existing drivers and validates boolean parameter values
        /// against the expected binary representation for the state number.
        public static BinaryParameterDriverAnalysis AnalyzeExistingBinaryParameterDrivers(AnimatorState state, int expectedStateNumber, string[] parameterNames)
        {
            var analysis = new BinaryParameterDriverAnalysis();
            
            if (state?.behaviours == null || parameterNames == null || parameterNames.Length == 0)
            {
                analysis.IsValid = false;
                analysis.ErrorMessage = "State, behaviours, or parameter names is null/empty";
                return analysis;
            }

            System.Type parameterDriverType = typeof(VRCAvatarParameterDriver);
            
            foreach (StateMachineBehaviour behaviour in state.behaviours)
            {
                if (behaviour != null && behaviour.GetType() == parameterDriverType)
                {
                    try
                    {
                        using (SerializedObject serializedDriver = new SerializedObject(behaviour))
                        {
                            // Check IsLocal property
                            SerializedProperty localOnlyProp = serializedDriver.FindProperty("localOnly");
                            bool isLocal = localOnlyProp != null && localOnlyProp.boolValue;
                            
                            // Skip this driver if it's local only, as it can't be a network driver
                            if (isLocal)
                            {
                                continue;
                            }
                            
                            // Check parameters
                            SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                            
                            if (parametersArrayProp != null && parametersArrayProp.isArray && parametersArrayProp.arraySize > 0)
                            {
                                for (int i = 0; i < parametersArrayProp.arraySize; i++)
                                {
                                    SerializedProperty parameterProp = parametersArrayProp.GetArrayElementAtIndex(i);
                                    SerializedProperty nameProp = parameterProp.FindPropertyRelative("name");
                                    SerializedProperty typeProp = parameterProp.FindPropertyRelative("type");
                                    SerializedProperty valueProp = parameterProp.FindPropertyRelative("value");
                                    
                                    if (nameProp != null && typeProp != null && valueProp != null)
                                    {
                                        var parameterInfo = new ParameterInfo
                                        {
                                            Name = nameProp.stringValue,
                                            Type = (ChangeType)typeProp.enumValueIndex,
                                            Value = valueProp.floatValue,
                                            IsLocal = isLocal
                                        };
                                        
                                        analysis.AllParameters.Add(parameterInfo);
                                        
                                        // For binary mode, we're interested in Set type parameters that match our expected parameters
                                        if (parameterInfo.Type == ChangeType.Set && parameterNames.Contains(parameterInfo.Name))
                                        {
                                            analysis.BinaryParameters.Add(parameterInfo);
                                            
                                            // Check if the boolean value matches expected
                                            bool expectedValue = BinaryEncoder.GetExpectedBooleanValue(parameterInfo.Name, expectedStateNumber, parameterNames);
                                            bool actualValue = parameterInfo.Value > 0.5f; // Convert float to bool
                                            
                                            if (actualValue == expectedValue)
                                            {
                                                analysis.CorrectBinaryParameters.Add(parameterInfo.Name);
                                            }
                                            else
                                            {
                                                analysis.IncorrectBinaryParameters.Add(new BinaryMismatchInfo
                                                {
                                                    ParameterName = parameterInfo.Name,
                                                    ExpectedValue = expectedValue,
                                                    ActualValue = actualValue
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                            
                            analysis.HasDriver = true;
                            // Only set IsLocal to true if this is the first driver and it's local
                            // Since we skip local drivers above, this should remain false for network drivers
                        }
                    }
                    catch (System.Exception e)
                    {
                        analysis.IsValid = false;
                        analysis.ErrorMessage = $"Exception while analyzing binary driver: {e.Message}";
                        return analysis;
                    }
                }
            }
            
            analysis.IsValid = true;
            return analysis;
        }

        /// Detailed information about a single parameter in a driver
        public class ParameterInfo
        {
            public string Name { get; set; }
            public ChangeType Type { get; set; }
            public float Value { get; set; }
            public bool IsLocal { get; set; }
            
            public override string ToString()
            {
                return $"{Name} ({Type}) = {Value}, IsLocal: {IsLocal}";
            }
        }

        /// Comprehensive analysis result for parameter driver scanning
        public class ParameterDriverAnalysis
        {
            public bool IsValid { get; set; } = false;
            public bool HasDriver { get; set; } = false;
            public bool IsLocal { get; set; } = false;
            public bool HasMatchingDriver { get; set; } = false;
            public ParameterInfo MatchingParameter { get; set; } = null;
            public List<ParameterInfo> AllParameters { get; set; } = new List<ParameterInfo>();
            public string ErrorMessage { get; set; } = string.Empty;
            
            /// Determines if we should skip adding a new driver based on existing ones
            public bool ShouldSkipAddingDriver()
            {
                if (!HasDriver) return false;
                if (IsLocal) return false; // Don't skip entirely if driver is local only, just ignore it for matching
                if (HasMatchingDriver) return true; // Skip if we have a matching non-local driver
                return false;
            }
            
            /// Provides a summary of the analysis for logging/debugging
            public string GetSummary()
            {
                if (!IsValid) return $"Invalid analysis: {ErrorMessage}";
                
                string summary = $"Parameter Driver Analysis for state:\n";
                summary += $"  Has Driver: {HasDriver}\n";
                summary += $"  Is Local: {IsLocal}\n";
                summary += $"  Has Matching Driver: {HasMatchingDriver}\n";
                summary += $"  Should Skip: {ShouldSkipAddingDriver()}\n";
                summary += $"  All Parameters ({AllParameters.Count}):\n";
                
                foreach (var param in AllParameters)
                {
                    summary += $"    - {param}\n";
                }
                
                if (MatchingParameter != null)
                {
                    summary += $"  Matching Parameter: {MatchingParameter}\n";
                }
                
                return summary;
            }
        }
        /// Convenience wrapper to create animator transition conditions that match a binary-encoded state.
        public static AnimatorCondition[] CreateBinaryConditions(int targetState, string[] parameterNames)
        {
            return BinaryEncoder.CreateBinaryConditions(targetState, parameterNames);
        }

        /// Validates inputs for adding binary parameter drivers.
        private static bool ValidateBinaryParameterDriverInput(AnimatorState state, int stateNumber, string[] parameterNames, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (state == null)
            {
                errorMessage = "Animator state cannot be null";
                return false;
            }

            if (parameterNames == null || parameterNames.Length == 0)
            {
                errorMessage = "Parameter names array cannot be null or empty";
                return false;
            }

            if (stateNumber < 0)
            {
                errorMessage = "State number cannot be negative";
                return false;
            }

            var duplicates = parameterNames.ToList().GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
            {
                errorMessage = "Duplicate parameter names found: " + string.Join(", ", duplicates);
                return false;
            }

            return true;
        }

        /// Detailed information about a binary parameter mismatch
        public class BinaryMismatchInfo
        {
            public string ParameterName { get; set; }
            public bool ExpectedValue { get; set; }
            public bool ActualValue { get; set; }
            
            public override string ToString()
            {
                return $"{ParameterName}: Expected {ExpectedValue}, Got {ActualValue}";
            }
        }

        /// Comprehensive analysis result for binary parameter driver scanning
        public class BinaryParameterDriverAnalysis
        {
            public bool IsValid { get; set; } = false;
            public bool HasDriver { get; set; } = false;
            public bool IsLocal { get; set; } = false;
            public List<ParameterInfo> AllParameters { get; set; } = new List<ParameterInfo>();
            public List<ParameterInfo> BinaryParameters { get; set; } = new List<ParameterInfo>();
            public List<string> CorrectBinaryParameters { get; set; } = new List<string>();
            public List<BinaryMismatchInfo> IncorrectBinaryParameters { get; set; } = new List<BinaryMismatchInfo>();
            public string ErrorMessage { get; set; } = string.Empty;
            
            /// Determines if we should skip adding binary drivers based on existing ones
            public bool ShouldSkipAddingBinaryDrivers()
            {
                if (!HasDriver) return false;
                if (IsLocal) return false; // Don't skip entirely if driver is local only, just ignore it for matching
                
                // If we have binary parameters, check if they're all correct
                if (BinaryParameters.Count > 0)
                {
                    return IncorrectBinaryParameters.Count == 0; // Skip if all binary parameters are correct
                }
                
                return false;
            }
            
            /// Provides a summary of the binary analysis for logging/debugging
            public string GetSummary()
            {
                if (!IsValid) return $"Invalid binary analysis: {ErrorMessage}";
                
                string summary = $"Binary Parameter Driver Analysis for state:\n";
                summary += $"  Has Driver: {HasDriver}\n";
                summary += $"  Is Local: {IsLocal}\n";
                summary += $"  Should Skip: {ShouldSkipAddingBinaryDrivers()}\n";
                summary += $"  All Parameters ({AllParameters.Count}):\n";
                
                foreach (var param in AllParameters)
                {
                    summary += $"    - {param}\n";
                }
                
                summary += $"  Binary Parameters ({BinaryParameters.Count}):\n";
                foreach (var param in BinaryParameters)
                {
                    summary += $"    - {param}\n";
                }
                
                summary += $"  Correct Binary Parameters ({CorrectBinaryParameters.Count}):\n";
                foreach (var param in CorrectBinaryParameters)
                {
                    summary += $"    - {param}\n";
                }
                
                if (IncorrectBinaryParameters.Count > 0)
                {
                    summary += $"  Incorrect Binary Parameters ({IncorrectBinaryParameters.Count}):\n";
                    foreach (var mismatch in IncorrectBinaryParameters)
                    {
                        summary += $"    - {mismatch}\n";
                    }
                }
                
                return summary;
            }
        }

        /// Helper method to get expected boolean value for a parameter in binary encoding
        public static bool GetExpectedBooleanValueForParameter(string parameterName, int stateNumber, string[] parameterNames)
        {
            return BinaryEncoder.GetExpectedBooleanValue(parameterName, stateNumber, parameterNames);
        }
    }
}