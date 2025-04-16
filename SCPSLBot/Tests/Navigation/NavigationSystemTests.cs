#define UNITY_ASSERTIONS

using CommandSystem;
using PluginAPI.Core;
using SCPSLBot.Navigation;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Assertions;

namespace SCPSLBot.Tests.Navigation
{
    internal class NavigationSystemTests : ICommand
    {
        public string Command { get; } = "nav";

        public string[] Aliases { get; } = new string[] { };

        public string Description { get; } = "Tests navigation.";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            NavigationSystem.Instance.Terminate();

            TestInitMeshes();

            var existingForm = GetRandomExistingForm();
            TestCreateMesh(existingForm);
            TestCreateMesh("Non_Existing");

            TestCreateVertex(existingForm);     // #0
            TestCreateVertex(existingForm);     // #1
            TestCreateVertex(existingForm);     // #2
            TestCreateVertex(existingForm);     // #3
            TestCreateVertex(existingForm);     // #4

            TestDeleteVertex(existingForm, 2);  // #3
            TestCreateVertex(existingForm);     // #4

            TestMakeCell(existingForm, 0, 1, 2);        // #0
            TestMakeCell(existingForm, 2, 1, 3, 4);     // #1

            TestCreateVertex(existingForm);     // #5
            TestCreateVertex(existingForm);     // #6
            TestCreateVertex(existingForm);     // #7
            TestCreateVertex(existingForm);     // #8

            TestMakeCell(existingForm, 2, 3, 7, 8);     // #2

            TestRemoveCell(existingForm, 2);            // #1
            TestMakeCell(existingForm, 4, 3, 5, 6);     // #2

            TestRemoveCell(existingForm, 2);            // #1

            response = $"Passed.";
            return true;
        }

        private void TestInitMeshes()
        {
            NavigationMesh.InitMeshes();

            Assert.AreEqual(Facility.Rooms.Count, NavigationMesh.RoomsByForm.Values.SelectMany(l => l).Count());
            foreach (var apiRoom in Facility.Rooms)
            {
                var room = apiRoom.Identifier.gameObject;
                var roomForm = room.name.EndsWith("(Clone)") ?
                    room.name.Remove(room.name.LastIndexOf("(Clone)")) :
                    room.name;

                Assert.IsTrue(NavigationMesh.RoomsByForm.TryGetValue(roomForm, out var rooms));
                Assert.IsTrue(rooms.Contains(room));
            }

            Log.Info(nameof(TestInitMeshes));
        }

        private void TestCreateMesh(string form)
        {
            var mesh = NavigationMesh.CreateRoom(form);

            Assert.IsNotNull(mesh);

            Assert.IsTrue(NavigationMesh.MeshesByRoomForm.ContainsKey(form));
            Assert.AreEqual(mesh, NavigationMesh.MeshesByRoomForm[form]);

            if (NavigationMesh.RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    Assert.IsTrue(NavigationMesh.LocalMeshesByRoom.ContainsKey(room));
                    Assert.AreEqual(mesh, NavigationMesh.LocalMeshesByRoom[room]);
                }
            }

            Log.Info($"{nameof(TestCreateMesh)}({form})");
        }

        private Vertex TestCreateVertex(string form)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];

            Vertex emittedVertex = null;
            void vertexCreatedHandler(Vertex v)
            {
                emittedVertex = v;
            }
            mesh.VertexCreated += vertexCreatedHandler;

            var position = UnityEngine.Random.insideUnitSphere with { y = 0f } * 5f;
            var vertex = mesh.AddVertex(position);

            Assert.IsNotNull(vertex);
            Assert.IsTrue(mesh.Vertices.Contains(vertex));

            Assert.AreEqual(emittedVertex, vertex);

            mesh.VertexCreated -= vertexCreatedHandler;

            Log.Info($"{nameof(TestCreateVertex)}({form})");
            return vertex;
        }

        private void TestDeleteVertex(string form, int vertexIdx)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];
            var vertex = mesh.Vertices[vertexIdx];

            Vertex emittedVertex = null;
            void vertexDeletedHandler(Vertex v)
            {
                emittedVertex = v;
            }
            mesh.VertexDeleted += vertexDeletedHandler;

            var countBefore = mesh.Vertices.Count;

            mesh.DeleteVertex(vertex);

            var countAfter = mesh.Vertices.Count;

            Assert.IsFalse(mesh.Vertices.Contains(vertex));
            Assert.AreEqual(1, countBefore - countAfter);

            Assert.AreEqual(emittedVertex, vertex);

            mesh.VertexDeleted -= vertexDeletedHandler;

            Log.Info($"{nameof(TestDeleteVertex)}({form}, {vertexIdx})");
        }

        private void TestMakeCell(string form, params int[] vertexIdxs)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];
            var vertices = vertexIdxs.Select(idx => mesh.Vertices[idx]);
            var edges = vertices.Zip(vertices.Skip(1), (v1, v2) => new Edge(v1, v2)).Append(new(vertices.Last(), vertices.First()));

            Cell emittedCell = null;
            void createdHandler(Cell a)
            {
                emittedCell = a;
            }
            mesh.CellCreated += createdHandler;

            var cell = mesh.MakeCell(vertices);

            Assert.IsNotNull(cell);

            Assert.AreEqual(vertexIdxs.Length, cell.Vertices.Count);
            foreach (var (vertex, cellVertex) in vertices.Zip(cell.Vertices, (vertex, cellVertex) => (vertex, cellVertex)))
            {
                Assert.AreEqual(vertex, cellVertex);
            }

            Assert.AreEqual(edges.Count(), cell.Edges.Count());
            foreach (var (edge, cellEdge) in edges.Zip(cell.Edges, (edge, cellEdge) => (edge, cellEdge)))
            {
                Assert.AreEqual(edge, cellEdge);
            }

            foreach (var edge in edges)
            {
                var adjacentEdge = new Edge(edge.To, edge.From);
                var adjacentCells = mesh.Cells.Where(c => c.Edges.Contains(adjacentEdge));
                foreach (var adjacentCell in adjacentCells)
                {
                    Assert.IsTrue(cell.AdjacentCellEdges.TryGetValue(adjacentCell, out var cellAdjacentEdge), "Cell does not contain expected adjacent cell.");
                    Assert.AreEqual(adjacentEdge, cellAdjacentEdge, "Cell does not contain expected edge of adjacent cell.");

                    Assert.IsTrue(adjacentCell.AdjacentCellEdges.TryGetValue(cell, out var cellAdjacentAdjacentEdge), "Adjacent cell does not contain expected cell.");
                    Assert.AreEqual(edge, cellAdjacentAdjacentEdge, "Adjacent cell does not contain expected edge of cell.");
                }
            }

            Assert.IsTrue(mesh.Cells.Contains(cell));

            if (NavigationMesh.RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    var transformCell = new TransformCell(cell, room.transform);
                    Assert.AreEqual(room.transform.TransformPoint(cell.CenterPosition), transformCell.CenterPosition);

                    Assert.IsTrue(NavigationMesh.ForeignConnectedCells.ContainsKey(transformCell));
                    Assert.IsNotNull(NavigationMesh.ForeignConnectedCells[transformCell]);

                    foreach (var transformEdge in edges.Select(e => new TransformEdge(e, room.transform)))
                    {
                        var adjacentTransformEdge = new TransformEdge(transformEdge.To, transformEdge.From, room.transform);
                        var adjacentTransformCells = mesh.Cells.Where(c => c.Edges.Select(e => new TransformEdge(e, room.transform)).Contains(adjacentTransformEdge))
                            .Select(c => new TransformCell(c, room.transform));
                        foreach (var adjacentTransformCell in adjacentTransformCells)
                        {
                            Assert.IsTrue(transformCell.AdjacentCellEdges.TryGetValue(adjacentTransformCell, out var cellAdjacentEdge), "Transform cell does not contain expected adjacent transform cell.");
                            Assert.AreEqual(adjacentTransformEdge, cellAdjacentEdge, "Transform cell does not contain expected edge of adjacent transform cell.");
                            
                            Assert.IsTrue(adjacentTransformCell.AdjacentCellEdges.TryGetValue(transformCell, out var cellAdjacentAdjacentEdge), "Transform adjacent cell does not contain expected transform cell.");
                            Assert.AreEqual(transformEdge, cellAdjacentAdjacentEdge, "Transform adjacent cell does not contain expected edge of transform cell.");
                        }
                    }
                }
            }

            Assert.AreEqual(emittedCell, cell);

            mesh.CellCreated -= createdHandler;

            Log.Info($"{nameof(TestMakeCell)}({form}, {string.Join(", ", vertexIdxs)})");
        }

        private void TestRemoveCell(string form, int cellIdx)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];
            var cell = mesh.Cells[cellIdx];

            Cell emittedCell = null;
            void deletedHandler(Cell a)
            {
                emittedCell = a;
            }
            mesh.CellDeleted += deletedHandler;

            var countBefore = mesh.Cells.Count;

            mesh.RemoveCell(cell);

            var countAfter = mesh.Cells.Count;

            Assert.IsFalse(mesh.Cells.Contains(cell));
            Assert.AreEqual(1, countBefore - countAfter);

            foreach (var otherCell in mesh.Cells)
            {
                Assert.IsFalse(otherCell.AdjacentCells.Contains(cell));
                Assert.IsFalse(otherCell.AdjacentCellEdges.ContainsKey(cell));
            }

            if (NavigationMesh.RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    var transformCell = new TransformCell(cell, room.transform);
                    foreach (var otherCell in mesh.Cells)
                    {
                        var otherTransformCell = new TransformCell(otherCell, room.transform);
                        Assert.IsFalse(otherTransformCell.AdjacentCells.Contains(transformCell));
                        Assert.IsFalse(otherTransformCell.AdjacentCellEdges.ContainsKey(transformCell));
                    }

                    Assert.IsFalse(NavigationMesh.ForeignConnectedCells.ContainsKey(transformCell));
                }
            }

            Assert.AreEqual(emittedCell, cell);

            mesh.CellDeleted -= deletedHandler;

            Log.Info($"{nameof(TestRemoveCell)}({form}, {cellIdx})");
        }

        private void TestPersistance()
        {
        }

        private string GetRandomExistingForm()
        {
            var random = new Random();
            var count = NavigationMesh.RoomsByForm.Keys.Count;
            return NavigationMesh.RoomsByForm.Keys.ElementAt(random.Next(count - 1));
        }
    }
}
