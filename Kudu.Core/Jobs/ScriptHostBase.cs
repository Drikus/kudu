using System;
using System.Collections.Generic;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;

namespace Kudu.Core.Jobs
{
    public abstract class ScriptHostBase : IScriptHost
    {
        private const string JobEnvironmentKey = "WEBSITE_JOB_RUNNING";

        protected ScriptHostBase(string hostPath, string argumentsFormat = "{0}")
        {
            HostPath = hostPath;
            ArgumentsFormat = argumentsFormat;
        }

        public string HostPath { get; private set; }

        public string ArgumentsFormat { get; private set; }

        public virtual bool IsSupported
        {
            get { return !string.IsNullOrEmpty(HostPath); }
        }

        public abstract IEnumerable<string> SupportedExtensions { get; }

        public void RunScript(ITracer tracer, string scriptFileName, string workingDirectory, Action<string> onWriteOutput, Action<string> onWriteError, TimeSpan timeout)
        {
            using (tracer.Step("Run script '{0}' with script host - '{1}'".FormatCurrentCulture(scriptFileName, GetType())))
            {
                try
                {
                    var exe = new Executable(HostPath, workingDirectory, TimeSpan.MaxValue);
                    exe.EnvironmentVariables[JobEnvironmentKey] = "true";
                    exe.ExecuteWithoutIdleManager(tracer, onWriteOutput, onWriteError, timeout, ArgumentsFormat, scriptFileName);
                }
                catch (Exception ex)
                {
                    tracer.TraceError(ex);
                }
            }
        }
    }
}