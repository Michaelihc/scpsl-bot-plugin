using SCPSLBot.Collections.Generic;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal partial record struct TransformArea(Area Local, Transform Transform)
    {
        public readonly SelectingEnumerable<IEnumerable<Area>, TransformArea, Area> ConnectedAreas
            => new(Local.ConnectedAreas, ToTransformArea);

        public readonly SelectingDictionary<TransformArea, TransformEdge, Area, Edge> ConnectedAreaEdges
            => new(Local.ConnectedAreaEdges, ToLocalArea, ToTransformArea, ToTransformEdge);

        public readonly Vector3 CenterPosition => Transform.TransformPoint(Local.CenterPosition);

        public TransformArea((Area Area, Transform Tranform) tuple)
            : this(tuple.Area, tuple.Tranform)
        {
        }

        private readonly TransformArea ToTransformArea(Area localArea) => new(localArea, Transform);
        private readonly TransformEdge ToTransformEdge(Edge localEdge) => new(localEdge, Transform);
        private readonly Area ToLocalArea(TransformArea transformArea) => transformArea.Local;
    }
}
