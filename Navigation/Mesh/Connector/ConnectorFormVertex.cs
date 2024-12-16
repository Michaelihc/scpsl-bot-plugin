using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Connector
{
    public class ConnectorFormVertex
    {
        public string ConnectorForm { get; }
        public Vector3 LocalPosition { get; set; }

        public ConnectorFormVertex(Vector3 localPosition, string roomForm)
        {
            LocalPosition = localPosition;
            ConnectorForm = roomForm;
        }

        public override string ToString()
        {
            return ConnectorForm;
        }
    }
}
