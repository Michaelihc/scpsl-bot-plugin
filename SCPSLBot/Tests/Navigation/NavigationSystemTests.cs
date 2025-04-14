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

            TestMakeArea(existingForm, 0, 1, 2);        // #0
            TestMakeArea(existingForm, 2, 1, 3, 4);     // #1

            TestCreateVertex(existingForm);     // #5
            TestCreateVertex(existingForm);     // #6
            TestCreateVertex(existingForm);     // #7
            TestCreateVertex(existingForm);     // #8

            TestMakeArea(existingForm, 2, 3, 7, 8);     // #2

            TestRemoveArea(existingForm, 2);            // #1
            TestMakeArea(existingForm, 4, 3, 5, 6);     // #2

            TestAddConnection(existingForm, 0, 1);
            TestAddConnection(existingForm, 1, 0);
            TestAddConnection(existingForm, 1, 2);

            TestRemoveConnection(existingForm, 1, 2);
            TestAddConnection(existingForm, 1, 2);

            TestRemoveArea(existingForm, 2);            // #1

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

        private void TestMakeArea(string form, params int[] vertexIdxs)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];
            var vertices = vertexIdxs.Select(idx => mesh.Vertices[idx]);
            var edges = vertices.Zip(vertices.Skip(1), (v1, v2) => new Edge(v1, v2)).Append(new(vertices.Last(), vertices.First()));

            Area emittedArea = null;
            void createdHandler(Area a)
            {
                emittedArea = a;
            }
            mesh.AreaCreated += createdHandler;

            var area = mesh.MakeArea(vertices);

            Assert.AreEqual(vertexIdxs.Length, area.Vertices.Count);
            foreach (var (vertex, areaVertex) in vertices.Zip(area.Vertices, (vertex, areaVertex) => (vertex, areaVertex)))
            {
                Assert.AreEqual(vertex, areaVertex);
            }

            Assert.AreEqual(edges.Count(), area.Edges.Count());
            foreach (var (edge, areaEdge) in edges.Zip(area.Edges, (edge, areaEdge) => (edge, areaEdge)))
            {
                Assert.AreEqual(edge, areaEdge);
            }

            Assert.IsNotNull(area);
            Assert.IsTrue(mesh.Areas.Contains(area));

            if (NavigationMesh.RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    var transformArea = new TransformArea(area, room.transform);
                    Assert.IsTrue(NavigationMesh.ForeignConnectedAreas.ContainsKey(transformArea));
                    Assert.IsNotNull(NavigationMesh.ForeignConnectedAreas[transformArea]);
                }
            }

            Assert.AreEqual(emittedArea, area);

            mesh.AreaCreated -= createdHandler;

            Log.Info($"{nameof(TestMakeArea)}({form}, {string.Join(", ", vertexIdxs)})");
        }

        private void TestRemoveArea(string form, int areaIdx)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];
            var area = mesh.Areas[areaIdx];

            Area emittedArea = null;
            void deletedHandler(Area a)
            {
                emittedArea = a;
            }
            mesh.AreaDeleted += deletedHandler;

            var countBefore = mesh.Areas.Count;

            mesh.RemoveArea(area);

            var countAfter = mesh.Areas.Count;

            Assert.IsFalse(mesh.Areas.Contains(area));
            Assert.AreEqual(1, countBefore - countAfter);

            foreach (var otherArea in mesh.Areas)
            {
                Assert.IsFalse(otherArea.ConnectedAreas.Contains(area));
            }

            if (NavigationMesh.RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    var transformArea = new TransformArea(area, room.transform);
                    foreach (var otherArea in mesh.Areas)
                    {
                        var otherTransformArea = new TransformArea(otherArea, room.transform);
                        Assert.IsFalse(otherTransformArea.ConnectedAreas.Contains(transformArea));
                    }

                    Assert.IsFalse(NavigationMesh.ForeignConnectedAreas.ContainsKey(transformArea));
                }
            }

            Assert.AreEqual(emittedArea, area);

            mesh.AreaDeleted -= deletedHandler;

            Log.Info($"{nameof(TestRemoveArea)}({form}, {areaIdx})");
        }

        private void TestAddConnection(string form, int fromIdx, int toIdx)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];
            var fromArea = mesh.Areas[fromIdx];
            var toArea = mesh.Areas[toIdx];

            fromArea.AddConnection(toArea);

            Assert.IsTrue(fromArea.ConnectedAreas.Contains(toArea));

            Assert.IsTrue(fromArea.ConnectedAreaEdges.TryGetValue(toArea, out var connectedEdge));
            Assert.AreEqual(toArea.Edges.First(te => fromArea.Edges.Contains(new Edge(te.To, te.From))), connectedEdge);

            if (NavigationMesh.RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    var fromTransformArea = new TransformArea(fromArea, room.transform);
                    var toTransformArea = new TransformArea(toArea, room.transform);

                    Assert.IsTrue(fromTransformArea.ConnectedAreas.Contains(toTransformArea));
                }
            }

            Log.Info($"{nameof(TestAddConnection)}({form}, {fromIdx}, {toIdx})");
        }

        private void TestRemoveConnection(string form, int fromIdx, int toIdx)
        {
            var mesh = NavigationMesh.MeshesByRoomForm[form];
            var fromArea = mesh.Areas[fromIdx];
            var toArea = mesh.Areas[toIdx];

            fromArea.RemoveConnection(toArea);

            Assert.IsFalse(fromArea.ConnectedAreas.Contains(toArea));
            Assert.IsFalse(fromArea.ConnectedAreaEdges.ContainsKey(toArea));

            if (NavigationMesh.RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    var fromTransformArea = new TransformArea(fromArea, room.transform);
                    var toTransformArea = new TransformArea(toArea, room.transform);
                    Assert.IsFalse(fromTransformArea.ConnectedAreas.Contains(toTransformArea));
                }
            }

            Log.Info($"{nameof(TestRemoveConnection)}({form}, {fromIdx}, {toIdx})");
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
