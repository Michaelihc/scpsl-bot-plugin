using Interactables.Interobjects;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh.Connector
{
    internal class ConnectorFormArea
    {
        public string ConnectorForm { get; }
        public List<ConnectorFormVertex> Vertices { get; } = new();

        public List<ConnectorFormArea> ConnectedConnectorFormAreas { get; } = new();

        public IEnumerable<ConnectorFormEdge> Edges => Vertices.Zip(Vertices.Skip(1), static (v1, v2) => new ConnectorFormEdge(v1, v2))
            .Append(new ConnectorFormEdge(Vertices.Last(), Vertices.First()));

        public Vector3 LocalCenterPosition => Vertices.Select(static v => v.LocalPosition)
            .Aggregate(Vector3.zero, static (a, u) => a + u) / Vertices.Count;

        public ConnectorFormArea(string roomForm)
        { 
            ConnectorForm = roomForm;
        }

        public ConnectorFormArea(IEnumerable<ConnectorFormVertex> vertices, string roomForm)
        {
            ConnectorForm = roomForm;
            Vertices.AddRange(vertices);
        }

        public override string ToString()
        {
            return ConnectorForm;
        }
    }
}
