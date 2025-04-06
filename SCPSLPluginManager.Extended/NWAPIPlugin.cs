using HarmonyLib;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using System;
using System.Reflection;

namespace SCPSLPluginManager
{
    public class NWAPIPlugin
    {
        public static NWAPIPlugin? Instance;
        public static Harmony? HarmonyInstance;

        [PluginConfig()]
        public Config? Config;

        [PluginEntryPoint("SCPSLPluginManager", "1.0.0", "Plugin manager extensions.", "repkins(19)")]
        public void OnLoad()
        {
            Instance = this;

            HarmonyInstance = new Harmony($"SCPSLPluginManager.100.{DateTime.Now.Ticks}");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            Log.Info("Patching successful.");

            Log.Info("Loaded plugin.");
        }

        [PluginUnload]
        public void OnUnload()
        {
            Instance = null;

            HarmonyInstance!.UnpatchAll();
            HarmonyInstance = null;

            Log.Info("Unloaded plugin.");
        }
    }
}
