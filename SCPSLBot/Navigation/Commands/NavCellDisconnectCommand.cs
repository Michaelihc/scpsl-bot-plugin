using CommandSystem;
using PlayerRoles;
using RemoteAdmin;
using SCPSLBot.Navigation.Mesh;
using System;

namespace SCPSLBot.Navigation.Commands
{
    [CommandHandler(typeof(NavCell))]
    internal class NavCellDisconnectCommand : ICommand
    {
        public string Command { get; } = "disconnect";

        public string[] Aliases { get; } = new string[] { };

        public string Description { get; } = "Deletes connection from cached cell to cell within.";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (sender is not PlayerCommandSender playerCommandSender)
            {
                response = "You must be in-game to use this command!";
                return false;
            }

            if (!playerCommandSender.ReferenceHub.IsAlive())
            {
                response = "Command disabled when you are not alive!";
                return false;
            }

            if (!NavigationMeshEditor.Instance.DeleteConnection())
            {
                response = "Failed to delete connection!";
                return false;
            }

            response = $"Connection from cached cell to cell within is deleted.";
            return true;
        }
    }
}
