using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class Cell
    {
        public List<Vertex> Vertices { get; } = new();
        public List<Cell> ConnectedCells { get; } = new();

        public IEnumerable<Edge> Edges => Vertices.Zip(Vertices.Skip(1), (v1, v2) => new Edge(v1, v2))
            .Append(new Edge(Vertices.Last(), Vertices.First()));

        public Vector3 CenterPosition => Vertices.Select(v => v.Position)
            .Aggregate(Vector3.zero, (a, u) => a + u) / Vertices.Count;

        public Dictionary<Cell, Edge> ConnectedCellEdges { get; } = new();

        public Cell(IEnumerable<Vertex> vertices)
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

        public void AddConnection(Cell connectingCell)
        {
            var connectingEdge = connectingCell.Edges.First(te => Edges.Contains(new Edge(te.To, te.From)));

            ConnectedCells.Add(connectingCell);
            ConnectedCellEdges.Add(connectingCell, connectingEdge);
        }

        public void RemoveConnection(Cell connectedCell)
        {
            ConnectedCells.Remove(connectedCell);
            ConnectedCellEdges.Remove(connectedCell);
        }
    }
}
