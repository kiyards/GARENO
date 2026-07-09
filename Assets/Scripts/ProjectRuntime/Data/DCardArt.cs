using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DCardArt", menuName = "Data/DCardArt", order = 4)]
public class DCardArt : ScriptableObject
{
    private static DCardArt s_loadedData;
    private static Dictionary<string, Sprite> s_cachedSpriteByCardId;

    [field: SerializeField]
    public List<CardArtEntry> Data { get; private set; }

    public static Sprite GetSpriteByCardId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        EnsureLoaded();
        return s_cachedSpriteByCardId != null &&
               s_cachedSpriteByCardId.TryGetValue(id, out var sprite)
            ? sprite
            : null;
    }

    private static void EnsureLoaded()
    {
        if (s_loadedData == null)
        {
            s_loadedData = Resources.Load<DCardArt>("data/DCardArt");
        }

        if (s_cachedSpriteByCardId == null)
        {
            RebuildCache();
        }
    }

    private static void RebuildCache()
    {
        s_cachedSpriteByCardId = new();

        if (s_loadedData == null)
        {
            Debug.LogError("Failed to load DCardArt data.");
            return;
        }

        if (s_loadedData.Data == null)
        {
            return;
        }

        foreach (var entry in s_loadedData.Data)
        {
            if (string.IsNullOrEmpty(entry.CardId))
            {
                continue;
            }

            if (s_cachedSpriteByCardId.ContainsKey(entry.CardId))
            {
                Debug.LogError($"Duplicate DCardArt id {entry.CardId}");
                continue;
            }

            s_cachedSpriteByCardId.Add(entry.CardId, entry.Sprite);
        }
    }
}

[Serializable]
public struct CardArtEntry
{
    [field: SerializeField]
    public string CardId { get; set; }

    [field: SerializeField]
    public Sprite Sprite { get; set; }
}
