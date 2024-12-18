using Interactables.Interobjects;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Connector
{
    internal class ConnectorArea : Area
    {
        public FormArea FormArea { get; }
        public RoomConnector Connector { get; }

        public override Vector3 CenterPosition => Connector.transform.TransformPoint(FormArea.LocalCenterPosition);

        public Dictionary<FormArea, Area> ConnectedAreasOfForm { get; } = new();
        public List<Area> ForeignConnectedAreas { get; } = new();

        public override IEnumerable<Area> ConnectedAreas => FormArea.ConnectedFormAreas.Select(f => ConnectedAreasOfForm[f]).Concat(ForeignConnectedAreas);
        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public ConnectorArea(FormArea connectorFormArea, RoomConnector room)
        {
            Connector = room;
            FormArea = connectorFormArea;
        }

        public override bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From as ConnectorVertex, edge.To as ConnectorVertex);
            return FormArea.Edges.Contains(new FormEdge(from!.ConnectorFormVertex, to!.ConnectorFormVertex));
        }

        public override string ToString()
        {
            return FormArea.Form;
        }
    }
}
