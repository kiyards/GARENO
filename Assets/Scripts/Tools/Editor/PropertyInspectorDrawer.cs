using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(CustomPropClassAttribute))]
public class PropertyInspectorDrawer : DecoratorDrawer
{
    private PropertyInfo[] _cachedProps;


    public override void OnGUI(Rect position)
    {
        var classIndicator = (CustomPropClassAttribute)attribute;
        Object unityObject = Selection.activeGameObject.GetComponent(classIndicator.ClassType);

        //These list all the attributes that exist on the class itself
        //TypeInfo typeInfo = unityObject.GetType().GetTypeInfo();
        //var attrs = typeInfo.GetCustomAttributes();

        //foreach (var attr in attrs)
        //{
        //    string toShow = "Attribute on MyClass: " + attr.GetType().Name;
        //    position.height = EditorGUIUtility.singleLineHeight;
        //    EditorGUI.LabelField(position, toShow);
        //    position.y += EditorGUIUtility.singleLineHeight;
        //}

        //_cachedProps = unityObject.GetType()
        //    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        //    .Where(prop => System.Attribute.IsDefined(prop, typeof(CustomPropAttribute)))
        //    .ToArray();


        foreach (PropertyInfo property in _cachedProps)
        {
            var attr = property.GetCustomAttribute<CustomPropAttribute>();

            var propName = (attr.PropName?? property.Name) + ":";

            // Get the value of the property from the object
            object value = property.GetValue(unityObject);

            position.height = EditorGUIUtility.singleLineHeight;
            //Could use this for property field
            //https://forum.unity.com/threads/display-a-propertyfield-for-an-assetreference-without-using-serialize.644506/

            EditorGUI.LabelField(position, propName, (string)value);
            position.y += EditorGUIUtility.singleLineHeight;
        }

    }

    // This runs first
    // Is necesary for increasing the height of the property drawer if not just using default logic for how much a single prop should take
    public override float GetHeight()
    {
        var classIndicator = (CustomPropClassAttribute)attribute;

        Object unityObject = Selection.activeGameObject.GetComponent(classIndicator.ClassType);
        if (unityObject == null) return base.GetHeight();
        //// Calculate the height of the label field
        //var labelHeight = EditorStyles.label.CalcHeight(labelContent, EditorGUIUtility.currentViewWidth);
        _cachedProps = unityObject.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(prop => System.Attribute.IsDefined(prop, typeof(CustomPropAttribute)))
            .ToArray();

        return EditorGUIUtility.standardVerticalSpacing * _cachedProps.Length + 20;
    }
}