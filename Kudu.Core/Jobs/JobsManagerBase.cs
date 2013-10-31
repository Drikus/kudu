using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Kudu.Contracts.Jobs;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public abstract class JobsManagerBase<TJob> : IJobsManager<TJob> where TJob : JobBase, new()
    {
        private const string DefaultScriptFileName = "run";

        private static readonly ScriptHostBase[] ScriptHosts = new ScriptHostBase[]
            {
                new WindowsScriptHost(),
                new BashScriptHost(),
                new PythonScriptHost(),
                new PhpScriptHost(),
                new NodeScriptHost()
            };

        protected IEnvironment Environment { get; private set; }
        protected IFileSystem FileSystem { get; private set; }
        protected ITraceFactory TraceFactory { get; private set; }

        protected JobsManagerBase(ITraceFactory traceFactory, IEnvironment environment, IFileSystem fileSystem)
        {
            TraceFactory = traceFactory;
            Environment = environment;
            FileSystem = fileSystem;
        }

        public abstract IEnumerable<TJob> ListJobs();
        public abstract TJob GetJob(string jobName);

        protected TJob GetJob(string jobName, IEnumerable<string> jobsPaths)
        {
            IEnumerable<TJob> jobs = ListJobs(jobsPaths, jobName);
            int jobsCount = jobs.Count();
            if (jobsCount == 0)
            {
                return null;
            }
            else if (jobsCount > 1)
            {
                // TODO: fix error
                throw new Exception("Duplicate error");
            }

            return jobs.First();
        }

        protected IEnumerable<TJob> ListJobs(IEnumerable<string> jobsPaths, string searchPattern = "*")
        {
            var jobs = new List<TJob>();

            foreach (string jobsPath in jobsPaths)
            {
                if (!FileSystem.Directory.Exists(jobsPath))
                {
                    continue;
                }

                DirectoryInfoBase jobsDirectory = FileSystem.DirectoryInfo.FromDirectoryName(jobsPath);
                DirectoryInfoBase[] jobDirectories = jobsDirectory.GetDirectories(searchPattern, SearchOption.TopDirectoryOnly);
                foreach (DirectoryInfoBase jobDirectory in jobDirectories)
                {
                    TJob job = BuildJob(jobDirectory);
                    if (job != null)
                    {
                        jobs.Add(job);
                    }
                }
            }

            return jobs;
        }

        protected TJob BuildJob(DirectoryInfoBase jobDirectory)
        {
            string jobName = jobDirectory.Name;
            FileInfoBase[] files = jobDirectory.GetFiles("*.*", SearchOption.TopDirectoryOnly);
            IScriptHost scriptHost;
            string runCommand = FindCommandToRun(files, out scriptHost);

            if (runCommand == null)
            {
                return null;
            }

            return new TJob()
                {
                    Name = jobName,
                    ScriptFilePath = runCommand,
                    BinariesPath = jobDirectory.FullName,
                    ScriptHost = scriptHost
                };
        }

        private string FindCommandToRun(FileInfoBase[] files, out IScriptHost scriptHostFound)
        {
            foreach (ScriptHostBase scriptHost in ScriptHosts)
            {
                foreach (string supportedExtension in scriptHost.SupportedExtensions)
                {
                    var supportedFiles = files.Where(f => String.Equals(f.Extension, supportedExtension, StringComparison.OrdinalIgnoreCase));
                    if (supportedFiles.Any())
                    {
                        var scriptFound =
                            supportedFiles.FirstOrDefault(f => String.Equals(f.Name, DefaultScriptFileName, StringComparison.OrdinalIgnoreCase));
                        var supportedFile = scriptFound ?? supportedFiles.First();
                        scriptHostFound = scriptHost;
                        return supportedFile.FullName;
                    }
                }
            }

            scriptHostFound = null;

            return null;
        }
    }
}