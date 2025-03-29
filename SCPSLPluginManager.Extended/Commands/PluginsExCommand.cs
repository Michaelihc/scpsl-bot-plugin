using CommandSystem;
using HarmonyLib;
using PluginAPI.Commands;
using PluginAPI.Core;
using PluginAPI.Core.Extensions;
using PluginAPI.Helpers;
using PluginAPI.Loader;
using RemoteAdmin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

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
                response = "No plugin identifier.";
                return false;
            }

            var pluginIdentifier = arguments[0];

            var (assembly, handlersByType) = AssemblyLoader.Plugins.First(p => p.Key.GetName().Name == pluginIdentifier);

            var pluginToAssembly = (Dictionary<object, Assembly>)AccessTools.Property(typeof(AssemblyLoader), "PluginToAssembly").GetValue(null);

            foreach (var handler in handlersByType.Values)
            {
                handler.Unload();

                var registeredCommands = (Dictionary<Type, Dictionary<string, Command>>)AccessTools.Field(typeof(CommandsManager), "_registeredCommands").GetValue(null);
                var commandHandlerToName = (Dictionary<Type, string>)AccessTools.Field(typeof(CommandsManager), "_commandHandlerToName").GetValue(null);
                foreach (var (commandHandlerType, commandEntries) in registeredCommands.ToArray())
                {
                    if (commandHandlerType.Assembly == assembly)
                    {
                        commandHandlerToName.Remove(commandHandlerType);
                        registeredCommands.Remove(commandHandlerType);
                        continue;
                    }

                    foreach (var commandToRemove in commandEntries.Where(p => p.Value.Plugin == handler).ToArray())
                    {
                        if (!commandEntries.Remove(commandToRemove.Key))
                        {
                            Log.Warning($"Could not remove {commandToRemove.Key} at {commandHandlerType}");
                        }

                        if (commandHandlerType == typeof(GameConsoleCommandHandler))
                        {
                            GameCore.Console.singleton.ConsoleCommandHandler.UnregisterCommand(commandToRemove.Value.Object);
                        }
                        else if (commandHandlerType == typeof(RemoteAdminCommandHandler))
                        {
                            CommandProcessor.RemoteAdminCommandHandler.UnregisterCommand(commandToRemove.Value.Object);
                        }
                        else if (commandHandlerType == typeof(ClientCommandHandler))
                        {
                            QueryProcessor.DotCommandHandler.UnregisterCommand(commandToRemove.Value.Object);
                        }
                    }
                }

                pluginToAssembly.Remove(AccessTools.Field(typeof(PluginHandler), "_plugin").GetValue(handler));
            }

            AssemblyLoader.Plugins.Remove(assembly);

            var assemblyPath = Path.Combine(Paths.GlobalPlugins.Plugins, assembly.GetName().Name+".dll");
            assembly = Assembly.Load(File.ReadAllBytes(assemblyPath));

            var types = assembly.GetTypes();
            var typesWithEntryPoints = types.Where(t => t.IsValidEntrypoint());

            foreach (var type in typesWithEntryPoints)
            {
                if (!AssemblyLoader.Plugins.ContainsKey(assembly))
                {
                    AssemblyLoader.Plugins.Add(assembly, []);
                }
                if (!AssemblyLoader.Plugins[assembly].ContainsKey(type))
                {
                    var obj = Activator.CreateInstance(type);

                    pluginToAssembly.Add(obj, assembly);
                    AssemblyLoader.Plugins[assembly].Add(type, new PluginHandler(Paths.GlobalPlugins, obj, type, types));
                }
            }

            foreach (var pluginHandler in AssemblyLoader.Plugins[assembly].Values)
            {
                pluginHandler.Load();
            }

            response = "Done.";
            return true;
        }
    }
}
