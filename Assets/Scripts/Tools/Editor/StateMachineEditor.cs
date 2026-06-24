using UnityEditor;
using UnityEngine;
using Core;

//The second arguement is if inherited classes use this editor
//NOTE: This means that the child objects cant have custom editors of their own and you have to resort to making DecoratorDrawers
[CustomEditor(typeof(StateMachine), true)]
public class StateMachineEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        StateMachine machine = (StateMachine)target;
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

        //https://gamedev.stackexchange.com/questions/149133/custom-editor-script-not-updating-when-values-are-changed-from-script
        if (EditorApplication.isPlaying)
            Repaint();
    }
}