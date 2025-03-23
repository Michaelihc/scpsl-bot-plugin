using SCPSLBot.Collections.Generic;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal partial record struct TransformArea(Area Local, Transform Transform)
    {
        public SelectingEnumerable<IEnumerable<Area>, TransformArea, Area> ConnectedAreas { get; } 
            = new(Local.ConnectedAreas, ca => new TransformArea(ca, Transform));

        public SelectingDictionary<TransformArea, TransformEdge, Area, Edge> ConnectedAreaEdges { get; }
            = new(Local.ConnectedAreaEdges, 
                ta => ta.Local, 
                a => new (a, Transform), 
                e => new (e, Transform)
            );

        public TransformArea((Area Area, Transform Tranform) tuple)
            : this(tuple.Area, tuple.Tranform)
        {
        }

        public readonly Vector3 CenterPosition => Transform.TransformPoint(Local.CenterPosition);
    }
}
