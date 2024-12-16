using Interactables.Interobjects;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Connector
{
    internal class ConnectorArea : Area
    {
        public ConnectorFormArea ConnectorFormArea { get; }
        public RoomConnector Connector { get; }

        public override Vector3 CenterPosition => Connector.transform.TransformPoint(ConnectorFormArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => ConnectorFormArea.LocalCenterPosition;

        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public IEnumerable<ConnectorArea> ConnectedConnectorAreas => ConnectorFormArea.ConnectedConnectorFormAreas.Select(k => k.AreasOfConnector[Connector]).Concat(ForeignConnectedAreas);
        public override IEnumerable<Area> ConnectedAreas => ConnectedConnectorAreas;

        public List<ConnectorArea> ForeignConnectedAreas { get; } = new();

        public ConnectorArea(ConnectorFormArea roomFormArea, RoomConnector room)
        {
            Connector = room;
            ConnectorFormArea = roomFormArea;

            ConnectorFormArea.AreasOfConnector.Add(Connector, this);
        }

        ~ConnectorArea()
        {
            ConnectorFormArea.AreasOfConnector.Remove(Connector);
        }

        public override bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From as ConnectorVertex, edge.To as ConnectorVertex);
            return ConnectorFormArea.Edges.Contains(new ConnectorFormEdge(from!.ConnectorFormVertex, to!.ConnectorFormVertex));
        }

        public override string ToString()
        {
            return ConnectorFormArea.ConnectorForm;
        }

    }
}
