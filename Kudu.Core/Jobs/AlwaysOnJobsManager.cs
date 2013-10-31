using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using Kudu.Contracts.Jobs;
using Kudu.Core.Tracing;

namespace Kudu.Core.Jobs
{
    public class AlwaysOnJobsManager : JobsManagerBase<AlwaysOnJob>, IAlwaysOnJobsManager
    {
        private readonly Dictionary<string, AlwaysOnJobRunner> _alwaysOnJobRunners = new Dictionary<string, AlwaysOnJobRunner>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> _updatedJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Timer _makeChangesTimer;
        private readonly Timer _startFileWatcherTimer;
        private readonly object _lockObject = new object();

        private FileSystemWatcher _fileSystemWatcher;

        private bool _makingChanges;

        public AlwaysOnJobsManager(ITraceFactory traceFactory, IEnvironment environment, IFileSystem fileSystem)
            : base(traceFactory, environment, fileSystem)
        {
            foreach (AlwaysOnJob alwaysOnJob in ListJobs())
            {
                UpdateJob(alwaysOnJob);
            }

            _makeChangesTimer = new Timer(OnMakeChanges);
            _startFileWatcherTimer = new Timer(StartWatcher);
            _startFileWatcherTimer.Change(0, Timeout.Infinite);
        }

        public override IEnumerable<AlwaysOnJob> ListJobs()
        {
            return ListJobs(Environment.AlwaysOnJobsPath);
        }

        public override AlwaysOnJob GetJob(string jobName)
        {
            return GetJob(jobName, Environment.AlwaysOnJobsPath);
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
                AlwaysOnJob alwaysOnJob = GetJob(updatedJobName);
                if (alwaysOnJob == null)
                {
                    RemoveJob(updatedJobName);
                }
                else
                {
                    UpdateJob(alwaysOnJob);
                }
            }
        }

        private void UpdateJob(AlwaysOnJob alwaysOnJob)
        {
            AlwaysOnJobRunner alwaysOnJobRunner;
            if (!_alwaysOnJobRunners.TryGetValue(alwaysOnJob.Name, out alwaysOnJobRunner))
            {
                alwaysOnJobRunner = new AlwaysOnJobRunner(alwaysOnJob.Name, alwaysOnJob.BinariesPath, Environment, FileSystem, TraceFactory);
            }

            alwaysOnJobRunner.Refresh(alwaysOnJob);
        }

        private void RemoveJob(string updatedJobName)
        {
            AlwaysOnJobRunner alwaysOnJobRunner;
            if (!_alwaysOnJobRunners.TryGetValue(updatedJobName, out alwaysOnJobRunner))
            {
                return;
            }

            alwaysOnJobRunner.Stop();

            _alwaysOnJobRunners.Remove(updatedJobName);
        }

        private void StartWatcher(object state)
        {
            if (!FileSystem.Directory.Exists(Environment.AlwaysOnJobsPath))
            {
                _startFileWatcherTimer.Change(30 * 1000, Timeout.Infinite);
                return;
            }

            _fileSystemWatcher = new FileSystemWatcher(Environment.AlwaysOnJobsPath);
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
            if (path != null && path.Length > Environment.AlwaysOnJobsPath.Length)
            {
                path = path.Substring(Environment.AlwaysOnJobsPath.Length);
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