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

        public List<Area> Areas { get; } = new();
        public event Action<Area> AreaCreated;
        public event Action<Area> AreaDeleted;

        private NavigationMesh()
        {
            VertexDeleted += RemoveVertexFromAreas;
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

        private void RemoveVertexFromAreas(Vertex vertex)
        {
            foreach (var area in Areas)
            {
                area.RemoveVertex(vertex);
            }
        }

        public bool MoveVertex(Vertex vertex, Vector3 newPosition)
        {
            vertex.Position = newPosition;

            return true;
        }

        public Area MakeArea(IEnumerable<Vertex> vertices)
        {
            var newArea = new Area(vertices);
            Areas.Add(newArea);

            AreaCreated?.Invoke(newArea);

            return newArea;
        }

        public bool RemoveArea(Area area)
        {
            var areas = Areas;
            if (!areas.Remove(area))
            {
                return false;
            }

            RemoveConnectionsToArea(area);

            AreaDeleted?.Invoke(area);

            return true;
        }

        private void RemoveConnectionsToArea(Area area)
        {
            foreach (var otherArea in Areas)
            {
                otherArea.RemoveConnection(area);
            }
        }

        public void CreateAreaConnection(Area fromArea, Area toArea)
        {
            fromArea.AddConnection(toArea);
        }

        public void DeleteAreaConnection(Area fromArea, Area toArea)
        {
            fromArea.RemoveConnection(toArea);
        }

        public void AddVertexToArea(Area area, Vertex vertex, Vertex beforeVertex)
        {
            area.AddVertex(vertex, beforeVertex);
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
            /// Areas reading
            ///

            var areasCount = binaryReader.ReadInt32();

            var areasVertices = new int[areasCount][];
            var areasConnections = new int[areasCount][];

            for (var j = 0; j < areasCount; j++)
            {
                var newArea = MakeArea(Enumerable.Empty<Vertex>());

                var areaVerticesCount = binaryReader.ReadInt32();
                var areaVertices = new int[areaVerticesCount];
                for (var k = 0; k < areaVerticesCount; k++)
                {
                    areaVertices[k] = binaryReader.ReadInt32();
                }
                areasVertices[j] = areaVertices;

                var connectedAreasCount = binaryReader.ReadInt32();
                var connectedAreas = new int[connectedAreasCount];
                for (var k = 0; k < connectedAreasCount; k++)
                {
                    connectedAreas[k] = binaryReader.ReadInt32();
                }
                areasConnections[j] = connectedAreas;
            }

            foreach (var (area, vertices) in areasVertices.Select((vertices, areaIndex) => (Areas[areaIndex], vertices)))
            {
                foreach (var areaVertex in vertices.Select(vertexIdx => Vertices[vertexIdx]))
                {
                    area.AddVertex(areaVertex);
                }
            }

            foreach (var (area, conns) in areasConnections.Select((conns, areaIndex) => (Areas[areaIndex], conns)))
            {
                foreach (var connectingArea in conns.Select(connectedIndex => Areas[connectedIndex]))
                {
                    area.AddConnection(connectingArea);
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

            binaryWriter.Write(Areas.Count);
            foreach (var area in Areas)
            {
                binaryWriter.Write(area.Vertices.Count);
                foreach (var vertexIdx in area.Vertices.Select(areaVertex => Vertices.IndexOf(areaVertex)))
                {
                    binaryWriter.Write(vertexIdx);
                }

                binaryWriter.Write(area.ConnectedAreas.Count);
                foreach (var connIdx in area.ConnectedAreas.Select(connArea => Areas.IndexOf(connArea)))
                {
                    binaryWriter.Write(connIdx);
                }
            }
        }

        #endregion
    }
}
