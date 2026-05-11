using Core;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

[CreateAssetMenu(fileName = "DModule", menuName = "Data/DModule", order = 3)]
public class DModule : ScriptableObject, IDataImport
{
    private static DModule s_loadedData;
    private static Dictionary<int, ModuleData> s_cachedDataDict;

    [field: SerializeField]
    public List<ModuleData> Data { get; private set; }

    public static DModule GetAllData()
    {
        EnsureLoaded();
        return s_loadedData;
    }

    public static ModuleData? GetDataById(int id)
    {
        EnsureLoaded();
        return s_cachedDataDict.TryGetValue(id, out var result) ? result : null;
    }

    private static void EnsureLoaded()
    {
        if (s_loadedData == null)
        {
            s_loadedData = Resources.Load<DModule>("data/DModule");
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
            Debug.LogError("Failed to load DModule data.");
            return;
        }

        if (s_loadedData.Data == null)
        {
            return;
        }

        foreach (var moduleData in s_loadedData.Data)
        {
            if (s_cachedDataDict.ContainsKey(moduleData.ModuleId))
            {
                Debug.LogError($"Duplicate Id {moduleData.ModuleId}");
                continue;
            }

            s_cachedDataDict.Add(moduleData.ModuleId, moduleData);
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
            var ModuleData = new ModuleData
            {
                ModuleId = CommonUtil.ConvertToInt32(paramList[1]),
                ParsedLevel = paramList[2],
            };
            s_loadedData.Data.Add(ModuleData);
        }

        RebuildCache();
        CommonUtil.SaveScriptableObject(s_loadedData);
    }
#endif
}

[Serializable]
public struct ModuleData
{
    [field: SerializeField]
    public int ModuleId { get; set; }

    [field: SerializeField]
    public string ParsedLevel { get; set; }
}
