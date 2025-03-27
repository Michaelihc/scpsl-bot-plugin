using HarmonyLib;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Helpers;
using System;
using System.Reflection;

namespace SCPSLTests.Runner
{
    public class NWAPIPlugin
    {
        public static NWAPIPlugin? Instance;
        public static Harmony? HarmonyInstance;

        [PluginConfig()]
        public Config? Config;

        [PluginEntryPoint("SCPSLTests.Runner", "1.0.0", "Tests runner.", "repkins(19)")]
        public void OnLoad()
        {
            Instance = this;

            HarmonyInstance = new Harmony($"SCPSLTests.Runner.100.{DateTime.Now.Ticks}");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            Log.Info("Patching successful.");

            Log.Info("Loaded plugin.");
        }

        [PluginUnload]
        public void OnUnload()
        {
            HarmonyInstance!.UnpatchAll();
            HarmonyInstance = null;
            Instance = null;
        }
    }
}
