using Interactables.Interobjects;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Connector
{
    internal class ConnectorVertex : Vertex
    {
        public FormVertex ConnectorFormVertex { get; }
        public RoomConnector Connector { get; }

        public override Vector3 Position => Connector.transform.TransformPoint(ConnectorFormVertex.LocalPosition);

        public ConnectorVertex(FormVertex connectorFormVertex, RoomConnector connector)
        {
            ConnectorFormVertex = connectorFormVertex;
            Connector = connector;
        }

        public override string ToString()
        {
            return ConnectorFormVertex.Form;
        }
    }
}
