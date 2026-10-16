using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace AppRouter;

/// <summary>
/// Servicio de Windows ("AppRouter.exe --service") que mantiene sing-box en marcha:
/// lo arranca, lo relanza si se cae y lo detiene con el servicio. sing-box.exe no sabe
/// funcionar como servicio por sí mismo, así que no hace falta NSSM ni WinSW.
/// </summary>
public sealed class SingBoxHostService : ServiceBase
{
    const long MaxLogBytes = 5 * 1024 * 1024;

    readonly object gate = new();
    Process? child;
    StreamWriter? log;
    volatile bool stopping;
    IntPtr job;

    public SingBoxHostService()
    {
        ServiceName = Paths.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        // Job object: si este proceso muere, Windows mata también sing-box (no queda el túnel huérfano).
        job = Native.CreateKillOnCloseJob();
        stopping = false;
        StartChild();
    }

    protected override void OnStop() => StopChild();
    protected override void OnShutdown() => StopChild();

    void StartChild()
    {
        lock (gate)
        {
            if (stopping) return;
            OpenLog();
            Log($"--- Arrancando sing-box {Paths.SingBoxVersion} ---");

            var psi = new ProcessStartInfo(Paths.SingBoxExe)
            {
                ArgumentList = { "run", "-c", Paths.ConfigPath, "-D", Paths.DataDir },
                WorkingDirectory = Paths.DataDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
            p.Exited += OnChildExited;
            p.Start();
            Native.AssignToJob(job, p);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            child = p;
        }
    }

    void OnChildExited(object? sender, EventArgs e)
    {
        var code = (sender as Process)?.ExitCode;
        Log($"--- sing-box terminó (código {code}) ---");
        if (stopping) return;
        // Relanzar tras una pausa (p. ej. adaptador desconectado o config inválido).
        Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => { try { StartChild(); } catch (Exception ex) { Log("Error al relanzar: " + ex.Message); } });
    }

    void StopChild()
    {
        stopping = true;
        Process? p;
        lock (gate) { p = child; child = null; }
        try
        {
            if (p is { HasExited: false })
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(10000);
            }
        }
        catch (Exception ex) { Log("Error al detener: " + ex.Message); }
        lock (gate) { log?.Dispose(); log = null; }
    }

    void OpenLog()
    {
        if (log is not null) return;
        Directory.CreateDirectory(Paths.DataDir);
        var fi = new FileInfo(Paths.LogPath);
        if (fi.Exists && fi.Length > MaxLogBytes) File.Move(Paths.LogPath, Paths.LogPath + ".old", overwrite: true);
        log = new StreamWriter(new FileStream(Paths.LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        { AutoFlush = true };
    }

    void Log(string line)
    {
        lock (gate) { try { log?.WriteLine(line); } catch { /* el log nunca debe tumbar el servicio */ } }
    }

    static class Native
    {
        const int JobObjectExtendedLimitInformation = 9;
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        struct BASIC_LIMIT
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct EXTENDED_LIMIT
        {
            public BASIC_LIMIT Basic;
            public IO_COUNTERS Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attrs, string? name);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref EXTENDED_LIMIT info, int length);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        public static IntPtr CreateKillOnCloseJob()
        {
            var h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) throw new Win32Exception();
            var info = new EXTENDED_LIMIT { Basic = { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
            if (!SetInformationJobObject(h, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<EXTENDED_LIMIT>()))
                throw new Win32Exception();
            return h;
        }

        public static void AssignToJob(IntPtr job, Process p) => AssignProcessToJobObject(job, p.Handle);
    }
}
