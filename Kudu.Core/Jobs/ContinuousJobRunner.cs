﻿using System;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public class ContinuousJobRunner : BaseJobRunner
    {
        private int _started = 0;
        private Task _task;

        public ContinuousJobRunner(string jobName, IEnvironment environment, IFileSystem fileSystem, IDeploymentSettingsManager settings, ITraceFactory traceFactory)
            : base(jobName, Constants.ContinuousPath, environment, fileSystem, settings, traceFactory)
        {
        }

        protected override string JobEnvironmentKeyPrefix
        {
            get { return "WEBSITE_ALWAYS_ON_JOB_RUNNING_"; }
        }

        private void StartJobAsync(ContinuousJob continuousJob)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                return;
            }

            // TODO: Use actual async code
            _task = Task.Factory.StartNew(() =>
                {
                    ITracer tracer = TraceFactory.GetTracer();
                    using (tracer.Step("ContinuousJobRunner.StartJobAsync"))
                    {
                        while (_started == 1)
                        {
                            InitializeJobInstance(continuousJob, tracer);
                            RunJobInstance(continuousJob, tracer);

                            WaitForTimeOrStop(TimeSpan.FromSeconds(30));
                        }
                    }
                });
        }

        public void Stop()
        {
            ITracer tracer = TraceFactory.GetTracer();
            using (tracer.Step("ContinuousJobRunner.Stop"))
            {
                Interlocked.Exchange(ref _started, 0);
                SafeKillAllRunningJobInstances(tracer);
            }
        }

        public void Refresh(ContinuousJob continuousJob)
        {
            ITracer tracer = TraceFactory.GetTracer();
            using (tracer.Step("ContinuousJobRunner.Refresh"))
            {
                SafeKillAllRunningJobInstances(tracer);
                StartJobAsync(continuousJob);
            }
        }

        private void WaitForTimeOrStop(TimeSpan timeSpan)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeSpan && _started == 1)
            {
                Thread.Sleep(200);
            }
        }
    }
}