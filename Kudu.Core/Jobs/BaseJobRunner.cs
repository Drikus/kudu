using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public abstract class BaseJobRunner
    {
        protected IEnvironment Environment { get; private set; }
        protected IFileSystem FileSystem { get; private set; }
        protected ITraceFactory TraceFactory { get; private set; }

        protected string JobName { get; private set; }
        protected string JobBinariesPath { get; private set; }
        protected string TempJobPath { get; private set; }
        protected string DataPath { get; private set; }

        protected string WorkingDirectory { get; private set; }

        protected BaseJobRunner(string jobName, string jobBinariesPath, IEnvironment environment, IFileSystem fileSystem, ITraceFactory traceFactory)
        {
            TraceFactory = traceFactory;
            Environment = environment;
            FileSystem = fileSystem;
            JobName = jobName;
            JobBinariesPath = jobBinariesPath;

            TempJobPath = Path.Combine(Environment.TempPath, Constants.AlwaysOnPath, jobName);
            DataPath = Path.Combine(Environment.AlwaysOnJobsDataPath, jobName);
        }

        private int CalculateHashForJob(string jobBinariesPath)
        {
            var updateDatesString = new StringBuilder();
            DirectoryInfoBase jobBinariesDirectory = FileSystem.DirectoryInfo.FromDirectoryName(jobBinariesPath);
            FileInfoBase[] files = jobBinariesDirectory.GetFiles("*.*", SearchOption.AllDirectories);
            foreach (FileInfoBase file in files)
            {
                updateDatesString.Append(file.LastWriteTimeUtc.Ticks);
            }

            return updateDatesString.ToString().GetHashCode();
        }

        private void CacheJobBinaries(ITracer tracer)
        {
            if (WorkingDirectory != null)
            {
                int currentHash = CalculateHashForJob(JobBinariesPath);
                int lastHash = CalculateHashForJob(WorkingDirectory);

                if (lastHash == currentHash)
                {
                    return;
                }
            }

            SafeKillAllRunningJobInstances(tracer);

            if (FileSystem.Directory.Exists(TempJobPath))
            {
                FileSystemHelpers.DeleteDirectoryContentsSafe(TempJobPath, true);
            }

            if (FileSystem.Directory.Exists(TempJobPath))
            {
                tracer.TraceWarning("Failed to delete temporary directory");
            }

            try
            {
                var tempJobInstancePath = Path.Combine(TempJobPath, Path.GetRandomFileName());

                FileSystemHelpers.CopyDirectoryRecursive(FileSystem, JobBinariesPath, tempJobInstancePath);

                WorkingDirectory = tempJobInstancePath;
            }
            catch (Exception ex)
            {
                //Status = "Worker is not running due to an error";
                //TraceError("Failed to copy bin directory: " + ex);
                tracer.TraceError("Failed to copy job files: " + ex);

                // job disabled
                WorkingDirectory = null;
            }
        }

        public string GetJobEnvironmentKey()
        {
            return JobEnvironmentKeyPrefix + JobName;
        }

        protected abstract string JobEnvironmentKeyPrefix { get; }

        protected void InitializeJobInstance(JobBase job, ITracer tracer)
        {
            // TODO: Use actual async code
            if (!String.Equals(JobName, job.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The job runner can only run jobs with the same name it was configured, configured - {0}, trying to run - {1}".FormatInvariant(
                        JobName, job.Name));
            }

            if (!FileSystem.File.Exists(job.ScriptFilePath))
            {
                //Status = "Missing run_worker.cmd file";
                //Trace.TraceError(Status);
                throw new InvalidOperationException("Missing job script to run - {0}".FormatInvariant(job.ScriptFilePath));
            }

            CacheJobBinaries(tracer);

            if (WorkingDirectory == null)
            {
                throw new InvalidOperationException("Missing working directory");
            }
        }

        protected void RunJobInstance(JobBase job, ITracer tracer)
        {
            // TODO: Use actual async code
            string scriptFileName = Path.GetFileName(job.ScriptFilePath);

            using (tracer.Step("Run script '{0}' with script host - '{1}'".FormatCurrentCulture(scriptFileName, job.ScriptHost.GetType())))
            {
                try
                {
                    var exe = new Executable(job.ScriptHost.HostPath, WorkingDirectory, TimeSpan.MaxValue);

                    // Set environment variable to be able to identify all processes spawned for this job
                    exe.EnvironmentVariables[GetJobEnvironmentKey()] = "true";

                    exe.ExecuteWithoutIdleManager(tracer, (message) => tracer.Trace(message), tracer.TraceError, TimeSpan.MaxValue,
                                                  job.ScriptHost.ArgumentsFormat, scriptFileName);
                }
                catch (Exception ex)
                {
                    tracer.TraceError(ex);
                }
            }
        }

        public void SafeKillAllRunningJobInstances(ITracer tracer)
        {
            try
            {
                Process[] processes = Process.GetProcesses();

                foreach (Process process in processes)
                {
                    StringDictionary processEnvironment = ProcessEnvironment.TryGetEnvironmentVariables(process);
                    if (processEnvironment != null && processEnvironment.ContainsKey(GetJobEnvironmentKey()))
                    {
                        try
                        {
                            process.Kill();
                        }

                        catch (Exception ex)
                        {
                            if (!process.HasExited)
                            {
                                tracer.TraceError("Failed to kill process - {0} for job - {1}\n{2}".FormatInvariant(process.ProcessName, JobName, ex));
                            }
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                tracer.TraceError(ex);
            }
        }
    }
}