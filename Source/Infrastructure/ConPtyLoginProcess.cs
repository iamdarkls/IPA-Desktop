using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace IpaDesktop
{
    /// <summary>ConPTY Windows 10 1809+: скрытый TTY для штатного ipatool, без shell и секретных аргументов.</summary>
    public sealed class ConPtyLoginFactory : ILoginProcessFactory
    {
        public ILoginProcess Start(string executable, string workingDirectory)
        {
            return new ConPtyLoginProcess(executable, workingDirectory);
        }
    }
    public sealed class ConPtyLoginProcess : ILoginProcess
    {
        readonly Queue<string> output = new Queue<string>();
        readonly object gate = new object();
        readonly SemaphoreSlim available = new SemaphoreSlim(0);
        FileStream input, reader;
        IntPtr console, process;
        Task readTask;
        bool ended, overflow;
        int disposed;

        public ConPtyLoginProcess(string executable, string workingDirectory)
        {
            IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero;
            IntPtr attributes = IntPtr.Zero;
            bool initialized = false;
            try
            {
                if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
                try
                {
                    int hr = CreatePseudoConsole(new Coord { X = 1200, Y = 60 }, inputRead, outputWrite, 0, out console);
                    if (hr != 0) throw new Win32Exception(hr);
                }
                catch (EntryPointNotFoundException) { throw new NotSupportedException("Вход в окне требует Windows 10 версии 1809 или новее. Обновите Windows."); }
                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == IntPtr.Zero) throw new Win32Exception();
                attributes = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
                initialized = true;
                if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x00020016), console, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                var start = new StartupEx(); start.Info.Size = Marshal.SizeOf(typeof(StartupEx)); start.Attributes = attributes;
                // При запуске из перенаправленного host (включая CI) Windows иначе копирует его pipe handles.
                // Явные нулевые std handles + STARTF_USESTDHANDLES дают ConPTY назначить собственный TTY.
                start.Info.Flags = 0x00000100;
                ProcessInfo info;
                // Только фиксированная команда. Адрес, пароль и код передаются позже в терминальный канал.
                if (!CreateProcess(executable, new StringBuilder(ProcessRunner.Quote(executable) + " auth login --format json"), IntPtr.Zero, IntPtr.Zero,
                    false, 0x00080000, IntPtr.Zero, workingDirectory, ref start, out info)) throw new Win32Exception();
                process = info.Process; CloseHandle(info.Thread);
                input = new FileStream(new SafeFileHandle(inputWrite, true), FileAccess.Write, 4096, false); inputWrite = IntPtr.Zero;
                reader = new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read, 4096, false); outputRead = IntPtr.Zero;
                readTask = Task.Run((Action)ReadOutput);
            }
            catch { Dispose(); throw; }
            finally
            {
                if (initialized) DeleteProcThreadAttributeList(attributes);
                if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                foreach (var handle in new[] { inputRead, inputWrite, outputRead, outputWrite }) if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }
        // Чтение не останавливается при отмене: ClosePseudoConsole требует дренировать выходной канал.
        void ReadOutput()
        {
            try
            {
                using (var text = new StreamReader(reader, Encoding.UTF8, false, 1024, true))
                {
                    var chars = new char[1024]; int count;
                    while ((count = text.Read(chars, 0, chars.Length)) > 0)
                    {
                        lock (gate)
                        {
                            if (disposed != 0 || overflow) continue;
                            if (output.Count >= 128) { overflow = true; output.Clear(); }
                            else output.Enqueue(new string(chars, 0, count));
                            available.Release();
                        }
                        Array.Clear(chars, 0, chars.Length);
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { lock (gate) { ended = true; available.Release(); } }
        }
        public async Task<string> ReadAsync(CancellationToken ct)
        {
            var exitDrain = new System.Diagnostics.Stopwatch();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                lock (gate)
                {
                    if (overflow) throw new IOException("Превышен безопасный размер ответа входа.");
                    if (output.Count > 0) return output.Dequeue();
                    if (ended) return null;
                }
                if (WaitForSingleObject(process, 0) == 0)
                {
                    if (!exitDrain.IsRunning) exitDrain.Start();
                    if (exitDrain.ElapsedMilliseconds >= 700) return null;
                }
                await available.WaitAsync(100, ct).ConfigureAwait(false);
            }
        }
        /// <summary>Короткоживущие массивы очищаются; plaintext-строка пароля и аргумент -p не создаются.</summary>
        public Task SendAsync(SecureString value, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                ct.ThrowIfCancellationRequested();
                IntPtr pointer = IntPtr.Zero; char[] chars = null; byte[] bytes = null;
                try
                {
                    if (value == null || value.Length == 0 || value.Length > 1024) throw new ArgumentException("Проверьте введённые данные.");
                    pointer = Marshal.SecureStringToGlobalAllocUnicode(value);
                    chars = new char[value.Length + 1];
                    for (int i = 0; i < value.Length; i++)
                    {
                        chars[i] = (char)Marshal.ReadInt16(pointer, i * 2);
                        if (Char.IsControl(chars[i])) throw new ArgumentException("Ввод не должен содержать управляющие символы или переносы строк.");
                    }
                    chars[value.Length] = '\r'; bytes = Encoding.UTF8.GetBytes(chars);
                    ct.ThrowIfCancellationRequested(); input.Write(bytes, 0, bytes.Length); input.Flush();
                }
                finally
                {
                    if (pointer != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pointer);
                    if (chars != null) Array.Clear(chars, 0, chars.Length);
                    if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                }
            }, ct);
        }
        public int ExitCode
        {
            get { uint code; if (!GetExitCodeProcess(process, out code) || code == 259) throw new IOException("Процесс входа не завершён."); return (int)code; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (process != IntPtr.Zero)
            {
                if (WaitForSingleObject(process, 0) != 0) { TerminateProcess(process, 1); WaitForSingleObject(process, 2000); }
                CloseHandle(process); process = IntPtr.Zero;
            }
            if (input != null) input.Dispose();
            lock (gate) { output.Clear(); }
            var pc = console; console = IntPtr.Zero;
            // Сбои платформы не должны навечно блокировать окно. Читатель продолжает дренировать pipe.
            var close = Task.Run(delegate { if (pc != IntPtr.Zero) ClosePseudoConsole(pc); if (reader != null) reader.Dispose(); });
            close.Wait(3000);
            if (readTask != null) readTask.Wait(1000);
        }
        [StructLayout(LayoutKind.Sequential)] struct Coord { public short X, Y; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Startup
        {
            public int Size; public string Reserved, Desktop, Title;
            public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public short Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] struct StartupEx { public Startup Info; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr security, int size);
        [DllImport("kernel32.dll")] static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
        [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr console);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr result);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW", SetLastError = true)] static extern bool CreateProcess(string app, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupEx start, out ProcessInfo process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr handle, uint code);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    }
}
