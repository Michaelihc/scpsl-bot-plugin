using CommandSystem;
using SCPSLBot.AI;
using System;

namespace SCPSLBot.AI.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    internal class BotAddCommand : ICommand
    {
        public string Command => "bot_add";

        public string[] Aliases => new string[] { };

        public string Description => "Bot add";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!BotManager.Instance.CanSpawnBot())
            {
                response = "Cannot spawn a bot before the network server is active.";
                return false;
            }

            if (BotManager.Instance.BotPlayers.Count >= 10)
            {
                response = "Bot cap reached (10). Use the warmup panel bot count control to lower it.";
                return false;
            }

            BotManager.Instance.AddBotPlayer();

            response = "Done.";
            return true;
        }
    }
}
