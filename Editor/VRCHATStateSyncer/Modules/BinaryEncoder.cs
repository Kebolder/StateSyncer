using UnityEngine;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq;

namespace JaxTools.AnimatorTools
{


    /// Pure helpers for encoding/decoding state numbers to and from binary across
    /// boolean animator parameters and for creating AnimatorConditions to match them.
    public static class BinaryEncoder
    {

        /// Converts a state number to a little-endian binary array with the given bit depth.
        /// Values are wrapped into range [0, 2^bitDepth).
        public static bool[] StateNumberToBinary(int stateNumber, int bitDepth)
        {
            if (bitDepth < 1 || bitDepth > 8)
            {
                return new bool[0];
            }

            int maxStates = (int)Mathf.Pow(2, bitDepth);
            stateNumber = stateNumber % maxStates;

            var binary = new bool[bitDepth];
            
            for (int i = 0; i < bitDepth; i++)
            {
                binary[i] = ((stateNumber >> i) & 1) == 1;
            }

            return binary;
        }


        /// Converts a binary array (little-endian) back to a state number.
        public static int BinaryToStateNumber(bool[] binaryValues)
        {
            if (binaryValues == null || binaryValues.Length == 0)
            {
                return 0;
            }

            int stateNumber = 0;
            int bitDepth = binaryValues.Length;

            for (int i = 0; i < bitDepth; i++)
            {
                if (binaryValues[i])
                {
                    stateNumber |= (1 << i);
                }
            }

            return stateNumber;
        }


        /// Creates name/value pairs for each boolean parameter representing the binary pattern of targetState.
        public static List<ParameterCondition> GenerateParameterConditions(int targetState, string[] parameterNames)
        {
            var conditions = new List<ParameterCondition>();
            
            if (parameterNames == null || parameterNames.Length == 0)
            {
                return conditions;
            }

            int bitDepth = Mathf.Min(parameterNames.Length, 8);
            var binary = StateNumberToBinary(targetState, bitDepth);

            string binaryStr = string.Join("", binary.Select(b => b ? "1" : "0"));
            Debug.Log($"GenerateParameterConditions for state {targetState}, binary: {binaryStr}");

            for (int i = 0; i < bitDepth; i++)
            {
                conditions.Add(new ParameterCondition
                {
                    ParameterName = parameterNames[i],
                    Value = binary[i]
                });
                
                Debug.Log($"  Parameter condition: {parameterNames[i]} = {binary[i]}");
            }

            return conditions;
        }


        /// Builds a set of AnimatorConditions that evaluate the boolean parameters to match a target state.
        public static AnimatorCondition[] CreateBinaryConditions(int targetState, string[] parameterNames)
        {
            if (parameterNames == null || parameterNames.Length == 0)
            {
                return new AnimatorCondition[0];
            }

            int bitDepth = Mathf.Min(parameterNames.Length, 8);
            var binary = StateNumberToBinary(targetState, bitDepth);
            var conditions = new List<AnimatorCondition>();

            for (int i = 0; i < bitDepth; i++)
            {
                var condition = new AnimatorCondition
                {
                    parameter = parameterNames[i],
                    mode = binary[i] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    threshold = 0f
                };
                conditions.Add(condition);
            }

            return conditions.ToArray();
        }


        /// Produces a human-readable summary of the binary encoding for a state number.
        public static string GenerateBinaryDescription(int stateNumber, string[] parameterNames)
        {
            if (parameterNames == null || parameterNames.Length == 0)
            {
                return "No parameters selected";
            }

            int bitDepth = Mathf.Min(parameterNames.Length, 8);
            var binary = StateNumberToBinary(stateNumber, bitDepth);
            var binaryString = string.Join("", binary.Select(b => b ? "1" : "0"));
            
            var descriptions = new List<string>();
            for (int i = 0; i < bitDepth; i++)
            {
                string status = binary[i] ? "true" : "false";
                descriptions.Add($"{parameterNames[i]} = {status}");
            }

            return $"State {stateNumber} ({binaryString}): {string.Join(", ", descriptions)}";
        }


        /// Checks that stateNumber is within the representable range for a given bit depth.
        public static bool IsValidStateNumber(int stateNumber, int bitDepth)
        {
            if (bitDepth < 1 || bitDepth > 8)
            {
                return false;
            }

            int maxStates = (int)Mathf.Pow(2, bitDepth);
            return stateNumber >= 0 && stateNumber < maxStates;
        }


        /// Returns the maximum representable state number for the bit depth.
        public static int GetMaxStateNumber(int bitDepth)
        {
            if (bitDepth < 1 || bitDepth > 8)
            {
                return 0;
            }

            return (int)Mathf.Pow(2, bitDepth) - 1;
        }


        /// Returns the minimum representable state number (always 0).
        public static int GetMinStateNumber(int bitDepth)
        {
            return 0;
        }


        /// Returns the total number of distinct states at the bit depth (2^bitDepth).
        public static int GetTotalStates(int bitDepth)
        {
            if (bitDepth < 1 || bitDepth > 8)
            {
                return 0;
            }

            return (int)Mathf.Pow(2, bitDepth);
        }

        /// Gets the expected boolean value for a specific parameter in a binary-encoded state number.
        /// This method determines what value a specific boolean parameter should have for a given state number.
        public static bool GetExpectedBooleanValue(string parameterName, int stateNumber, string[] parameterNames)
        {
            if (parameterNames == null || parameterNames.Length == 0 || !parameterNames.Contains(parameterName))
            {
                return false;
            }

            int parameterIndex = Array.IndexOf(parameterNames, parameterName);
            if (parameterIndex < 0 || parameterIndex >= 8) // Max 8 bits
            {
                return false;
            }

            int bitDepth = Mathf.Min(parameterNames.Length, 8);
            var binary = StateNumberToBinary(stateNumber, bitDepth);
            
            // Ensure the index is within the binary array bounds
            if (parameterIndex < binary.Length)
            {
                return binary[parameterIndex];
            }

            return false;
        }

        /// Computes the smallest bit depth that can encode at least requiredStates.
        public static int GetRecommendedBitDepth(int requiredStates)
        {
            if (requiredStates <= 0)
            {
                return 1;
            }

            for (int depth = 1; depth <= 8; depth++)
            {
                int maxStates = (int)Mathf.Pow(2, depth);
                if (maxStates >= requiredStates)
                {
                    return depth;
                }
            }

            return 8;
        }


        /// True if the bit depth can represent at least requiredStates.
        public static bool CanSupportStates(int bitDepth, int requiredStates)
        {
            if (bitDepth < 1 || bitDepth > 8)
            {
                return false;
            }

            int maxStates = (int)Mathf.Pow(2, bitDepth);
            return maxStates >= requiredStates;
        }


        /// Computes simple utilization metrics for a chosen bit depth and actual used states.
        public static EfficiencyMetrics GetEfficiencyMetrics(int bitDepth, int usedStates)
        {
            int totalStates = GetTotalStates(bitDepth);
            float utilization = totalStates > 0 ? (float)usedStates / totalStates * 100f : 0f;
            
            return new EfficiencyMetrics
            {
                TotalStates = totalStates,
                UsedStates = usedStates,
                UtilizationPercentage = utilization,
                IsEfficient = utilization >= 50f
            };
        }


        /// Name/value pair for a generated boolean parameter condition.
        public class ParameterCondition
        {
            public string ParameterName { get; set; }
            public bool Value { get; set; }
        }



        /// Simple utilization summary for binary encoding choices.
        public class EfficiencyMetrics
        {
            public int TotalStates { get; set; }
            public int UsedStates { get; set; }
            public float UtilizationPercentage { get; set; }
            public bool IsEfficient { get; set; }
        }
    }
}