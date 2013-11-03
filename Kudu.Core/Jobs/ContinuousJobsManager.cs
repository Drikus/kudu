using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Settings;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public class ContinuousJobsManager : JobsManagerBase<ContinuousJob>, IContinuousJobsManager
    {
        private readonly Dictionary<string, ContinuousJobRunner> _continuousJobRunners = new Dictionary<string, ContinuousJobRunner>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> _updatedJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Timer _makeChangesTimer;
        private readonly Timer _startFileWatcherTimer;
        private readonly object _lockObject = new object();

        private FileSystemWatcher _fileSystemWatcher;

        private bool _makingChanges;

        public ContinuousJobsManager(ITraceFactory traceFactory, IEnvironment environment, IFileSystem fileSystem, IDeploymentSettingsManager settings)
            : base(traceFactory, environment, fileSystem, settings, Constants.ContinuousPath)
        {
            foreach (ContinuousJob continuousJob in ListJobs())
            {
                UpdateJob(continuousJob);
            }

            _makeChangesTimer = new Timer(OnMakeChanges);
            _startFileWatcherTimer = new Timer(StartWatcher);
            _startFileWatcherTimer.Change(0, Timeout.Infinite);
        }

        public override IEnumerable<ContinuousJob> ListJobs()
        {
            return ListJobsInternal();
        }

        public override ContinuousJob GetJob(string jobName)
        {
            return GetJobInternal(jobName);
        }

        private void OnMakeChanges(object state)
        {
            HashSet<string> updatedJobs;

            lock (_lockObject)
            {
                if (_makingChanges)
                {
                    _makeChangesTimer.Change(5000, Timeout.Infinite);
                }

                _makingChanges = true;

                _makeChangesTimer.Change(Timeout.Infinite, Timeout.Infinite);

                updatedJobs = _updatedJobs;
                _updatedJobs = new HashSet<string>();
            }

            foreach (string updatedJobName in updatedJobs)
            {
                ContinuousJob continuousJob = GetJob(updatedJobName);
                if (continuousJob == null)
                {
                    RemoveJob(updatedJobName);
                }
                else
                {
                    UpdateJob(continuousJob);
                }
            }
        }

        private void UpdateJob(ContinuousJob continuousJob)
        {
            ContinuousJobRunner continuousJobRunner;
            if (!_continuousJobRunners.TryGetValue(continuousJob.Name, out continuousJobRunner))
            {
                continuousJobRunner = new ContinuousJobRunner(continuousJob.Name, Environment, FileSystem, Settings, TraceFactory);
            }

            continuousJobRunner.Refresh(continuousJob);
        }

        private void RemoveJob(string updatedJobName)
        {
            ContinuousJobRunner continuousJobRunner;
            if (!_continuousJobRunners.TryGetValue(updatedJobName, out continuousJobRunner))
            {
                return;
            }

            continuousJobRunner.Stop();

            _continuousJobRunners.Remove(updatedJobName);
        }

        private void StartWatcher(object state)
        {
            if (!FileSystem.Directory.Exists(JobsBinariesPath))
            {
                _startFileWatcherTimer.Change(30 * 1000, Timeout.Infinite);
                return;
            }

            _fileSystemWatcher = new FileSystemWatcher(JobsBinariesPath);
            _fileSystemWatcher.Changed += OnChanged;
            _fileSystemWatcher.Deleted += OnChanged;
            _fileSystemWatcher.Renamed += OnChanged;
            //_fileSystemWatcher.Error += new ErrorEventHandler(DoSafeAction<object, ErrorEventArgs>(OnError, "LogStreamManager.OnError"));
            _fileSystemWatcher.IncludeSubdirectories = true;
            _fileSystemWatcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            string path = e.FullPath;
            if (path != null && path.Length > JobsBinariesPath.Length)
            {
                path = path.Substring(JobsBinariesPath.Length).TrimStart(Path.DirectorySeparatorChar);
                int firstSeparator = path.IndexOf(Path.DirectorySeparatorChar);
                if (firstSeparator > 0)
                {
                    string jobName = path.Substring(0, firstSeparator);
                    MarkJobUpdated(jobName);
                }
            }
        }

        private void MarkJobUpdated(string jobName)
        {
            _updatedJobs.Add(jobName);
            _makeChangesTimer.Change(5000, Timeout.Infinite);
        }
    }
}