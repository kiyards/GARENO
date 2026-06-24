using System;
using UnityEngine;

/// <summary>
/// Use this attribute to make computed properties visible in the inspector
/// Ensure that <see cref="CustomPropClassAttribute"/> is declared somewhere in the class
///<code>
/// [CustomPropAttribute]
/// public int numElements => myList.Count;
/// </code>
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
public class CustomPropAttribute : Attribute
{
    public readonly string PropName;

    /// <param name="propName">
    /// Sets the Label of the property in the inspector
    /// If not provided, it uses the name of the actual prop in code</param>
    public CustomPropAttribute(string propName = null)
    {
        PropName = propName;
    }
}

public class CustomPropClassAttribute : PropertyAttribute
{
    public readonly Type ClassType;

    public CustomPropClassAttribute(Type classType)
    {
        ClassType = classType;
    }
}
