using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq;

namespace JaxTools.AnimatorTools
{

    /// Utilities for inspecting AnimatorController parameters and computing
    /// binary-state related recommendations and examples.
    public static class ParameterDetectionUtilities
    {

        /// Returns all boolean parameter names from an AnimatorController.
        public static string[] ExtractBoolParameters(AnimatorController controller)
        {
            if (controller == null || controller.parameters == null)
            {
                return new string[0];
            }

            return controller.parameters
                .Where(p => p.type == AnimatorControllerParameterType.Bool)
                .Select(p => p.name)
                .ToArray();
        }

        /// Returns all int parameter names from an AnimatorController.
        public static string[] ExtractIntParameters(AnimatorController controller)
        {
            if (controller == null || controller.parameters == null)
            {
                return new string[0];
            }

            return controller.parameters
                .Where(p => p.type == AnimatorControllerParameterType.Int)
                .Select(p => p.name)
                .ToArray();
        }

        /// Groups controller parameters by their type into a structured result.
        public static ParameterGroup CategorizeParameters(AnimatorController controller)
        {
            var result = new ParameterGroup();
            
            if (controller == null || controller.parameters == null)
            {
                return result;
            }

            foreach (var param in controller.parameters)
            {
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        result.BoolParameters.Add(param.name);
                        break;
                    case AnimatorControllerParameterType.Int:
                        result.IntParameters.Add(param.name);
                        break;
                    case AnimatorControllerParameterType.Float:
                        result.FloatParameters.Add(param.name);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        result.TriggerParameters.Add(param.name);
                        break;
                }
            }

            return result;
        }


        /// Validates a binary encoding configuration and computes derived metrics.
        public static ValidationResult ValidateBinaryConfiguration(string[] selectedBoolParams, int bitDepth)
        {
            var result = new ValidationResult();

            if (selectedBoolParams == null || selectedBoolParams.Length == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No boolean parameters selected";
                return result;
            }

            if (bitDepth < 1)
            {
                result.Success = false;
                result.ErrorMessage = "Bit depth must be at least 1";
                return result;
            }

            if (bitDepth > selectedBoolParams.Length)
            {
                result.Success = false;
                result.ErrorMessage = $"Bit depth ({bitDepth}) cannot exceed available parameters ({selectedBoolParams.Length})";
                return result;
            }

            if (bitDepth > 8)
            {
                result.Success = false;
                result.ErrorMessage = "Bit depth cannot exceed 8 (maximum 256 states)";
                return result;
            }

            var duplicates = selectedBoolParams.GroupBy(p => p)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                result.Success = false;
                result.ErrorMessage = "Duplicate parameter names found: " + string.Join(", ", duplicates);
                return result;
            }

            result.Success = true;
            result.MaxStates = (int)Mathf.Pow(2, bitDepth);
            result.UsedParameters = bitDepth;
            result.AvailableParameters = selectedBoolParams.Length;
            result.ParameterUtilization = (float)bitDepth / selectedBoolParams.Length * 100f;

            return result;
        }


        /// Suggests the minimal bit depth that supports the desired number of states.
        public static int GetRecommendedBitDepth(string[] selectedBoolParams, int desiredMaxStates)
        {
            if (selectedBoolParams == null || selectedBoolParams.Length == 0)
            {
                return 0;
            }

            for (int depth = 1; depth <= Mathf.Min(selectedBoolParams.Length, 8); depth++)
            {
                int maxStates = (int)Mathf.Pow(2, depth);
                if (maxStates >= desiredMaxStates)
                {
                    return depth;
                }
            }

            return Mathf.Min(selectedBoolParams.Length, 8);
        }


        /// Converts an int state number to a binary array with the given bit depth.
        public static bool[] StateNumberToBinary(int stateNumber, int bitDepth)
        {
            if (bitDepth < 1 || bitDepth > 8)
            {
                return new bool[0];
            }

            var binary = new bool[bitDepth];

            for (int i = 0; i < bitDepth; i++)
            {

                binary[i] = ((stateNumber >> i) & 1) == 1;
            }

            return binary;
        }

        /// Picks the first N parameters to use as bits for encoding.
        public static string[] GetParametersForBits(string[] allParameters, int bitDepth)
        {
            if (allParameters == null || bitDepth <= 0 || bitDepth > allParameters.Length)
            {
                return new string[0];
            }

            var selectedParams = new string[bitDepth];
            for (int i = 0; i < bitDepth; i++)
            {
                selectedParams[i] = allParameters[i];
            }

            return selectedParams;
        }


        /// Generates example mappings of state numbers to binary strings for quick reference.
        public static List<BinaryExample> GetBinaryExamples(int bitDepth, int examplesToShow = 5)
        {
            var examples = new List<BinaryExample>();
            int maxExamples = Mathf.Min(examplesToShow, (int)Mathf.Pow(2, bitDepth));

            for (int i = 0; i < maxExamples; i++)
            {
                var binary = StateNumberToBinary(i, bitDepth);
                var binaryString = string.Join("", binary.Select(b => b ? "1" : "0"));
                examples.Add(new BinaryExample
                {
                    StateNumber = i,
                    BinaryString = binaryString,
                    BinaryValues = binary,
                    Description = $"State {i}: {binaryString}"
                });
            }

            return examples;
        }



        /// Container grouping parameter names by type.
        public class ParameterGroup
        {
            public List<string> BoolParameters = new List<string>();
            public List<string> IntParameters = new List<string>();
            public List<string> FloatParameters = new List<string>();
            public List<string> TriggerParameters = new List<string>();

            public bool HasBoolParameters => BoolParameters.Count > 0;
            public bool HasIntParameters => IntParameters.Count > 0;
        }
    
        /// Result of validating a binary configuration along with computed stats.
        public class ValidationResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public int MaxStates { get; set; }
            public int UsedParameters { get; set; }
            public int AvailableParameters { get; set; }
            public float ParameterUtilization { get; set; }
        }

        /// Simple DTO showcasing a state number and its binary representation.
        public class BinaryExample
        {
            public int StateNumber { get; set; }
            public string BinaryString { get; set; }
            public bool[] BinaryValues { get; set; }
            public string Description { get; set; }
        }
    }
}