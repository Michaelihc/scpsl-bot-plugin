using MapGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomArea : Area
    {
        public RoomIdentifier Room { get; }

        public override Vector3 CenterPosition => Room.transform.TransformPoint(FormArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => FormArea.LocalCenterPosition;

        public override Dictionary<RoomFormArea, Area> ConnectedAreasOfForm { get; } = new();
        public List<Area> ForeignConnectedAreas { get; } = new();

        public override IEnumerable<Area> ConnectedAreas => FormArea.ConnectedFormAreas.Select(f => ConnectedAreasOfForm[f]).Concat(ForeignConnectedAreas);
        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public IEnumerable<RoomArea> ForeignConnectedRoomAreas => ForeignConnectedAreas.OfType<RoomArea>();
        public IEnumerable<RoomArea> ConnectedRoomAreas => ConnectedAreas.OfType<RoomArea>();

        public RoomArea(RoomFormArea formArea, RoomIdentifier room, Func<RoomFormArea, Area> areaGetter) : base(formArea, areaGetter)
        {
            Room = room;
        }

        public override bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From as RoomVertex, edge.To as RoomVertex);
            return FormArea.Edges.Contains(new RoomFormEdge(from!.RoomFormVertex, to!.RoomFormVertex));
        }

        public override string ToString()
        {
            return FormArea.Form;
        }
    }
}
