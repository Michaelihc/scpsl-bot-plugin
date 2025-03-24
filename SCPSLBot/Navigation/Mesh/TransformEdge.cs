using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal record struct TransformEdge(TransformVertex From, TransformVertex To, Transform Transform)
    {
        public TransformEdge((Vertex From, Vertex To, Transform Tranform) tuple)
            : this(new(tuple.From, tuple.Tranform), new(tuple.To, tuple.Tranform), tuple.Tranform)
        { }

        public TransformEdge(Edge Edge, Transform Tranform)
            : this(new(Edge.From, Tranform), new(Edge.To, Tranform), Tranform)
        { }
    }
}
