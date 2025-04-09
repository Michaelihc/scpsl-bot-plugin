using CommandSystem;
using NUnit;
using NUnit.Engine;
using PluginAPI.Core;
using SCPSLTests.Runner.Listeners;
using System.Xml;

namespace SCPSLTests.Runner.Commands
{
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    internal class RunTestsCommand : ICommand
    {
        public string Command => "tests";

        public string[] Aliases => new string[] { };

        public string Description => "Run tests";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count < 1)
            {
                response = "No assembly specifier.";
                return false;
            }

            var assemblyFileName = arguments[0];

            using var engine = TestEngineActivator.CreateInstance();
            engine.Initialize();

            var package = new TestPackage(assemblyFileName);
            package.AddSetting("ProcessModel", "Single");
            package.AddSetting("DomainUsage", "None");

            using var runner = engine.GetRunner(package);

            XmlNode testResult = runner.Run(new ConsoleEventListener(), TestFilter.Empty);

            Console.WriteLine(testResult.OuterXml);

            response = "Done.";
            return true;
        }
    }
}
