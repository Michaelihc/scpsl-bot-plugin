using MapGeneration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomArea : Area
    {
        public override FormArea FormArea { get; }
        public RoomIdentifier Room { get; }

        public override Vector3 CenterPosition => Room.transform.TransformPoint(FormArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => FormArea.LocalCenterPosition;

        public override Dictionary<FormArea, Area> ConnectedAreasOfForm { get; } = new();
        public List<Area> ForeignConnectedAreas { get; } = new();

        public override IEnumerable<Area> ConnectedAreas => FormArea.ConnectedFormAreas.Select(f => ConnectedAreasOfForm[f]).Concat(ForeignConnectedAreas);
        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public IEnumerable<RoomArea> ForeignConnectedRoomAreas => ForeignConnectedAreas.OfType<RoomArea>();
        public IEnumerable<RoomArea> ConnectedRoomAreas => ConnectedAreas.OfType<RoomArea>();

        public RoomArea(FormArea formArea, RoomIdentifier room)
        {
            Room = room;
            FormArea = formArea;
        }

        public override bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From as RoomVertex, edge.To as RoomVertex);
            return FormArea.Edges.Contains(new FormEdge(from!.RoomFormVertex, to!.RoomFormVertex));
        }

        public override string ToString()
        {
            return FormArea.Form;
        }
    }
}
