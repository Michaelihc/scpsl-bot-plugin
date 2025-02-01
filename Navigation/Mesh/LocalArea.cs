using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class LocalArea
    {
        public string Form { get; private set; }
        public List<LocalVertex> Vertices { get; } = new();

        public Vector3 LocalCenterPosition => Vertices.Select(v => v.LocalPosition)
            .Aggregate(Vector3.zero, (a, u) => a + u) / Vertices.Count;

        public IEnumerable<LocalEdge> Edges => Vertices.Zip(Vertices.Skip(1), (v1, v2) => new LocalEdge(v1, v2))
            .Append(new LocalEdge(Vertices.Last(), Vertices.First()));

        public List<LocalArea> ConnectedLocalAreas { get; } = new();

        public event Action<LocalVertex> VertexAdded;
        public event Action<LocalVertex> VertexRemoved;

        public event Action<LocalArea> ConnectionAdded;
        public event Action<LocalArea> ConnectionRemoved;

        public LocalArea(string form)
        {
            Form = form;
        }

        public LocalArea(IEnumerable<LocalVertex> vertices, string form)
        {
            Form = form;
            Vertices.AddRange(vertices);
        }

        public void AddVertex(LocalVertex vertex)
        {
            Vertices.Add(vertex);
            VertexAdded?.Invoke(vertex);
        }

        public void AddVertex(LocalVertex vertex, LocalVertex beforeVertex)
        {
            var atIdx = Vertices.IndexOf(beforeVertex);
            Vertices.Insert(atIdx, vertex);

            VertexAdded?.Invoke(vertex);
        }

        public void RemoveVertex(LocalVertex vertex)
        {
            if (Vertices.Remove(vertex))
            {
                VertexRemoved?.Invoke(vertex);
            }
        }

        public void AddConnection(LocalArea connectingArea)
        {
            ConnectedLocalAreas.Add(connectingArea);
            ConnectionAdded?.Invoke(connectingArea);
        }

        public void RemoveConnection(LocalArea connectedArea)
        {
            if (ConnectedLocalAreas.Remove(connectedArea))
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
