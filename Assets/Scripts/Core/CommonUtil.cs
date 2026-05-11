using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Core
{
    public static class CommonUtil
    {
#if UNITY_EDITOR
        /// <summary>
        /// Save AssetDatabase
        /// </summary>
        /// <param name="obj"></param>
        public static void SaveScriptableObject(Object obj)
        {
            // Mark the ScriptableObject as dirty
            UnityEditor.EditorUtility.SetDirty(obj);

            // Save the changes to the asset file
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log("ScriptableObject updated and saved!");
        }
#endif

        /// <summary>
        /// Convert string to int32
        /// </summary>
        /// <param name="text"></param>
        /// <returns>int</returns>
        public static int ConvertToInt32(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        /// <summary>
        /// Convert string to int64
        /// </summary>
        /// <param name="text"></param>
        /// <returns>int</returns>
        public static long ConvertToInt64(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        /// <summary>
        /// Convert string to float
        /// </summary>
        /// <param name="text"></param>
        /// <returns>float</returns>
        public static float ConvertToSingle(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return float.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0f;
        }

        /// <summary>
        /// Convert string to double
        /// </summary>
        /// <param name="text"></param>
        /// <returns>double</returns>
        public static double ConvertToDouble(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0d;
        }

        /// <summary>
        /// Convert string to decimal
        /// </summary>
        /// <param name="text"></param>
        /// <returns>decimal</returns>
        public static decimal ConvertToDecimal(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
        }

        /// <summary>
        /// Given a list of weights, return one of them at random treating the data as weights.
        /// Return value of -1 means invalid input (nothing to random from or no positive weights).
        /// </summary>
        public static int GetRandomIndexByWeight(List<int> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                return -1;
            }

            // Calculate total weight, ignore values <= 0
            var totalWeight = 0;
            foreach (var weight in weights)
            {
                if (weight > 0)
                {
                    totalWeight += weight;
                }
            }

            if (totalWeight <= 0)
            {
                // Nothing with positive chance to random from
                return -1;
            }

            // Roll a random number from [1 to totalWeight] inclusive
            var randomValue = Random.Range(1, totalWeight + 1);

            // Select based on the weight, ignore values <= 0
            var cumulativeWeight = 0;
            for (var i = 0; i < weights.Count; i++)
            {
                var weight = weights[i];
                if (weight > 0)
                {
                    cumulativeWeight += weight;
                    if (randomValue <= cumulativeWeight)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Given a list of weights, return one of them at random treating the data as weights.
        /// Return value of -1 means invalid input (nothing to random from or no positive weights).
        /// </summary>
        public static int GetRandomIndexByWeight(List<float> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                return -1;
            }

            // Calculate total weight, ignore values <= 0
            var totalWeight = 0f;
            foreach (var weight in weights)
            {
                if (weight > 0f)
                {
                    totalWeight += weight;
                }
            }

            if (totalWeight <= 0f)
            {
                // Nothing with positive chance to random from
                return -1;
            }

            // Roll a random number from [1 to totalWeight] inclusive
            var randomValue = Random.Range(0f, totalWeight);

            // Select based on the weight, ignore values <= 0
            var cumulativeWeight = 0f;
            for (var i = 0; i < weights.Count; i++)
            {
                var weight = weights[i];
                if (weight > 0)
                {
                    cumulativeWeight += weight;
                    if (randomValue <= cumulativeWeight)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        public static void ForceRebuildLayoutImmediateRecursive(RectTransform rt)
        {
            // Depth-first search to go rebuild children LayoutGroup and ContentSizeFitter first
            foreach (Transform child in rt)
            {
                if (child.gameObject.activeSelf && child is RectTransform childRt)
                {
                    ForceRebuildLayoutImmediateRecursive(childRt);
                }
            }

            // After handling all children, update layout of ourselves
            var lg = rt.GetComponent<LayoutGroup>();
            var csf = rt.GetComponent<ContentSizeFitter>();
            if ((lg != null && lg.enabled) || (csf != null && csf.enabled))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
        }
    }
}