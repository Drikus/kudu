using Kudu.Contracts.Settings;

namespace Kudu.Core.Deployment.Generator
{
    /// <summary>
    /// Console worker consisting of files and a command line to run (as the worker)
    /// </summary>
    public class BasicConsoleBuilder : BaseBasicBuilder
    {
        private const string Argument = "--basicConsole";

        public BasicConsoleBuilder(IEnvironment environment, IDeploymentSettingsManager settings, IBuildPropertyProvider propertyProvider, string sourcePath, string projectPath)
            : base(environment, settings, propertyProvider, sourcePath, projectPath, "--basicConsole")
        {
        }

        public override string ProjectType
        {
            get { return "BASIC CONSOLE WORKER"; }
        }
    }
}