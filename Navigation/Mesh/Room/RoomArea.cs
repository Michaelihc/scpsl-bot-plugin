using PluginAPI.Core.Zones;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomArea : Area
    {
        public FormArea FormArea { get; }
        public FacilityRoom Room { get; }

        public override Vector3 CenterPosition => Room.Transform.TransformPoint(FormArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => FormArea.LocalCenterPosition;

        public Dictionary<FormArea, RoomArea> ConnectedAreasOfForm { get; } = new();
        public List<RoomArea> ForeignConnectedAreas { get; } = new();
        public IEnumerable<RoomArea> ConnectedRoomAreas => FormArea.ConnectedFormAreas.Select(f => ConnectedAreasOfForm[f]).Concat(ForeignConnectedAreas);

        public override IEnumerable<Area> ConnectedAreas => ConnectedRoomAreas;
        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public RoomArea(FormArea formArea, FacilityRoom room)
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
