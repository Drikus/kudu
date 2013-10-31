using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public class TriggeredJobRunner : BaseJobRunner
    {
        private readonly LockFile _lockFile;

        public TriggeredJobRunner(string jobName, string jobBinariesPath, IEnvironment environment, IFileSystem fileSystem, ITraceFactory traceFactory)
            : base(jobName, jobBinariesPath, environment, fileSystem, traceFactory)
        {
            _lockFile = new LockFile(Path.Combine(DataPath, "triggeredJob.lock"), TraceFactory, FileSystem);
        }

        protected override string JobEnvironmentKeyPrefix
        {
            get { return "WEBSITE_TRIGGERED_JOB_RUNNING_"; }
        }

        public Task RunJobInstanceAsync(TriggeredJob triggeredJob, ITracer tracer)
        {
            if (!_lockFile.Lock())
            {
                throw new InvalidOperationException("Job {0} is already running".FormatInvariant(JobName));
            }

            try
            {
                InitializeJobInstance(triggeredJob, tracer);

                // TODO: Use actual async code
                return Task.Factory.StartNew(() =>
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

