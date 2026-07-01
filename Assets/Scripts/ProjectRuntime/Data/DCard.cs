using Core;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

[CreateAssetMenu(fileName = "DCard", menuName = "Data/DCard", order = 3)]
public class DCard : ScriptableObject, IDataImport
{
    private static DCard s_loadedData;
    private static Dictionary<string, CardData> s_cachedDataDict;

    [field: SerializeField]
    public List<CardData> Data { get; private set; }

    public static DCard GetAllData()
    {
        EnsureLoaded();
        return s_loadedData;
    }

    public static CardData? GetDataById(string id)
    {
        EnsureLoaded();
        return s_cachedDataDict.TryGetValue(id, out var result) ? result : null;
    }

    private static void EnsureLoaded()
    {
        if (s_loadedData == null)
        {
            s_loadedData = Resources.Load<DCard>("data/DCard");
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
            Debug.LogError("Failed to load DCard data.");
            return;
        }

        if (s_loadedData.Data == null)
        {
            return;
        }

        foreach (var cardData in s_loadedData.Data)
        {
            if (s_cachedDataDict.ContainsKey(cardData.CardId))
            {
                Debug.LogError($"Duplicate Id {cardData.CardId}");
                continue;
            }

            s_cachedDataDict.Add(cardData.CardId, cardData);
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

            if (paramList.Length < 5)
            {
                Debug.LogWarning($"Skipped DCard row {i + 1}. Expected at least 5 columns but found {paramList.Length}.");
                continue;
            }

            // New item
            var cardData = new CardData
            {
                CardId = paramList[1],
                DisplayName = paramList[2],
                ManaCost = CommonUtil.ConvertToInt32(paramList[3]),
                Effect = ParseEnumOrDefault(paramList[4], CardEffectType.SPAWN_BASIC_ZOMBIE, paramList[1]),
                CardType = paramList.Length > 5
                    ? ParseEnumOrDefault(paramList[5], CardType.ZOMBIE, paramList[1])
                    : InferCardType(paramList[4]),
                CardDescription = paramList.Length > 6 ? paramList[6] : string.Empty,
            };
            s_loadedData.Data.Add(cardData);
        }

        RebuildCache();
        CommonUtil.SaveScriptableObject(s_loadedData);
    }

    private static TEnum ParseEnumOrDefault<TEnum>(string value, TEnum defaultValue, string cardId)
        where TEnum : struct
    {
        if (Enum.TryParse(value, true, out TEnum result))
        {
            return result;
        }

        Debug.LogWarning($"Failed to parse {typeof(TEnum).Name} '{value}' for card {cardId}. Using {defaultValue}.");
        return defaultValue;
    }

    private static CardType InferCardType(string effect)
    {
        return effect != null && effect.IndexOf("TRAP", StringComparison.OrdinalIgnoreCase) >= 0
            ? CardType.TRAP
            : CardType.ZOMBIE;
    }
#endif
}

/// <summary>
/// What a card does when the Dungeon Master plays it. The server switches on this in
/// DungeonMasterCardManager.ServerPlayCard to execute the card's effect. New cards add a
/// value here and a matching case in that switch.
/// </summary>
public enum CardEffectType
{
    SPAWN_BASIC_ZOMBIE,
    PLACE_BEAR_TRAP,
    DEPLOY_TURRET,
    SPAWN_CREEPER_ZOMBIE,
    SPAWN_GROUP_OF_DOGS,
    SPAWN_MIMIC_ZOMBIE,
}

public enum CardType
{
    ZOMBIE,
    TRAP,
}

[Serializable]
public struct CardData
{
    [field: SerializeField]
    public string CardId { get; set; }

    [field: SerializeField]
    public string DisplayName { get; set; }

    [field: SerializeField]
    public int ManaCost { get; set; }

    [field: SerializeField]
    public CardEffectType Effect { get; set; }

    [field: SerializeField]
    public CardType CardType { get; set; }

    [field: SerializeField]
    public string CardDescription { get; set; }
}
