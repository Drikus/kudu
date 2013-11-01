using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Microsoft.AspNet.SignalR;

namespace Kudu.Services
{
    public class PersistentCommandController : PersistentConnection, IDisposable
    {
        private readonly ITracer _tracer;
        private readonly IEnvironment _environment;
        static readonly IDictionary<string, Process> Processes = new Dictionary<string, Process>();
        private readonly object _syncLock = new object();
        private readonly BlockingCollection<Action> _actionsCollection = new BlockingCollection<Action>();
        private readonly CancellationTokenSource _cancellationToken = new CancellationTokenSource();


        public PersistentCommandController(IEnvironment environment, ITracer tracer)
        {
            _environment = environment;
            _tracer = tracer;
            
            var syncThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        var action = _actionsCollection.Take(_cancellationToken.Token);
                        action();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            });
            syncThread.Start();
        }

        protected override Task OnConnected(IRequest request, string connectionId)
        {
            var process = GetProcessForConnection(connectionId);
            var fmt = String.Format("Connected to {0} ({1})\n", process.ProcessName, process.Id);
            return Connection.Send(connectionId, fmt);
        }

        protected override Task OnDisconnected(IRequest request, string connectionId)
        {
            try
            {
                var process = GetProcessForConnection(connectionId, false);
                if (process == null) return base.OnDisconnected(request, connectionId);

                process.StandardInput.WriteLine("exit");
                process.StandardInput.Flush();
                Thread.Sleep(2000);
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception)
            {
                _tracer.TraceError(exception);
            }
            return base.OnDisconnected(request, connectionId);
        }

        protected override Task OnReceived(IRequest request, string connectionId, string data)
        {
            return Task.Factory.StartNew(() =>
            {
                var process = GetProcessForConnection(connectionId);
                process.StandardInput.WriteLine(data.Replace("\n", ""));
                process.StandardInput.Flush();
            });
        }

        Process GetProcessForConnection(string connectionId, bool createIfNotExist = true)
        {
            lock (Processes)
            {
                Process ret = null;
                if (!Processes.TryGetValue(connectionId, out ret) && createIfNotExist)
                {
                    ret = StartProcess(connectionId);
                    Processes.Add(connectionId, ret);
                }
                return ret;
            }
        }
        private Process StartProcess(string connectionId)
        {
            var startInfo = new ProcessStartInfo()
            {
                UseShellExecute = false,
                FileName = System.Environment.ExpandEnvironmentVariables(@"%SystemDrive%\Windows\System32\cmd.exe"),
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                WorkingDirectory = _environment.RootPath
            };

            var process = Process.Start(startInfo);
            process.EnableRaisingEvents = true;

            var outputReader = TextReader.Synchronized(process.StandardOutput);
            var outputThread = new Thread(() => ListenAndSendStream(process, outputReader, connectionId, false));
            outputThread.Start();

            var errorReader = TextReader.Synchronized(process.StandardError);
            var errorThread = new Thread(() => ListenAndSendStream(process, errorReader, connectionId, true));
            errorThread.Start();
            
            return process;
        }

        private void ListenAndSendStream(Process process, TextReader textReader, string connectionId, bool isError)
        {
            const int bufferSize = 4096;
            while (!process.HasExited)
            {
                int count;
                var buffer = new char[bufferSize];
                while ((count = textReader.Read(buffer, 0, bufferSize)) > 0)
                {
                    var builder = new StringBuilder();
                    builder.Append(buffer, 0, count);
                    var output = builder.ToString().Replace("\r\n", "\n");
                    if (!String.IsNullOrEmpty(output))
                        if (isError)
                        lock(Connection)
                        {
                            Connection.Send(connectionId, new {Error = output});
                            //_actionsCollection.Add(() =>
                            Task.Run(() =>
                            {
                                lock (_syncLock)
                                {
                                    Thread.Sleep(TimeSpan.FromMilliseconds(100));
                                }
                            });
                        }
                        else
                        lock(Connection)
                        {
                            lock (_syncLock)
                            {
                                Connection.Send(connectionId, new {Output = output});
                            }
                        }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool cleanupNative)
        {
            _cancellationToken.Cancel();
            _cancellationToken.Dispose();
            _actionsCollection.Dispose();
            foreach (var process in Processes)
            {
                try
                {
                    process.Value.Kill();
                }
                catch
                {
                }
            }
        }
    }
}
