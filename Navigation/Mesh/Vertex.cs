using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class Vertex
    {
        public LocalVertex LocalVertex { get; }
        public Transform Transform { get; }

        public Vector3 Position => Transform.TransformPoint(LocalVertex.LocalPosition);

        public Vector3 LocalPosition => LocalVertex.LocalPosition;

        public Vertex(LocalVertex formVertex, Transform transform)
        {
            LocalVertex = formVertex;
            Transform = transform;
        }
    }
}
