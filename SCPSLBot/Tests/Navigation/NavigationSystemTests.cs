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

            TestCreateVertex(existingForm);
            TestCreateVertex(existingForm);
            TestCreateVertex(existingForm);
            TestCreateVertex(existingForm);

            TestDeleteVertex(existingForm, 2);

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

        private void TestCreateArea()
        {

        }

        private void TestDeleteArea()
        {

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
