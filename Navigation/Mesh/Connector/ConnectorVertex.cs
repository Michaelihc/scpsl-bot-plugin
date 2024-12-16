using Interactables.Interobjects;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Connector
{
    internal class ConnectorVertex : Vertex
    {
        public ConnectorFormVertex ConnectorFormVertex { get; }
        public RoomConnector Connector { get; }

        public override Vector3 Position => Connector.transform.TransformPoint(ConnectorFormVertex.LocalPosition);

        public ConnectorVertex(ConnectorFormVertex connectorFormVertex, RoomConnector connector)
        {
            ConnectorFormVertex = connectorFormVertex;
            Connector = connector;
        }

        public override string ToString()
        {
            return ConnectorFormVertex.ConnectorForm;
        }
    }
}
