using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal abstract class Area
    {
        public RoomFormArea FormArea { get; }

        public abstract Vector3 CenterPosition { get; }
        public abstract Dictionary<Area, Edge> ConnectedAreaEdges { get; }
        public abstract IEnumerable<Area> ConnectedAreas { get; }
        public abstract Dictionary<RoomFormArea, Area> ConnectedAreasOfForm { get; }

        public Area(RoomFormArea formArea, Func<RoomFormArea, Area> areaGetter)
        {
            FormArea = formArea;

            FormArea.ConnectionAdded += (RoomFormArea otherFormArea) => AddConnection(otherFormArea, areaGetter.Invoke(otherFormArea));
            FormArea.ConnectionRemoved += RemoveConnection;

            foreach (var connectedFormArea in FormArea.ConnectedFormAreas)
            {
                AddConnection(connectedFormArea, areaGetter.Invoke(connectedFormArea));
            }
        }

        public abstract bool ContainsEdge(Edge edge);

        private void AddConnection(RoomFormArea otherFormArea, Area otherArea)
        {
            ConnectedAreasOfForm.Add(otherFormArea, otherArea);
        }

        private void RemoveConnection(RoomFormArea otherFormArea)
        {
            ConnectedAreasOfForm.Remove(otherFormArea);
        }
    }
}
