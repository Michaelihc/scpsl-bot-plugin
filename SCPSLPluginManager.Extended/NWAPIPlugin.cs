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

        [PluginConfig()]
        public Config? Config;

        [PluginEntryPoint("SCPSLPluginManager", "1.0.0", "Plugin manager extensions.", "repkins(19)")]
        public void OnLoad()
        {
            Instance = this;

            Log.Info("Loaded plugin.");
        }

        [PluginUnload]
        public void OnUnload()
        {
            Instance = null;

            Log.Info("Unloaded plugin.");
        }
    }
}
