using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

//https://docs.unity3d.com/Manual/TreeViewAPI.html

namespace Core.BehaviourTree
{
    /// <summary>
    /// ViewModel of BehaviourTree
    /// </summary>
    [CustomEditor(typeof(BehaviourTree), true)]
    public class BehaviorTreeEditor : Editor
    {



        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space();
            var tree = (BehaviourTree)target;
            if (tree.Root == null)
            {
                EditorGUILayout.LabelField($"Current Behaviour: nothing lol", EditorStyles.boldLabel);
                return;
            }
            StringBuilder sum = new();
            tree.Root.AddNodeChildren(null, sum);

            // Draw your custom TreeView interface here using GUILayout and EditorGUI classes
            EditorGUILayout.LabelField("Current Behaviour:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(sum.ToString(), EditorStyles.wordWrappedLabel);
        }
    }

    public static class NodeExtensions
    {
        public static void AddNodeChildren(this Node self, Node parent, StringBuilder sum)
        {
            if (parent == null)
            {
                sum.Append(self.ToString());
            }
            else
            {
                if (self.state == NodeState.FAILURE)
                    return;
                sum.Append(" > " + self.ToString());
            }
            foreach (var child in self.children)
            {
                child.AddNodeChildren(self, sum);
                if (self is Selector && (child.state == NodeState.RUNNING || child.state == NodeState.SUCCESS))
                    break;
            }
        }
    }
}
