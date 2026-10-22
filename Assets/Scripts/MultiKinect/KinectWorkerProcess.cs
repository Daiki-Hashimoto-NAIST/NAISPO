using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Naispo.MultiKinect
{
    // Owns only its child process. A restart waits for that child's shutdown first.
    public sealed class KinectWorkerProcess
    {
        readonly object gate = new object();
        Process process;
        StreamWriter log;
        Task stopping = Task.CompletedTask;
        public string LastLine { get; private set; } = "Not started";
        public int Id { get; private set; }
        public bool Running
        {
            get { lock (gate) { try { return process != null && !process.HasExited; } catch { return false; } } }
        }
        public bool IsStopping { get { lock (gate) return !stopping.IsCompleted; } }
        public int? ExitCode
        {
            get { lock (gate) { try { return process != null && process.HasExited ? process.ExitCode : (int?)null; } catch { return null; } } }
        }

        public void Start(string executable, string config, string logPath)
        {
            lock (gate)
            {
                if (Running || !stopping.IsCompleted) throw new InvalidOperationException("Previous worker is still stopping");
                if (!File.Exists(executable)) throw new FileNotFoundException("Build Tools/KinectWorker first", executable);
                process?.Dispose();
                log?.Dispose();
                log = new StreamWriter(logPath, false) { AutoFlush = true };
                process = new Process
                {
                    StartInfo = new ProcessStartInfo(executable, "--config \"" + config + "\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(executable)
                    }
                };
                process.OutputDataReceived += (s, e) => Line(e.Data);
                process.ErrorDataReceived += (s, e) => Line(e.Data);
                try
                {
                    process.Start(); Id = process.Id;
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    LastLine = "Started PID " + Id;
                }
                catch
                {
                    process.Dispose(); process = null;
                    log.Dispose(); log = null;
                    throw;
                }
            }
        }

        void Line(string value)
        {
            if (value == null) return;
            lock (gate)
            {
                if (!value.StartsWith("   at ")) LastLine = value;
                log?.WriteLine(value);
            }
        }

        public Task StopAsync()
        {
            lock (gate)
            {
                if (!stopping.IsCompleted) return stopping;
                var owned = process;
                if (owned == null) return Task.CompletedTask;
                stopping = Task.Run(() =>
                {
                    try
                    {
                        if (!owned.HasExited)
                        {
                            try { owned.StandardInput.WriteLine("stop"); owned.StandardInput.Flush(); }
                            catch (IOException) { }
                            if (!owned.WaitForExit(5000))
                            {
                                owned.Kill();
                                if (!owned.WaitForExit(2000)) throw new TimeoutException("Worker did not terminate");
                            }
                        }
                        // Drain redirected output before this worker's log is closed.
                        owned.WaitForExit();
                    }
                    catch (Exception e) { LastLine = "Worker shutdown: " + e.Message; }
                    finally
                    {
                        lock (gate)
                        {
                            owned.Dispose();
                            if (process == owned) process = null;
                            log?.Dispose(); log = null;
                            Id = 0;
                        }
                    }
                });
                return stopping;
            }
        }
    }
}
