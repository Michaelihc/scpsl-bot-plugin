using CommandSystem;
using RemoteAdmin;
using SCPSLBot.Navigation.Mesh;
using System;

namespace SCPSLBot.Navigation.Commands
{
    [CommandHandler(typeof(Nav))]
    internal class NavPrintConnectorCommand : ICommand
    {
        public string Command { get; } = "print_connector";

        public string[] Aliases { get; } = new string[] { };

        public string Description { get; } = "Prints closest room connector.";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (sender is not PlayerCommandSender playerCommandSender)
            {
                response = "You must be in-game to use this command!";
                return false;
            }

            var playerPosition = playerCommandSender.ReferenceHub.transform.position;
            var connectorForm = NavigationMeshEditor.GetClosestConnector(playerPosition, out var direction);

            response = $"Closest room connector {connectorForm} at direction {direction}";
            return true;
        }
    }
}
