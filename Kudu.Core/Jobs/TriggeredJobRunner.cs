using System.IO;
using System.IO.Abstractions;
using System.Threading;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Hooks;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public class TriggeredJobRunner : BaseJobRunner
    {
        private readonly LockFile _lockFile;

        public TriggeredJobRunner(string jobName, IEnvironment environment, IFileSystem fileSystem, IDeploymentSettingsManager settings, ITraceFactory traceFactory)
            : base(jobName, Constants.TriggeredPath, environment, fileSystem, settings, traceFactory)
        {
            _lockFile = new LockFile(Path.Combine(JobDataPath, "triggeredJob.lock"), TraceFactory, FileSystem);
        }

        protected override string JobEnvironmentKeyPrefix
        {
            get { return "WEBSITE_TRIGGERED_JOB_RUNNING_"; }
        }

        public void StartJobRun(TriggeredJob triggeredJob, ITracer tracer)
        {
            if (!_lockFile.Lock())
            {
                throw new ConflictException();
            }

            try
            {
                InitializeJobInstance(triggeredJob, tracer);

                // TODO: Use actual async code
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        RunJobInstance(triggeredJob, tracer);
                    }
                    finally
                    {
                        _lockFile.Release();
                    }
                });
            }
            catch
            {
                _lockFile.Release();
                throw;
            }
        }
    }
}

