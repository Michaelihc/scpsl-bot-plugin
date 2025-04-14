using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class Area
    {
        public List<Vertex> Vertices { get; } = new();
        public List<Area> ConnectedAreas { get; } = new();

        public IEnumerable<Edge> Edges => Vertices.Zip(Vertices.Skip(1), (v1, v2) => new Edge(v1, v2))
            .Append(new Edge(Vertices.Last(), Vertices.First()));

        public Vector3 CenterPosition => Vertices.Select(v => v.Position)
            .Aggregate(Vector3.zero, (a, u) => a + u) / Vertices.Count;

        public Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public Area(IEnumerable<Vertex> vertices)
        {
            Vertices.AddRange(vertices);
        }

        public bool ContainsEdge(Edge edge)
        {
            return Edges.Contains(edge);
        }

        public void AddVertex(Vertex vertex)
        {
            Vertices.Add(vertex);
        }

        public void AddVertex(Vertex vertex, Vertex beforeVertex)
        {
            var atIdx = Vertices.IndexOf(beforeVertex);
            Vertices.Insert(atIdx, vertex);
        }

        public void RemoveVertex(Vertex vertex)
        {
            Vertices.Remove(vertex);
        }

        public void AddConnection(Area connectingArea)
        {
            ConnectedAreas.Add(connectingArea);
            ConnectedAreaEdges.Add(connectingArea, connectingArea.Edges.First(te => Edges.Contains(new Edge(te.To, te.From))));
        }

        public void RemoveConnection(Area connectedArea)
        {
            ConnectedAreas.Remove(connectedArea);
            ConnectedAreaEdges.Remove(connectedArea);
        }
    }
}
