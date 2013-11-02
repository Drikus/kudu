using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public class TriggeredJobsManager : JobsManagerBase<TriggeredJob>, ITriggeredJobsManager
    {
        private readonly ConcurrentDictionary<string, TriggeredJobRunner> _triggeredJobRunners =
            new ConcurrentDictionary<string, TriggeredJobRunner>(StringComparer.OrdinalIgnoreCase);

        public TriggeredJobsManager(ITraceFactory traceFactory, IEnvironment environment, IFileSystem fileSystem, IDeploymentSettingsManager settings)
            : base(traceFactory, environment, fileSystem, settings, Constants.TriggeredPath)
        {
        }

        public override IEnumerable<TriggeredJob> ListJobs()
        {
            return ListJobsInternal();
        }

        public override TriggeredJob GetJob(string jobName)
        {
            return GetJobInternal(jobName);
        }

        public void InvokeTriggeredJob(string jobName)
        {
            ITracer tracer = TraceFactory.GetTracer();
            using (tracer.Step("jobsManager.InvokeTriggeredJob"))
            {
                TriggeredJob triggeredJob = GetJob(jobName);
                if (triggeredJob == null)
                {
                    // TODO: Create specific exception
                    throw new FileNotFoundException();
                }

                TriggeredJobRunner triggeredJobRunner =
                    _triggeredJobRunners.GetOrAdd(
                        jobName,
                        _ => new TriggeredJobRunner(triggeredJob.Name, Environment, FileSystem, Settings, TraceFactory));

                triggeredJobRunner.StartJobRun(triggeredJob, tracer);
            }
        }
    }
}