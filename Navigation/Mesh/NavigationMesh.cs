using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal partial class NavigationMesh
    {
        public List<LocalVertex> LocalVertices { get; } = new();
        public event Action<LocalVertex> LocalVertexCreated;
        public event Action<LocalVertex> LocalVertexDeleted;

        public List<LocalArea> LocalAreas { get; } = new();
        public event Action<LocalArea> LocalAreaCreated;
        public event Action<LocalArea> LocalAreaDeleted;

        private NavigationMesh()
        {
            LocalVertexDeleted += RemoveVertexFromAreas;
        }

        #region Mesh manipulation

        public LocalVertex AddVertex(Vector3 localPosition)
        {
            var newVertex = new LocalVertex(localPosition);
            LocalVertices.Add(newVertex);

            LocalVertexCreated?.Invoke(newVertex);

            return newVertex;
        }

        public bool DeleteVertex(LocalVertex vertex)
        {
            if (!LocalVertices.Remove(vertex))
            {
                return false;
            }

            LocalVertexDeleted?.Invoke(vertex);

            return true;
        }

        private void RemoveVertexFromAreas(LocalVertex vertex)
        {
            foreach (var area in LocalAreas)
            {
                area.RemoveVertex(vertex);
            }
        }

        public bool MoveVertex(LocalVertex vertex, Vector3 newLocalPosition)
        {
            vertex.LocalPosition = newLocalPosition;

            return true;
        }

        public LocalArea MakeArea(IEnumerable<LocalVertex> vertices, string form)
        {
            var newArea = new LocalArea(vertices, form);
            LocalAreas.Add(newArea);

            LocalAreaCreated?.Invoke(newArea);

            return newArea;
        }

        public bool RemoveArea(LocalArea area)
        {
            var areas = LocalAreas;
            if (!areas.Remove(area))
            {
                Log.Warning($"No areas at {area.Form} to remove area from.");
                return false;
            }

            RemoveConnectionsToArea(area);

            LocalAreaDeleted?.Invoke(area);

            return true;
        }

        private void RemoveConnectionsToArea(LocalArea area)
        {
            foreach (var otherArea in LocalAreas)
            {
                otherArea.RemoveConnection(area);
            }
        }

        public void CreateAreaConnection(LocalArea fromArea, LocalArea toArea)
        {
            fromArea.AddConnection(toArea);
        }

        public void DeleteAreaConnection(LocalArea fromArea, LocalArea toArea)
        {
            fromArea.RemoveConnection(toArea);
        }

        public void AddVertexToArea(LocalArea area, LocalVertex vertex, LocalVertex beforeVertex)
        {
            area.AddVertex(vertex, beforeVertex);
        }

        #endregion
        #region Mesh reading/writing

        public void ReadMesh(BinaryReader binaryReader, string form)
        {
            ///
            /// Vertices reading
            /// 

            var vertexCount = binaryReader.ReadInt32();

            for (var j = 0; j < vertexCount; j++)
            {
                var vertexLocalPosition = new Vector3()
                {
                    x = binaryReader.ReadSingle(),
                    y = binaryReader.ReadSingle(),
                    z = binaryReader.ReadSingle()
                };

                var newRoomFormVertex = AddVertex(vertexLocalPosition);
            }

            ///
            /// Areas reading
            ///

            var areasCount = binaryReader.ReadInt32();

            var areasVertices = new int[areasCount][];
            var areasConnections = new int[areasCount][];

            for (var j = 0; j < areasCount; j++)
            {
                var newRoomFormArea = MakeArea(Enumerable.Empty<LocalVertex>(), form);

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

            foreach (var (area, vertices) in areasVertices.Select((vertices, areaIndex) => (LocalAreas[areaIndex], vertices)))
            {
                foreach (var areaVertex in vertices.Select(vertexIdx => LocalVertices[vertexIdx]))
                {
                    area.AddVertex(areaVertex);
                }
            }

            foreach (var (area, conns) in areasConnections.Select((conns, areaIndex) => (LocalAreas[areaIndex], conns)))
            {
                foreach (var connectingArea in conns.Select(connectedIndex => LocalAreas[connectedIndex]))
                {
                    area.AddConnection(connectingArea);
                }
            }
        }

        public void WriteMesh(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(LocalVertices.Count);
            foreach (var vertex in LocalVertices)
            {
                binaryWriter.Write(vertex.LocalPosition.x);
                binaryWriter.Write(vertex.LocalPosition.y);
                binaryWriter.Write(vertex.LocalPosition.z);
            }

            binaryWriter.Write(LocalAreas.Count);
            foreach (var area in LocalAreas)
            {
                binaryWriter.Write(area.Vertices.Count);
                foreach (var vertexIdx in area.Vertices.Select(areaVertex => LocalVertices.IndexOf(areaVertex)))
                {
                    binaryWriter.Write(vertexIdx);
                }

                binaryWriter.Write(area.ConnectedLocalAreas.Count);
                foreach (var connIdx in area.ConnectedLocalAreas.Select(connArea => LocalAreas.IndexOf(connArea)))
                {
                    binaryWriter.Write(connIdx);
                }
            }
        }

        #endregion
    }
}
