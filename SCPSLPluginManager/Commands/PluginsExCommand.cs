using CommandSystem;
using LabApi.Loader;
using SCPSLPluginManager.Extensions;

namespace SCPSLPluginManager.Commands
{
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    internal class PluginsExCommand : ICommand
    {
        public string Command => "plugin_reload";

        public string[] Aliases => new string[] { };

        public string Description => "Reload specified plugin";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count < 1)
            {
                response = "No plugin assembly identifier.";
                return false;
            }

            var pluginIdentifier = arguments[0];

            var pluginsOfIdentifier = PluginLoader.Plugins.Where(p => p.Value.GetName().Name == pluginIdentifier);

            foreach (var plugin in pluginsOfIdentifier.Select(p => p.Key).ToArray())
            {
                if (PluginLoader.EnabledPlugins.Contains(plugin))
                {
                    PluginLoaderExtensions.DisablePlugin(plugin);
                }

                PluginLoader.Plugins.Remove(plugin);
            }

            PluginLoaderExtensions.LoadPlugins(pluginIdentifier);

            response = "Done.";
            return true;
        }
    }
}
