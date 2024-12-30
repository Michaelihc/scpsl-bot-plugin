using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class RoomFormArea
    {
        public string Form { get; private set; }
        public List<RoomFormVertex> Vertices { get; } = new();

        public Vector3 LocalCenterPosition => Vertices.Select(v => v.LocalPosition)
            .Aggregate(Vector3.zero, (a, u) => a + u) / Vertices.Count;

        public IEnumerable<RoomFormEdge> Edges => Vertices.Zip(Vertices.Skip(1), (v1, v2) => new RoomFormEdge(v1, v2))
            .Append(new RoomFormEdge(Vertices.Last(), Vertices.First()));

        public List<RoomFormArea> ConnectedFormAreas { get; } = new();
        public Dictionary<Vector3Int, Dictionary<string, List<RoomFormArea>>> ConnectedFormAreasByConnectorsByDirection { get; } = new();

        public event Action<RoomFormVertex> VertexAdded;
        public event Action<RoomFormVertex> VertexRemoved;

        public event Action<RoomFormArea> ConnectionAdded;
        public event Action<RoomFormArea> ConnectionRemoved;

        public RoomFormArea(string form)
        {
            Form = form;
        }

        public RoomFormArea(IEnumerable<RoomFormVertex> vertices, string form)
        {
            Form = form;
            Vertices.AddRange(vertices);
        }

        public void AddVertex(RoomFormVertex vertex)
        {
            Vertices.Add(vertex);
            VertexAdded?.Invoke(vertex);
        }

        public void AddVertex(RoomFormVertex vertex, RoomFormVertex beforeVertex)
        {
            var atIdx = Vertices.IndexOf(beforeVertex);
            Vertices.Insert(atIdx, vertex);

            VertexAdded?.Invoke(vertex);
        }

        public void RemoveVertex(RoomFormVertex vertex)
        {
            if (Vertices.Remove(vertex))
            {
                VertexRemoved?.Invoke(vertex);
            }
        }

        public void AddConnection(RoomFormArea connectingArea)
        {
            ConnectedFormAreas.Add(connectingArea);
            ConnectionAdded?.Invoke(connectingArea);
        }

        public void AddConnection(RoomFormArea connectingArea, Vector3Int connectingDirection, string connectingConnectorForm)
        {
            ConnectedFormAreasByConnectorsByDirection[connectingDirection][connectingConnectorForm].Add(connectingArea);
            ConnectionAdded?.Invoke(connectingArea);
        }

        public void RemoveConnection(RoomFormArea connectedArea)
        {
            if (ConnectedFormAreas.Remove(connectedArea))
            {
                ConnectionRemoved?.Invoke(connectedArea);
            }
        }

        public void RemoveConnection(RoomFormArea connectedArea, Vector3Int connectedDirection, string connectedConnectorForm)
        {
            if (ConnectedFormAreasByConnectorsByDirection[connectedDirection][connectedConnectorForm].Remove(connectedArea))
            {
                ConnectionRemoved?.Invoke(connectedArea);
            }
        }

        public override string ToString()
        {
            return Form;
        }
    }
}
