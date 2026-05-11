using Core;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

[CreateAssetMenu(fileName = "DWorld", menuName = "Data/DWorld", order = 3)]
public class DWorld : ScriptableObject, IDataImport
{
    private static DWorld s_loadedData;
    private static Dictionary<string, WorldData> s_cachedDataDict;

    [field: SerializeField]
    public List<WorldData> Data { get; private set; }

    public static DWorld GetAllData()
    {
        EnsureLoaded();
        return s_loadedData;
    }

    public static WorldData? GetDataById(string id)
    {
        EnsureLoaded();
        return s_cachedDataDict.TryGetValue(id, out var result) ? result : null;
    }

    private static void EnsureLoaded()
    {
        if (s_loadedData == null)
        {
            s_loadedData = Resources.Load<DWorld>("data/DWorld");
        }

        if (s_cachedDataDict == null)
        {
            RebuildCache();
        }
    }

    private static void RebuildCache()
    {
        s_cachedDataDict = new();

        if (s_loadedData == null)
        {
            Debug.LogError("Failed to load DWorld data.");
            return;
        }

        if (s_loadedData.Data == null)
        {
            return;
        }

        foreach (var worldData in s_loadedData.Data)
        {
            if (s_cachedDataDict.ContainsKey(worldData.WorldId))
            {
                Debug.LogError($"Duplicate Id {worldData.WorldId}");
                continue;
            }

            s_cachedDataDict.Add(worldData.WorldId, worldData);
        }
    }

#if UNITY_EDITOR
    public static void ImportData(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        s_loadedData = GetAllData();
        if (s_loadedData == null)
        {
            return;
        }

        if (s_loadedData.Data == null)
        {
            s_loadedData.Data = new();
        }
        else
        {
            s_loadedData.Data.Clear();
        }

        // special handling for shape parameter and percentage
        var pattern = @"[{}""]";
        text = text.Replace("\r\n", "\n");      // handle window line break
        text = text.Replace("\n", "|");
        text = Regex.Replace(text, pattern, "");

        // Split data into lines
        var lines = text.Split(new char[] { '\r', '|' }, StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            // Comment and Header
            if (lines[i][0].Equals('#') || lines[i][0].Equals('$'))
            {
                continue;
            }

            // Empty line
            var trimLine = lines[i].Trim();
            var testList = trimLine.Split('\t');
            if (testList.Length == 1 && string.IsNullOrEmpty(testList[0]))
            {
                continue;
            }

            // Split
            var paramList = lines[i].Split('\t');
            for (var j = 0; j < paramList.Length; j++)
            {
                paramList[j] = paramList[j].Trim();
            }

            // New item
            var WorldData = new WorldData
            {
                WorldId = paramList[1],
                TaskCount = CommonUtil.ConvertToInt32(paramList[2]),
                TimeLimit = CommonUtil.ConvertToSingle(paramList[3]),
            };
            s_loadedData.Data.Add(WorldData);
        }

        RebuildCache();
        CommonUtil.SaveScriptableObject(s_loadedData);
    }
#endif
}

[Serializable]
public struct WorldData
{
    [field: SerializeField]
    public string WorldId { get; set; }

    [field: SerializeField]
    public int TaskCount { get; set; }

    [field: SerializeField]
    public float TimeLimit { get; set; }
}
