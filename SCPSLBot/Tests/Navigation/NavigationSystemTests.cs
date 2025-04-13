#define UNITY_ASSERTIONS

using CommandSystem;
using SCPSLBot.Navigation;
using SCPSLBot.Navigation.Mesh;
using System;
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
            NavigationMesh.InitMeshes();

            TestCreateVertex();

            response = $"Passed.";
            return true;
        }

        private void TestCreateVertex()
        {

        }

        private void TestDeleteVertex()
        {

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
    }
}
