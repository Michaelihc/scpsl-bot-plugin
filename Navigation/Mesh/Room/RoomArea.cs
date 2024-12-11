using PluginAPI.Core.Zones;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomArea
    {
        public RoomKindArea RoomKindArea { get; }
        public FacilityRoom Room { get; }

        public Vector3 CenterPosition => Room.Transform.TransformPoint(RoomKindArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => RoomKindArea.LocalCenterPosition;

        //public IEnumerable<(RoomVertex From, RoomVertex To)> Edges => RoomKindArea.Edges.Select(e => (e.From.))

        public IEnumerable<RoomArea> ConnectedAreas => RoomKindArea.ConnectedRoomKindAreas.Select(k => k.AreasOfRoom[Room]).Concat(ForeignConnectedAreas);
        public Dictionary<RoomArea, (RoomVertex From, RoomVertex To)> ConnectedAreaEdges { get; } = new();

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

        public override string ToString()
        {
            return $"#{NavigationMesh.Instance.AreasByRoom[Room].IndexOf(this)} {RoomKindArea.RoomKind}";
        }
    }
}
