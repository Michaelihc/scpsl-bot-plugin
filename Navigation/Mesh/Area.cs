using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class Area
    {
        public LocalArea LocalArea { get; }
        public Transform Transform { get; }

        public Vector3 CenterPosition => Transform.TransformPoint(LocalArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => LocalArea.LocalCenterPosition;

        public Dictionary<LocalArea, Area> ConnectedAreasOfLocal { get; } = new();
        public List<Area> ForeignConnectedAreas { get; } = new();

        public IEnumerable<Area> ConnectedAreas { get; }
        public Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public Area(
            LocalArea localArea, Transform transform, Func<LocalArea, Area> areaGetter)
            : this(localArea, areaGetter)
        {
            Transform = transform;

            ConnectedAreas = LocalArea.ConnectedLocalAreas
                .Select(f => ConnectedAreasOfLocal[f])
                .Concat(ForeignConnectedAreas);
        }

        private Area(LocalArea localArea, Func<LocalArea, Area> areaGetter)
        {
            LocalArea = localArea;

            LocalArea.ConnectionAdded += (otherLocalArea) => AddConnectionOfLocal(otherLocalArea, areaGetter.Invoke(otherLocalArea));
            LocalArea.ConnectionRemoved += RemoveConnectionOfLocal;

            foreach (var connectedLocalArea in LocalArea.ConnectedLocalAreas)
            {
                AddConnectionOfLocal(connectedLocalArea, areaGetter.Invoke(connectedLocalArea));
            }
        }

        private void AddConnectionOfLocal(LocalArea otherLocalArea, Area otherArea)
        {
            ConnectedAreasOfLocal.Add(otherLocalArea, otherArea);
        }

        private void RemoveConnectionOfLocal(LocalArea otherLocalArea)
        {
            ConnectedAreasOfLocal.Remove(otherLocalArea);
        }

        public bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From, edge.To);
            return LocalArea.Edges.Contains(new LocalEdge(from.LocalVertex, to.LocalVertex));
        }

        public void AddConnection(Area otherArea)
        {
            ForeignConnectedAreas.Add(otherArea);
        }

        public void RemoveConnection(Area otherArea)
        {
            ForeignConnectedAreas.Remove(otherArea);
        }
    }
}
