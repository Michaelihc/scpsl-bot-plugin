#define UNITY_ASSERTIONS

using CommandSystem;
using SCPSLBot.Navigation;
using SCPSLBot.Tests.Commands;
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
            Assert.IsTrue(NavigationSystem.Instance.Initialized);

            response = $"Passed.";
            return true;
        }
    }
}
