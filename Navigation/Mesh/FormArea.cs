using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class FormArea
    {
        public string Form { get; private set; }
        public List<FormVertex> Vertices { get; } = new();

        public Vector3 LocalCenterPosition => Vertices.Select(v => v.LocalPosition)
            .Aggregate(Vector3.zero, (a, u) => a + u) / Vertices.Count;

        public IEnumerable<FormEdge> Edges => Vertices.Zip(Vertices.Skip(1), (v1, v2) => new FormEdge(v1, v2))
            .Append(new FormEdge(Vertices.Last(), Vertices.First()));

        public List<FormArea> ConnectedFormAreas { get; } = new();

        public event Action<FormVertex> VertexAdded;
        public event Action<FormVertex> VertexRemoved;

        public event Action<FormArea> ConnectionAdded;
        public event Action<FormArea> ConnectionRemoved;

        public FormArea(string form)
        {
            Form = form;
        }

        public FormArea(IEnumerable<FormVertex> vertices, string form)
        {
            Form = form;
            Vertices.AddRange(vertices);
        }

        public void AddVertex(FormVertex vertex)
        {
            Vertices.Add(vertex);
            VertexAdded?.Invoke(vertex);
        }

        public void AddVertex(FormVertex vertex, FormVertex beforeVertex)
        {
            var atIdx = Vertices.IndexOf(beforeVertex);
            Vertices.Insert(atIdx, vertex);

            VertexAdded?.Invoke(vertex);
        }

        public void RemoveVertex(FormVertex vertex)
        {
            if (Vertices.Remove(vertex))
            {
                VertexRemoved?.Invoke(vertex);
            }
        }

        public void AddConnection(FormArea connectingArea)
        {
            ConnectedFormAreas.Add(connectingArea);
            ConnectionAdded?.Invoke(connectingArea);
        }

        public void RemoveConnection(FormArea connectedArea)
        {
            if (ConnectedFormAreas.Remove(connectedArea))
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
