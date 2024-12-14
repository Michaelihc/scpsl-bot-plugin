using PluginAPI.Core.Zones;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Room
{
    internal class RoomFormArea
    {
        public string RoomForm { get; set; }
        public List<RoomFormVertex> Vertices { get; } = new();

        public Vector3 LocalCenterPosition => Vertices.Select(v => v.LocalPosition)
            .Aggregate(Vector3.zero, (a, u) => a + u) / Vertices.Count;

        public IEnumerable<RoomFormEdge> Edges => Vertices.Zip(Vertices.Skip(1), (v1, v2) => new RoomFormEdge(v1, v2))
            .Append(new RoomFormEdge(Vertices.Last(), Vertices.First()));

        public List<RoomFormArea> ConnectedRoomFormAreas { get; } = new();
        public List<RoomFormEdge> ConnectedRoomFormAreaEdges = new();

        public Dictionary<FacilityRoom, RoomArea> AreasOfRoom { get; } = new();

        public RoomFormArea(string roomForm)
        { 
            RoomForm = roomForm;
        }

        public RoomFormArea(IEnumerable<RoomFormVertex> vertices, string roomForm)
        {
            RoomForm = roomForm;
            Vertices.AddRange(vertices);
        }

        public override string ToString()
        {
            return $"#{NavigationMesh.Instance.AreasByRoomForm[RoomForm].IndexOf(this)} {RoomForm}";
        }
    }
}
