using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class Area
    {
        public LocalArea FormArea { get; }
        public Transform Transform { get; }

        public Vector3 CenterPosition => Transform.transform.TransformPoint(FormArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => FormArea.LocalCenterPosition;

        public Dictionary<LocalArea, Area> ConnectedAreasOfForm { get; } = new();
        public List<Area> ForeignConnectedAreas { get; } = new();

        public IEnumerable<Area> ConnectedAreas { get; }
        public Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public Area(
            LocalArea formArea, Transform transform, Func<LocalArea, Area> areaGetter)
            : this(formArea, areaGetter)
        {
            Transform = transform;

            ConnectedAreas = FormArea.ConnectedFormAreas
                .Select(f => ConnectedAreasOfForm[f])
                .Concat(ForeignConnectedAreas);
        }

        private Area(LocalArea formArea, Func<LocalArea, Area> areaGetter)
        {
            FormArea = formArea;

            FormArea.ConnectionAdded += (LocalArea otherFormArea) => AddConnectionOfForm(otherFormArea, areaGetter.Invoke(otherFormArea));
            FormArea.ConnectionRemoved += RemoveConnectionOfForm;

            foreach (var connectedFormArea in FormArea.ConnectedFormAreas)
            {
                AddConnectionOfForm(connectedFormArea, areaGetter.Invoke(connectedFormArea));
            }
        }

        private void AddConnectionOfForm(LocalArea otherFormArea, Area otherArea)
        {
            ConnectedAreasOfForm.Add(otherFormArea, otherArea);
        }

        private void RemoveConnectionOfForm(LocalArea otherFormArea)
        {
            ConnectedAreasOfForm.Remove(otherFormArea);
        }

        public bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From, edge.To);
            return FormArea.Edges.Contains(new LocalEdge(from.LocalVertex, to.LocalVertex));
        }

        public void AddConnection(Area otherArea)
        {
            ForeignConnectedAreas.Add(otherArea);
        }

        public void RemoveConnection(Area otherArea)
        {
            ForeignConnectedAreas.Remove(otherArea);
        }

        public override string ToString()
        {
            return FormArea.Form;
        }
    }
}
