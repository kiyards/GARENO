using UnityEngine;
using UnityEditor;
using System.Reflection;

#if UNITY_EDITOR
public class MultiTextBoxEditor<T> : Editor where T : IDataImport
{
    private string _dataInput = string.Empty;

    public override void OnInspectorGUI()
    {
        // Update the serialized object
        serializedObject.Update();

        // Label
        EditorGUILayout.LabelField("Script Textbox", EditorStyles.boldLabel);

        // Text box
        _dataInput = EditorGUILayout.TextArea(_dataInput, GUILayout.Height(120));
        if (GUILayout.Button("Import Script", GUILayout.Height(20)))
        {
            typeof(T).GetMethod("ImportData", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).Invoke(null, new object[] { _dataInput });
        }

        EditorGUILayout.Space();

        // Apply changes to the serialized object
        serializedObject.ApplyModifiedProperties();

        base.OnInspectorGUI();
    }
}
#endif