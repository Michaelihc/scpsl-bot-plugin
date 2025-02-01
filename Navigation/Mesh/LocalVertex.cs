using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class LocalVertex
    {
        public Vector3 LocalPosition { get; set; }

        public LocalVertex(Vector3 position)
        {
            LocalPosition = position;
        }
    }
}
