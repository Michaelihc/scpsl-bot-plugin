using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal abstract class Area
    {
        public FormArea FormArea { get; }

        public abstract Vector3 CenterPosition { get; }
        public abstract Dictionary<Area, Edge> ConnectedAreaEdges { get; }
        public abstract IEnumerable<Area> ConnectedAreas { get; }
        public abstract Dictionary<FormArea, Area> ConnectedAreasOfForm { get; }

        public Area(FormArea formArea, Func<FormArea, Area> areaGetter)
        {
            FormArea = formArea;

            FormArea.ConnectionAdded += (FormArea otherFormArea) => AddConnectionOfForm(otherFormArea, areaGetter.Invoke(otherFormArea));
            FormArea.ConnectionRemoved += RemoveConnectionOfForm;

            foreach (var connectedFormArea in FormArea.ConnectedFormAreas)
            {
                AddConnectionOfForm(connectedFormArea, areaGetter.Invoke(connectedFormArea));
            }
        }

        public abstract bool ContainsEdge(Edge edge);

        public abstract void AddConnection(Area otherArea);
        public abstract void RemoveConnection(Area otherArea);

        private void AddConnectionOfForm(FormArea otherFormArea, Area otherArea)
        {
            ConnectedAreasOfForm.Add(otherFormArea, otherArea);
        }

        private void RemoveConnectionOfForm(FormArea otherFormArea)
        {
            ConnectedAreasOfForm.Remove(otherFormArea);
        }
    }
}
