using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;

namespace IpaDesktop
{
    /// <summary>Запускает CLI вне UI-потока, ограничивает вывод и обеспечивает прерываемое ожидание процесса.</summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        readonly string Downloads;
        public Action<string> Log
        {
            get;
            set;
        }
        public Process Active
        {
            get;
            private set;
        }
        public ProcessRunner(string workingDirectory)
        {
            Downloads = workingDirectory;
            Log = delegate
            {
            };
        }
        // Windows CommandLineToArgvW-compatible quoting; never invoke cmd.exe.
        /// <summary>Экранирует один аргумент по правилам Windows, включая кавычки и завершающие обратные слеши.</summary>
        public static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            int n = 0;
            foreach (char c in s)
            {
                if (c == '\\')
                {
                    n++;
                    continue;
                }
                if (c == '\"')
                {
                    b.Append('\\', n * 2 + 1);
                    b.Append(c);
                } else
                {
                    b.Append('\\', n);
                    b.Append(c);
                }
                n = 0;
            }
            b.Append('\\', n * 2);
            return b.Append('"').ToString();
        }
        /// <summary>Запускает процесс в фоне; отмена завершает дочерний процесс, а ожидание и чтение вывода ограничены.</summary>
        public ProcessRunner CreateIsolated() => new ProcessRunner(Downloads);
        public Task<Result> Run(string exe, string[] args, CancellationToken token, bool visible, bool publish, string workingDirectory = null)
            => RunMonitored(exe,args,token,visible,publish,workingDirectory,null);
        public Task<Result> RunMonitored(string exe, string[] args, CancellationToken token, bool visible, bool publish, string workingDirectory, ProcessRunPolicy policy)
        {
            return Task.Run(delegate
            {
                token.ThrowIfCancellationRequested();
                var si = new ProcessStartInfo(exe, String.Join(" ", args.Select(Quote)))
                {
                    WorkingDirectory = workingDirectory ?? Downloads, UseShellExecute = false, CreateNoWindow = !visible
                };
                si.EnvironmentVariables["PATH"] = Path.GetDirectoryName(exe) + ";" + Environment.GetEnvironmentVariable("PATH");
                if (!visible)
                {
                    si.RedirectStandardInput = true;
                    si.RedirectStandardOutput = true;
                    si.RedirectStandardError = true;
                    si.StandardOutputEncoding = Encoding.UTF8;
                    si.StandardErrorEncoding = Encoding.UTF8;
                }
                using (var p = new Process
                {
                    StartInfo = si
                })
                {
                    var output = new StringBuilder();
                    object gate = new object();
                    bool stdoutDone = visible, stderrDone = visible;
                    long lastOutput = Stopwatch.GetTimestamp();
                    string lastProgressMessage = null, lastActivityMessage = null;
                    Exception reportedFailure = null;
                    DataReceivedEventHandler stdout = delegate(object sender, DataReceivedEventArgs e)
                    {
                        lock (gate)
                        {
                            if (e.Data == null)
                            {
                                stdoutDone = true;
                                return;
                            }
                            if (e.Data.StartsWith("PROGRESS:", StringComparison.Ordinal))
                            {
                                if (e.Data == lastProgressMessage) return;
                                lastProgressMessage = e.Data;
                            }
                            if(e.Data != lastActivityMessage) lastOutput = Stopwatch.GetTimestamp();
                            lastActivityMessage=e.Data;
                            reportedFailure ??= policy?.ReadFailure?.Invoke(e.Data);
                            int count = Math.Min(e.Data.Length, 15000000 - output.Length);
                            if (count > 0)
                            {
                                output.Append(e.Data, 0, count);
                                if (output.Length<15000000) output.AppendLine();
                            }
                        }
                        if (publish) Log(e.Data);
                    };
                    DataReceivedEventHandler stderr = delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null)
                        {
                            lock (gate)
                            {
                                stderrDone = true;
                            }
                            return;
                        }
                        stdout(sender, e);
                    };
                    if (!visible)
                    {
                        p.OutputDataReceived += stdout;
                        p.ErrorDataReceived += stderr;
                    }
                    var elapsed = Stopwatch.StartNew();
                    long nextNotice = 30;
                    try
                    {
                        p.Start();
                        Active = p;
                        if (!visible)
                        {
                            p.StandardInput.Close();
                            p.BeginOutputReadLine();
                            p.BeginErrorReadLine();
                        }
                        while (!p.WaitForExit(200))
                        {
                            if (token.IsCancellationRequested)
                            {
                                StopProcess(p);
                                token.ThrowIfCancellationRequested();
                            }
                            Exception failure; long activity;
                            lock(gate) { failure=reportedFailure; activity=lastOutput; }
                            if(failure != null) { StopProcess(p); throw failure; }
                            if(policy != null && (Stopwatch.GetTimestamp()-activity)/(double)Stopwatch.Frequency >= policy.IdleTimeout.TotalSeconds)
                            { StopProcess(p); throw policy.IdleFailure(); }
                            if (publish && elapsed.Elapsed.TotalSeconds>= nextNotice)
                            {
                                long last;
                                lock (gate)
                                {
                                    last = lastOutput;
                                }
                                Log("Процесс работает " + (int) elapsed.Elapsed.TotalSeconds + " с; без нового вывода " + (int)((Stopwatch.GetTimestamp() - last) / (double) Stopwatch.Frequency) + " с. При необходимости нажмите «Отмена».");
                                nextNotice += 30;
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        // A child may keep a redirected pipe open after its parent exits.
                        // Never use an unbounded WaitForExit() to drain those pipes.
                        var drain = Stopwatch.StartNew();
                        while (drain.ElapsedMilliseconds < 2000)
                        {
                            lock (gate)
                            {
                                if (stdoutDone && stderrDone) break;
                            }
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(20);
                        }
                        lock (gate)
                        {
                            if (!stdoutDone || !stderrDone) Log("Процесс завершился, но поток вывода не закрылся. Ожидание потока прекращено.");
                            if(reportedFailure != null) throw reportedFailure;
                            return new Result
                            {
                                Code = p.ExitCode, Output = output.ToString()
                            };
                        }
                    } finally
                    {
                        Active = null;
                        // Exceptions must not leave the native installer holding the device session.
                        try { if(!p.HasExited) StopProcess(p); } catch(InvalidOperationException) { }
                    }
                }
            }, token);
        }
        static void StopProcess(Process process)
        {
            try { process.Kill(true); }
            catch(InvalidOperationException) { return; }
            catch(System.ComponentModel.Win32Exception) { if(!process.HasExited) process.Kill(); }
            if(!process.WaitForExit(5000)) throw new IOException("Не удалось остановить утилиту. Завершите её через Диспетчер задач и повторите проверку связи.");
        }
    }
}
