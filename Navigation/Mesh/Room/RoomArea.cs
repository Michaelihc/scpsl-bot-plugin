using PluginAPI.Core.Zones;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomArea : Area
    {
        public RoomFormArea RoomFormArea { get; }
        public FacilityRoom Room { get; }

        public override Vector3 CenterPosition => Room.Transform.TransformPoint(RoomFormArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => RoomFormArea.LocalCenterPosition;

        public override Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public IEnumerable<RoomArea> ConnectedRoomAreas => RoomFormArea.ConnectedRoomFormAreas.Select(k => k.AreasOfRoom[Room]).Concat(ForeignConnectedAreas);
        public override IEnumerable<Area> ConnectedAreas => ConnectedRoomAreas;

        public List<RoomArea> ForeignConnectedAreas { get; } = new();

        public RoomArea(RoomFormArea roomFormArea, FacilityRoom room)
        {
            Room = room;
            RoomFormArea = roomFormArea;

            RoomFormArea.AreasOfRoom.Add(Room, this);
        }

        ~RoomArea()
        {
            RoomFormArea.AreasOfRoom.Remove(Room);
        }

        public override bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From as RoomVertex, edge.To as RoomVertex);
            return RoomFormArea.Edges.Contains(new RoomFormEdge(from!.RoomFormVertex, to!.RoomFormVertex));
        }

        public override string ToString()
        {
            return $"#{NavigationMesh.Instance.AreasByRoom[Room].IndexOf(this)} {RoomFormArea.RoomForm}";
        }

    }
}
