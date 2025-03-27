using CommandSystem;
using NUnit;
using NUnit.Engine;
using NUnit.Engine.Internal;
using PluginAPI.Core;
using System;
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

            ITestEngine engine = TestEngineActivator.CreateInstance();
            TestPackage package = new (assemblyFileName);
            package.AddSetting("ProcessModel", "Single");
            package.AddSetting("DomainUsage", "None");

            ITestRunner runner = engine.GetRunner(package);

            XmlNode testResult = runner.Run(listener: null, TestFilter.Empty);

            Console.WriteLine(testResult.OuterXml);

            response = "Done.";
            return true;
        }
    }
}
