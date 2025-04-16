using SCPSLBot.Collections.Generic;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal partial record struct TransformCell(Cell Local, Transform Transform)
    {
        public readonly SelectingEnumerable<IEnumerable<Cell>, TransformCell, Cell> ConnectedCells
            => new(Local.ConnectedCells, ToTransformCell);

        public readonly SelectingDictionary<TransformCell, TransformEdge, Cell, Edge> ConnectedCellEdges
            => new(Local.ConnectedCellEdges, ToLocalCell, ToTransformCell, ToTransformEdge);

        public readonly Vector3 CenterPosition => Transform.TransformPoint(Local.CenterPosition);

        public TransformCell((Cell Cell, Transform Tranform) tuple)
            : this(tuple.Cell, tuple.Tranform)
        {
        }

        private readonly TransformCell ToTransformCell(Cell localCell) => new(localCell, Transform);
        private readonly TransformEdge ToTransformEdge(Edge localEdge) => new(localEdge, Transform);
        private readonly Cell ToLocalCell(TransformCell transformCell) => transformCell.Local;
    }
}
