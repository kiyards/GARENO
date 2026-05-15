using Core;
using FMODUnity;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

[CreateAssetMenu(fileName = "DAudio", menuName = "Data/DAudio", order = 3)]
public class DAudio : ScriptableObject, IDataImport
{
    private static DAudio s_loadedData;
    private static Dictionary<string, AudioData> s_cachedDataDict;

    [field: SerializeField]
    public List<AudioData> Data { get; private set; }

    public static DAudio GetAllData()
    {
        EnsureLoaded();
        return s_loadedData;
    }

    public static AudioData? GetDataById(string id)
    {
        EnsureLoaded();
        return s_cachedDataDict.TryGetValue(id, out var result) ? result : null;
    }

    private static void EnsureLoaded()
    {
        if (s_loadedData == null)
        {
            s_loadedData = Resources.Load<DAudio>("data/DAudio");
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
            Debug.LogError("Failed to load DAudio data.");
            return;
        }

        if (s_loadedData.Data == null)
        {
            return;
        }

        foreach (var audioData in s_loadedData.Data)
        {
            if (s_cachedDataDict.ContainsKey(audioData.EventId))
            {
                Debug.LogError($"Duplicate Id {audioData.EventId}");
                continue;
            }

            s_cachedDataDict.Add(audioData.EventId, audioData);
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

        var pattern = @"[""]";
        text = text.Replace("\r\n", "\n");      // handle window line break
        text = text.Replace("\n", "|");
        text = Regex.Replace(text, pattern, "");

        // Split data into lines
        var lines = text.Split(new char[] { '\r', '|' }, StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Empty line
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Comment and Header
            if (line[0].Equals('#') || line[0].Equals('$'))
            {
                continue;
            }

            // Split
            var paramList = line.Split('\t');
            for (var j = 0; j < paramList.Length; j++)
            {
                paramList[j] = paramList[j].Trim();
            }

            if (paramList.Length < 6)
            {
                Debug.LogWarning($"Skipped DAudio row {i + 1}. Expected 6 columns but found {paramList.Length}.");
                continue;
            }

            // New item
            var audioData = new AudioData
            {
                EventId = paramList[1],
                EventPath = paramList[2],
                EventReference = BuildEventReference(paramList[2]),
                PlaybackType = ParseEnumOrDefault(paramList[3], AudioPlaybackType.PLAYBACK_ONESHOT, paramList[1]),
                SpatialMode = ParseEnumOrDefault(paramList[4], AudioSpatialMode.SPATIAL_2D, paramList[1]),
                ReturnMode = ParseEnumOrDefault(paramList[5], AudioReturnMode.RETURN_ONSTOPPED, paramList[1]),
            };
            s_loadedData.Data.Add(audioData);
        }

        RebuildCache();
        CommonUtil.SaveScriptableObject(s_loadedData);
    }

    private static EventReference BuildEventReference(string eventPath)
    {
        if (string.IsNullOrEmpty(eventPath))
        {
            return new EventReference();
        }

        try
        {
            return EventReference.Find(eventPath);
        }
        catch (InvalidOperationException)
        {
            EventManager.Startup();
            return EventReference.Find(eventPath);
        }
    }

    private static TEnum ParseEnumOrDefault<TEnum>(string value, TEnum defaultValue, string eventId)
        where TEnum : struct
    {
        if (Enum.TryParse(value, true, out TEnum result))
        {
            return result;
        }

        Debug.LogWarning($"Failed to parse {typeof(TEnum).Name} '{value}' for audio {eventId}. Using {defaultValue}.");
        return defaultValue;
    }
#endif
}

public enum AudioPlaybackType
{
    /// <summary>SFX played once. Used for normal sounds and UI clicks.</summary>
    PLAYBACK_ONESHOT,

    /// <summary>SFX loops until stopped. Used for charging, ambience, and persistent sounds.</summary>
    PLAYBACK_LOOP,
}

public enum AudioSpatialMode
{
    /// <summary>Sound has no world-space position. Used for UI, music, and ambience.</summary>
    SPATIAL_2D,

    /// <summary>Sound plays once at a world position.</summary>
    SPATIAL_STATIC3D,

    /// <summary>Sound loops and follows a target.</summary>
    SPATIAL_FOLLOW3D,
}

public enum AudioReturnMode
{
    /// <summary>Returned when playback stops. Used for most oneshot events.</summary>
    RETURN_ONSTOPPED,

    /// <summary>Returned manually by code. Used for loops that need explicit stopping.</summary>
    RETURN_MANUAL,
}

[Serializable]
public struct AudioData
{
    [field: SerializeField]
    public string EventId { get; set; }

    [field: SerializeField]
    public string EventPath { get; set; }

    [field: SerializeField]
    public EventReference EventReference { get; set; }

    [field: SerializeField]
    public AudioPlaybackType PlaybackType { get; set; }

    [field: SerializeField]
    public AudioSpatialMode SpatialMode { get; set; }

    [field: SerializeField]
    public AudioReturnMode ReturnMode { get; set; }
}
