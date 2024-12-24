using MapGeneration;
using PluginAPI.Core;
using PluginAPI.Core.Zones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomVertex : Vertex
    {
        public RoomFormVertex RoomFormVertex { get; }
        public RoomIdentifier Room { get; }

        public override Vector3 Position => Room.transform.TransformPoint(RoomFormVertex.LocalPosition);

        public Vector3 LocalPosition => RoomFormVertex.LocalPosition;

        public RoomVertex(RoomFormVertex roomFormVertex, RoomIdentifier room)
        {
            RoomFormVertex = roomFormVertex;
            Room = room;
        }

        public override string ToString()
        {
            var idx = NavigationMesh.Instance.VerticesByRoomForm[RoomFormVertex.Form].IndexOf(RoomFormVertex);
            return $"#{idx} {RoomFormVertex.Form}";
        }
    }
}
