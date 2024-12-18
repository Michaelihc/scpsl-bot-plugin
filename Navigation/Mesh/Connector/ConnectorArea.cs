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

        public Dictionary<ConnectorFormArea, Area> ConnectedAreasOfForm { get; } = new();
        public List<Area> ForeignConnectedAreas { get; } = new();

        public override IEnumerable<Area> ConnectedAreas => ConnectorFormArea.ConnectedConnectorFormAreas.Select(f => ConnectedAreasOfForm[f]).Concat(ForeignConnectedAreas);
        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public ConnectorArea(ConnectorFormArea roomFormArea, RoomConnector room)
        {
            Connector = room;
            ConnectorFormArea = roomFormArea;
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
