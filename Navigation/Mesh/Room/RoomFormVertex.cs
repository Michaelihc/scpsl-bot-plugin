using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomFormVertex
    {
        public string RoomForm { get; }
        public Vector3 LocalPosition { get; set; }

        public RoomFormVertex(Vector3 position, string roomForm)
        {
            LocalPosition = position;
            RoomForm = roomForm;
        }

        public override string ToString()
        {
            var idx = NavigationMesh.Instance.VerticesByRoomForm[RoomForm].IndexOf(this);
            return $"#{idx} {RoomForm}";
        }
    }
}
