using System.Collections.Generic;
using UnityEngine;

namespace OSPREY.Planning
{
    [System.Serializable]
    public class Node
    {
        public Vector3 position;
        public Node parent;
        public float cost;

        // v2.0: children, so rewiring can push a cost change down the tree
        [System.NonSerialized] public List<Node> children = new List<Node>();

        public Node(Vector3 pos)
        {
            position = pos;
            cost = 0f;
            parent = null;
        }

        public Node(Vector3 pos, Node parentNode)
        {
            position = pos;
            parent = parentNode;

            if (parentNode == null)
                cost = 0f;
            else
            {
                cost = parentNode.cost + Vector3.Distance(parentNode.position, pos);
                parentNode.children.Add(this);
            }
        }

        public void SetParent(Node newParent)
        {
            if (parent != null) parent.children.Remove(this);
            parent = newParent;
            newParent.children.Add(this);
            float newCost = newParent.cost + Vector3.Distance(newParent.position, position);
            PropagateCost(newCost - cost);
        }

        void PropagateCost(float delta)
        {
            cost += delta;
            foreach (var c in children) c.PropagateCost(delta);
        }
    }
}
