using Mirror;
using UnityEditor;
using UnityEngine;
using ProjectRuntime.Network;

//The second arguement is if inherited classes use this editor
//NOTE: This means that the child objects cant have custom editors of their own and you have to resort to making DecoratorDrawers
[CustomEditor(typeof(NetworkStateMachine), true)]
public class NetworkStateMachineEditor : NetworkBehaviourInspector
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        DrawSyncObjectCollections();

        // Replicate DrawDefaultSyncSettings() without the syncsAnything guard
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sync Settings", EditorStyles.boldLabel);

        SerializedProperty syncDirection = serializedObject.FindProperty("syncDirection");
        EditorGUILayout.PropertyField(syncDirection);

        if (syncDirection.enumValueIndex == (int)SyncDirection.ServerToClient)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("syncMode"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("syncInterval"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("State Info", EditorStyles.boldLabel);

        NetworkStateMachine machine = (NetworkStateMachine)target;
        if (machine.currentState == null)
        {
            EditorGUILayout.LabelField("Current State", "nothing lol");
        }
        else
        {
            EditorGUILayout.LabelField("Current State", machine.currentState.stateName);
            EditorGUILayout.FloatField(machine.currentState.age);
            EditorGUILayout.FloatField("Duration", machine.currentState.duration);
        }
        if (machine.bufferedState == null)
        {
            EditorGUILayout.LabelField("Buffered State", "nothing lol");
        }
        else
        {
            EditorGUILayout.LabelField("Buffered State", machine.bufferedState.stateName);
        }

        serializedObject.ApplyModifiedProperties();

        //https://gamedev.stackexchange.com/questions/149133/custom-editor-script-not-updating-when-values-are-changed-from-script
        if (EditorApplication.isPlaying)
            Repaint();
    }
}