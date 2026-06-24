using UnityEngine;

/// <summary>
/// Use this to generate a random GUID for a ScriptableObject (Has to be used on string)
/// <code>
/// [ScriptableObjectId]
/// public string ID;
/// </code>
/// </summary>
public class ScriptableObjectIdAttribute : PropertyAttribute { }
