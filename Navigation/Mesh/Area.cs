using System.Collections.Generic;
using SCPSLBot.Navigation.Mesh.Room;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal abstract class Area
    {
        public abstract FormArea FormArea { get; }

        public abstract Vector3 CenterPosition { get; }
        public abstract Dictionary<Area, Edge> ConnectedAreaEdges { get; }
        public abstract IEnumerable<Area> ConnectedAreas { get; }
        public abstract Dictionary<FormArea, Area> ConnectedAreasOfForm { get; }

        public abstract bool ContainsEdge(Edge edge);
    }
}
