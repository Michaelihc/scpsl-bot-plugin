using NUnit;
using NUnit.Engine;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace SCPSLTests.Runner.Listeners
{
    internal class ConsoleEventListener : ITestEventListener
    {
        public void OnTestEvent(string report)
        {
            var doc = new XmlDocument();
            doc.LoadXml(report);

            var testEvent = doc.FirstChild;
            switch (testEvent.Name)
            {
                case "start-test":
                    TestStarted(testEvent);
                    break;

                case "test-case":
                    TestFinished(testEvent);
                    break;

                case "test-suite":
                    SuiteFinished(testEvent);
                    break;

                case "test-output":
                    TestOutput(testEvent);
                    break;
            }
        }

        private void TestStarted(XmlNode testResult)
        {
            //var testName = testResult.Attributes["fullname"].Value;

            //Log.Info($"Started: {testName}");
        }

        private void TestFinished(XmlNode testResult)
        {
            var testName = testResult.Attributes["fullname"].Value;
            var status = testResult.GetAttribute("label") ?? testResult.GetAttribute("result");
            var outputNode = testResult.SelectSingleNode("output");

            if (outputNode != null)
            {
                Log.Info(outputNode.InnerText);
            }

            switch (status)
            {
                case "Passed":
                    Log.Info($"{testName} - {status}"); break;
                case "Failed":
                case "Error":
                case "Invalid":
                    Log.Error($"{testName} - {status}"); break;
                case "Cancelled":
                case "Warning":
                case "Ignored":
                    Log.Warning($"{testName} - {status}"); break;
            }
        }

        private void SuiteFinished(XmlNode testResult)
        {
            var suiteName = testResult.Attributes["fullname"].Value;
            var outputNode = testResult.SelectSingleNode("output");

            Log.Info($"Suite: {suiteName}");
            if (outputNode != null)
            {
                Log.Info(outputNode.InnerText);
            }
        }

        private void TestOutput(XmlNode outputNode)
        {
            var testName = outputNode.GetAttribute("testname");
            var stream = outputNode.GetAttribute("stream");

            if (testName != null)
                Log.Info($"Output: {testName}");

            switch (stream)
            {
                case "Error": Log.Error(outputNode.InnerText); break;
                default: Log.Info(outputNode.InnerText); break;
            }
        }

    }
}
