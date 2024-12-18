using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class FormArea
    {
        public string Form { get; set; }
        public List<FormVertex> Vertices { get; } = new();

        public Vector3 LocalCenterPosition => Vertices.Select(v => v.LocalPosition)
            .Aggregate(Vector3.zero, (a, u) => a + u) / Vertices.Count;

        public IEnumerable<FormEdge> Edges => Vertices.Zip(Vertices.Skip(1), (v1, v2) => new FormEdge(v1, v2))
            .Append(new FormEdge(Vertices.Last(), Vertices.First()));

        public List<FormArea> ConnectedFormAreas { get; } = new();
        public List<FormEdge> ConnectedFormAreaEdges = new();

        public FormArea(string form)
        {
            Form = form;
        }

        public FormArea(IEnumerable<FormVertex> vertices, string form)
        {
            Form = form;
            Vertices.AddRange(vertices);
        }

        public override string ToString()
        {
            return Form;
        }
    }
}
