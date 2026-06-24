using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Core.BehaviourTree
{
    // https://medium.com/geekculture/how-to-create-a-simple-behaviour-tree-in-unity-c-3964c84c060e
    // https://github.com/MinaPecheux/UnityTutorials-BehaviourTrees
    public enum NodeState
    {
        RUNNING, SUCCESS, FAILURE
    }
    //[CreateAssetMenu(fileName = "BehaviourNode", menuName = "BehaviourNode/Base")]

    //[System.Serializable]
    public class Node
    {
        /// <summary>
        /// Ensure this is filled in 
        /// </summary>
        public NodeState state;
        public Node parent;
        public List<Node> children = new List<Node>();
        public string Name;


        //private Dictionary<string, object> _dataContext = new Dictionary<string, object>();

        public Node(string name = null)
        {
            parent = null;
            Name = name ?? GetType().Name;
        }

        public Node(List<Node> children, string name = null)
        {
            foreach (Node child in children)
                _Attach(child);
            Name = name ?? GetType().Name;
        }
        // This calls ealier constructor but supports easier syntax
        public Node(params Node[] list) : this(list.ToList()) { }
        public virtual void Init() { }
        private void _Attach(Node node)
        {
            node.parent = this;
            children.Add(node);
        }

        public virtual NodeState Evaluate() => NodeState.FAILURE;


        //public IEnumerable<NodeDef> RecurseChildren(NodeDef parent)
        //{
        //    var childrenDefs = children.Select(c =>
        //        {
        //            return new NodeDef
        //            {
        //                depth = parent.depth + 1,
        //                node = c,
        //            };
        //        }).ToDebuggableList();

        //    var result = childrenDefs.
        //        SelectMany(childDef => childDef.node.RecurseChildren(childDef))
        //        .Concat(childrenDefs);

        //    return result;
        //}

        public void RecurseChildren(System.Action<Node> runFunc)
        {
            runFunc(this);
            foreach (var child in children)
            {
                child.RecurseChildren(runFunc);
            }
        }

        public void GetRecurseChildren(System.Action<Node, Node> runFunc, Node parent)
        {
            runFunc(this, parent);
            foreach (var child in children)
            {
                child.GetRecurseChildren(runFunc, this);
            }
        }

    }

    [System.Serializable]
    public struct NodeDef
    {
        public Node node;
        public int depth;
    }

}