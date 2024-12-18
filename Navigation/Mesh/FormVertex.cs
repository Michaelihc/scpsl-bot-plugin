using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class FormVertex
    {
        public string Form { get; }
        public Vector3 LocalPosition { get; set; }

        public FormVertex(Vector3 position, string form)
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
