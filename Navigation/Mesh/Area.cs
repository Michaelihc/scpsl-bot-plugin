using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class Area
    {
        public FormArea FormArea { get; }
        public Transform Transform { get; }

        public Vector3 CenterPosition => Transform.transform.TransformPoint(FormArea.LocalCenterPosition);
        public Vector3 LocalCenterPosition => FormArea.LocalCenterPosition;

        public Dictionary<FormArea, Area> ConnectedAreasOfForm { get; } = new();
        public List<Area> ForeignConnectedAreas { get; } = new();

        public IEnumerable<Area> ConnectedAreas { get; }
        public Dictionary<Area, Edge> ConnectedAreaEdges { get; } = new();

        public Area(
            FormArea formArea, Transform transform, Func<FormArea, Area> areaGetter)
            : this(formArea, areaGetter)
        {
            Transform = transform;

            ConnectedAreas = FormArea.ConnectedFormAreas
                .Select(f => ConnectedAreasOfForm[f])
                .Concat(ForeignConnectedAreas);
        }

        private Area(FormArea formArea, Func<FormArea, Area> areaGetter)
        {
            FormArea = formArea;

            FormArea.ConnectionAdded += (FormArea otherFormArea) => AddConnectionOfForm(otherFormArea, areaGetter.Invoke(otherFormArea));
            FormArea.ConnectionRemoved += RemoveConnectionOfForm;

            foreach (var connectedFormArea in FormArea.ConnectedFormAreas)
            {
                AddConnectionOfForm(connectedFormArea, areaGetter.Invoke(connectedFormArea));
            }
        }

        private void AddConnectionOfForm(FormArea otherFormArea, Area otherArea)
        {
            ConnectedAreasOfForm.Add(otherFormArea, otherArea);
        }

        private void RemoveConnectionOfForm(FormArea otherFormArea)
        {
            ConnectedAreasOfForm.Remove(otherFormArea);
        }

        public bool ContainsEdge(Edge edge)
        {
            var (from, to) = (edge.From, edge.To);
            return FormArea.Edges.Contains(new FormEdge(from.FormVertex, to.FormVertex));
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
