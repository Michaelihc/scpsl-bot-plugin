using CommandSystem;
using PlayerRoles;
using RemoteAdmin;
using SCPSLBot.Navigation.Mesh;
using System;

namespace SCPSLBot.Navigation.Commands
{
    [CommandHandler(typeof(NavArea))]
    internal class NavAreaMakeCommand : ICommand
    {
        public string Command { get; } = "make";

        public string[] Aliases { get; } = new string[] { };

        public string Description { get; } = "Makes new navigation mesh area from selected vertices.";

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

            var formType = "room";
            if (arguments.Count > 0)
            {
                formType = arguments[0];
            }

            RoomFormArea formArea;
            switch (formType)
            {
                case "room": 
                    formArea = NavigationMeshEditor.Instance.MakeArea(playerCommandSender.ReferenceHub.transform.position);
                    break;
                case "connector":
                    formArea = NavigationMeshEditor.Instance.MakeArea(playerCommandSender.ReferenceHub.transform.position, connector: true);
                    break;
                default:
                    response = "Unrecognized form type argument!";
                    return false;
            }

            response = $"Area at local center position {formArea.LocalCenterPosition} created.";
            return true;
        }
    }
}
