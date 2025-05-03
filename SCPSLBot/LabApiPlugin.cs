using HarmonyLib;
using LabApi.Features;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using SCPSLBot.AI;
using SCPSLBot.Navigation;
using SCPSLBot.Navigation.Mesh;
using System;
using System.IO;
using System.Reflection;

namespace SCPSLBot
{
    public class LabApiPlugin : Plugin
    {
        public override string Name { get; } = "SCPSLBot";
        public override string Description { get; } = "AI players addon.";
        public override string Author { get; } = "repkins(19)";
        public override Version Version { get; } = new ("1.0.0");
        public override Version RequiredApiVersion { get; } = new(LabApiProperties.CompiledVersion);

        private Harmony harmonyInstance;

        public override void Enable()
        {
            harmonyInstance = new Harmony($"SCPSLBot.100.{DateTime.Now.Ticks}");
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

            Logger.Info("Enabled plugin.");
        }

        public override void Disable()
        {
            BotManager.Instance.Terminate();

            NavigationMeshEditor.Instance.Terminate();
            NavigationSystem.Instance.Terminate();

            harmonyInstance.UnpatchAll();
            Logger.Info("Unpatching successful.");

            Logger.Info("Disabled plugin.");
        }
    }
}
