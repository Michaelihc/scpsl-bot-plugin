using CommandSystem;
using SCPSLBot.AI.FirstPersonControl;
using System;

namespace SCPSLBot.AI.Commands
{
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    internal class BotDumpMindCommand : ICommand
    {
        public string Command => "bot_mind_dump";

        public string[] Aliases => [];

        public string Description => "Dumps bot mind action graph to debug output.";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count < 1)
            {
                response = "Player id not supplied.";
                return false;
            }

            if (!int.TryParse(arguments[0], out var playerId))
            {
                response = "Player id is not parsable as int.";
                return false;
            }

            var referenceHub = ReferenceHub.GetHub(playerId);

            if (!BotManager.Instance.BotPlayers.TryGetValue(referenceHub, out var botHub))
            {
                response = "Player id is not of bot player.";
                return false;
            }

            if (botHub.CurrentBotPlayer is not FpcBotPlayer botPlayer)
            {
                response = "Bot current role is not supported.";
                return false;
            }

            var dumpBuilder = botPlayer.DumpVisitedActionsGraph(allActions: true);
            var dump = dumpBuilder.ToString();

            foreach (var line in dump.Split(Environment.NewLine))
            {
                UnityEngine.Debug.Log(line);
            }

            response = "Done.";
            return true;
        }
    }
}
