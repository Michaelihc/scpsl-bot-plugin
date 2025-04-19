using HarmonyLib;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using SCPSLBot.AI;
using SCPSLBot.Navigation;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Reflection;

namespace SCPSLBot
{
    public class NWAPIPlugin
    {
        public Harmony HarmonyInstance;

        [PluginConfig()]
        public Config Config;

        [PluginEntryPoint("SCPSLBot", "1.0.0", "AI players addon.", "repkins(19)")]
        public void OnLoad()
        {
            HarmonyInstance = new Harmony($"SCPSLBot.100.{DateTime.Now.Ticks}");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            Log.Info("Patching successful.");

            NavigationSystem.Instance.BaseDir = PluginHandler.Get(this).PluginDirectoryPath;

            NavigationSystem.Instance.Init();
            NavigationMeshEditor.Instance.Init();

            BotManager.Instance.Init();

            Log.Info("Loaded plugin.");
        }

        [PluginUnload]
        public void OnUnload()
        {
            BotManager.Instance.Terminate();

            NavigationMeshEditor.Instance.Terminate();
            NavigationSystem.Instance.Terminate();

            HarmonyInstance.UnpatchAll();

            Log.Info("Unloaded plugin.");
        }
    }
}
