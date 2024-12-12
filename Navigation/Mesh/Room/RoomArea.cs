using PluginAPI.Core.Zones;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomArea : Area
    {
        public RoomKindArea RoomKindArea { get; }
        public FacilityRoom Room { get; }

        public override Vector3 CenterPosition => Room.Transform.TransformPoint(RoomKindArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => RoomKindArea.LocalCenterPosition;

        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public IEnumerable<RoomArea> ConnectedRoomAreas => RoomKindArea.ConnectedRoomKindAreas.Select(k => k.AreasOfRoom[Room]).Concat(ForeignConnectedAreas);
        public override IEnumerable<Area> ConnectedAreas => ConnectedRoomAreas;

        public List<RoomArea> ForeignConnectedAreas { get; } = new();

        public RoomArea(RoomKindArea roomKindArea, FacilityRoom room)
        {
            Room = room;
            RoomKindArea = roomKindArea;

            RoomKindArea.AreasOfRoom.Add(Room, this);
        }

        ~RoomArea()
        {
            RoomKindArea.AreasOfRoom.Remove(Room);
        }

        public override bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From as RoomVertex, edge.To as RoomVertex);
            return RoomKindArea.Edges.Contains(new RoomKindEdge(from!.RoomKindVertex, to!.RoomKindVertex));
        }

        public override string ToString()
        {
            return $"#{NavigationMesh.Instance.AreasByRoom[Room].IndexOf(this)} {RoomKindArea.RoomKind}";
        }

    }
}
