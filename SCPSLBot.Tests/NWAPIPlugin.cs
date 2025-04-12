using PluginAPI.Core;
using PluginAPI.Core.Attributes;

namespace SCPSLBot.Tests
{
    public class NWAPIPlugin
    {
        [PluginEntryPoint("SCPSLBot.Tests", "1.0.0", "Testing plugin for AI players addon.", "repkins(19)")]
        public void OnLoad()
        {
            Log.Info("Loaded plugin.");
        }

        [PluginUnload]
        public void OnUnload()
        {
            Log.Info("Unloaded plugin.");
        }
    }
}
