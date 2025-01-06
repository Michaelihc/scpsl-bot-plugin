using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class Vertex
    {
        public FormVertex FormVertex { get; }
        public Transform Transform { get; }

        public Vector3 Position => Transform.TransformPoint(FormVertex.LocalPosition);

        public Vector3 LocalPosition => FormVertex.LocalPosition;

        public Vertex(FormVertex formVertex, Transform transform)
        {
            FormVertex = formVertex;
            Transform = transform;
        }

        public override string ToString()
        {
            return FormVertex.Form;
        }
    }
}
