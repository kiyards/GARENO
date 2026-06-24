using System.Collections;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor.IMGUI.Controls;
#endif
using UnityEngine;

namespace Core.BehaviourTree
{
    public abstract class BehaviourTree : MonoBehaviour
    {
        [HideInInspector]
        public Node Root = null;


        // Use this to serialize and store relevant datas 
        //public Dictionary<string, NodeData> node2data;

        //Saving the treestate
#if UNITY_EDITOR
        [HideInInspector]
        public TreeViewState treeState;
#endif

        protected virtual void Start()
        {
            Root = SetupTree();
            Root.RecurseChildren((currNode) => { currNode.Init(); });
        }

        protected virtual void FixedUpdate()
        {
            if (Root != null)
                Root.Evaluate();
        }

        public abstract Node SetupTree();
    }
}