using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal partial class NavigationMesh
    {
        public List<Vertex> Vertices { get; } = new();
        public event Action<Vertex> VertexCreated;
        public event Action<Vertex> VertexDeleted;

        public List<Cell> Cells { get; } = new();
        public event Action<Cell> CellCreated;
        public event Action<Cell> CellDeleted;

        private NavigationMesh()
        {
            VertexDeleted += RemoveVertexFromCells;
        }

        #region Mesh manipulation

        public Vertex AddVertex(Vector3 position)
        {
            var newVertex = new Vertex(position);
            Vertices.Add(newVertex);

            VertexCreated?.Invoke(newVertex);

            return newVertex;
        }

        public bool DeleteVertex(Vertex vertex)
        {
            if (!Vertices.Remove(vertex))
            {
                return false;
            }

            VertexDeleted?.Invoke(vertex);

            return true;
        }

        private void RemoveVertexFromCells(Vertex vertex)
        {
            foreach (var cell in Cells)
            {
                cell.RemoveVertex(vertex);
            }
        }

        public bool MoveVertex(Vertex vertex, Vector3 newPosition)
        {
            vertex.Position = newPosition;

            return true;
        }

        public Cell MakeCell(IEnumerable<Vertex> vertices)
        {
            var newCell = new Cell(vertices);
            Cells.Add(newCell);

            CellCreated?.Invoke(newCell);

            return newCell;
        }

        public bool RemoveCell(Cell cell)
        {
            var cells = Cells;
            if (!cells.Remove(cell))
            {
                return false;
            }

            RemoveConnectionsToCell(cell);

            CellDeleted?.Invoke(cell);

            return true;
        }

        private void RemoveConnectionsToCell(Cell cell)
        {
            foreach (var otherCell in Cells)
            {
                otherCell.RemoveConnection(cell);
            }
        }

        public void CreateCellConnection(Cell fromCell, Cell toCell)
        {
            fromCell.AddConnection(toCell);
        }

        public void DeleteCellConnection(Cell fromCell, Cell toCell)
        {
            fromCell.RemoveConnection(toCell);
        }

        public void AddVertexToCell(Cell cell, Vertex vertex, Vertex beforeVertex)
        {
            cell.AddVertex(vertex, beforeVertex);
        }

        #endregion
        #region Mesh reading/writing

        public void ReadMesh(BinaryReader binaryReader)
        {
            ///
            /// Vertices reading
            /// 

            var vertexCount = binaryReader.ReadInt32();

            for (var j = 0; j < vertexCount; j++)
            {
                var vertexPosition = new Vector3()
                {
                    x = binaryReader.ReadSingle(),
                    y = binaryReader.ReadSingle(),
                    z = binaryReader.ReadSingle()
                };

                var newVertex = AddVertex(vertexPosition);
            }

            ///
            /// Cells reading
            ///

            var cellsCount = binaryReader.ReadInt32();

            var cellsVertices = new int[cellsCount][];
            var cellsConnections = new int[cellsCount][];

            for (var j = 0; j < cellsCount; j++)
            {
                var newCell = MakeCell(Enumerable.Empty<Vertex>());

                var cellVerticesCount = binaryReader.ReadInt32();
                var cellVertices = new int[cellVerticesCount];
                for (var k = 0; k < cellVerticesCount; k++)
                {
                    cellVertices[k] = binaryReader.ReadInt32();
                }
                cellsVertices[j] = cellVertices;

                var connectedCellsCount = binaryReader.ReadInt32();
                var connectedCells = new int[connectedCellsCount];
                for (var k = 0; k < connectedCellsCount; k++)
                {
                    connectedCells[k] = binaryReader.ReadInt32();
                }
                cellsConnections[j] = connectedCells;
            }

            foreach (var (cell, vertices) in cellsVertices.Select((vertices, cellIndex) => (Cells[cellIndex], vertices)))
            {
                foreach (var cellVertex in vertices.Select(vertexIdx => Vertices[vertexIdx]))
                {
                    cell.AddVertex(cellVertex);
                }
            }

            foreach (var (cell, conns) in cellsConnections.Select((conns, cellIndex) => (Cells[cellIndex], conns)))
            {
                foreach (var connectingCell in conns.Select(connectedIndex => Cells[connectedIndex]))
                {
                    cell.AddConnection(connectingCell);
                }
            }
        }

        public void WriteMesh(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(Vertices.Count);
            foreach (var vertex in Vertices)
            {
                binaryWriter.Write(vertex.Position.x);
                binaryWriter.Write(vertex.Position.y);
                binaryWriter.Write(vertex.Position.z);
            }

            binaryWriter.Write(Cells.Count);
            foreach (var cell in Cells)
            {
                binaryWriter.Write(cell.Vertices.Count);
                foreach (var vertexIdx in cell.Vertices.Select(cellVertex => Vertices.IndexOf(cellVertex)))
                {
                    binaryWriter.Write(vertexIdx);
                }

                binaryWriter.Write(cell.ConnectedCells.Count);
                foreach (var connIdx in cell.ConnectedCells.Select(connCell => Cells.IndexOf(connCell)))
                {
                    binaryWriter.Write(connIdx);
                }
            }
        }

        #endregion
    }
}
