using HarmonyLib;
using LabApi.Features;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using SCPSLBot.AI;
using SCPSLBot.Cleanup;
using SCPSLBot.Navigation;
using SCPSLBot.Navigation.Mesh;
using SCPSLBot.Warmup;
using System;
using System.IO;
using System.Reflection;

namespace SCPSLBot
{
    public class LabApiPlugin : Plugin<BotPluginConfig>
    {
        public static LabApiPlugin Instance { get; private set; }

        public override string Name { get; } = "SCPSLBot";
        public override string Description { get; } = "Bot players addon.";
        public override string Author { get; } = "repkins(19)";
        public override Version Version { get; } = new ("0.0.1");
        public override Version RequiredApiVersion { get; } = new(LabApiProperties.CompiledVersion);

        private Harmony harmonyInstance;

        public override void Enable()
        {
            Instance = this;

            harmonyInstance = new Harmony($"SCPSLBot.{DateTime.Now.Ticks}");
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            Logger.Info("Patching successful.");

            NavigationSystem.Instance.BaseDir = Path.Combine(Path.GetDirectoryName(FilePath), Path.GetFileNameWithoutExtension(FilePath));

            if (!Directory.Exists(NavigationSystem.Instance.BaseDir))
            {
                Directory.CreateDirectory(NavigationSystem.Instance.BaseDir);
            }

            NavigationSystem.Instance.Init();
            NavigationMeshEditor.Instance.Init();

            BotManager.Instance.Init();
            OverflowCleanupManager.Instance.Init(Config);
            WarmupManager.Instance.Init(Config);

            Logger.Info("Enabled plugin.");
        }

        public override void Disable()
        {
            WarmupManager.Instance.Terminate();
            OverflowCleanupManager.Instance.Terminate();
            BotManager.Instance.Terminate();

            NavigationMeshEditor.Instance.Terminate();
            NavigationSystem.Instance.Terminate();

            harmonyInstance.UnpatchAll(harmonyInstance.Id);
            Logger.Info("Unpatching successful.");

            Instance = null;
            Logger.Info("Disabled plugin.");
        }

        public void SaveSettings()
        {
            SaveConfig();
        }
    }
}
