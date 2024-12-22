using System;
using System.Collections.Generic;
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

        public Area(Func<FormArea, Area> areaGetter)
        {
            FormArea.ConnectionAdded += (FormArea otherFormArea) => AddConnection(otherFormArea, areaGetter.Invoke(otherFormArea));
            FormArea.ConnectionRemoved += RemoveConnection;

            foreach (var connectedFormArea in FormArea.ConnectedFormAreas)
            {
                AddConnection(connectedFormArea, areaGetter.Invoke(connectedFormArea));
            }
        }

        public abstract bool ContainsEdge(Edge edge);

        private void AddConnection(FormArea otherFormArea, Area otherArea)
        {
            ConnectedAreasOfForm.Add(otherFormArea, otherArea);
        }

        private void RemoveConnection(FormArea otherFormArea)
        {
            ConnectedAreasOfForm.Remove(otherFormArea);
        }
    }
}
