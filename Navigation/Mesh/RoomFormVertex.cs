using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class RoomFormVertex
    {
        public string Form { get; }
        public Vector3 LocalPosition { get; set; }

        public RoomFormVertex(Vector3 position, string form)
        {
            LocalPosition = position;
            Form = form;
        }

        public override string ToString()
        {
            return Form;
        }
    }
}
