using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using KnightsCleaner.Core;

namespace KnightsCleaner.App.Services;

public sealed record CleanerOption(string Name, string Description, string? Path, bool RequiresAdmin, bool Advanced);

public static class ScriptActions
{
    public static bool IsAdministrator =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static string WindowsFolder => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    public static IReadOnlyList<CleanerOption> Options() =>
    [
        new("User Temp", "Temporary files from your TEMP folder", Path.GetTempPath(), false, false),
        new("Windows Temp", "Temporary files used by Windows", Path.Combine(WindowsFolder, "Temp"), false, false),
        new("Prefetch", "App launch cache • rebuilt by Windows", Path.Combine(WindowsFolder, "Prefetch"), true, false),
        new("Event Logs", "Erase Windows diagnostic history", null, true, true),
        new("Windows Update", "Reset SoftwareDistribution contents", Path.Combine(WindowsFolder, "SoftwareDistribution"), true, true)
    ];

    private static void ValidateUserTemp(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var leaf = Path.GetFileName(full);
        // Environment variables can be redirected. Never accept a broad arbitrary folder.
        if (!leaf.Equals("Temp", StringComparison.OrdinalIgnoreCase) &&
            !leaf.Equals("tmp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TEMP must point to a folder named Temp or tmp; custom broad paths are refused.");
    }

    public static void Execute(CleanerOption option, IProgress<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (option.RequiresAdmin && !IsAdministrator)
            throw new InvalidOperationException("Administrator access is required for " + option.Name);
        if (option.Name == "Event Logs")
        {
            ClearEventLogs(log, token);
            return;
        }
        if (option.Name == "User Temp") ValidateUserTemp(option.Path!);
        var target = new CleanupTarget(option.Name, option.Path!);
        var engine = new CleanerEngine([target]);
        void Clean() => engine.Clean(target, log, token, TimeSpan.Zero, option.Name != "Prefetch");
        if (option.Name != "Windows Update") { Clean(); return; }

        using var update = new ManagedService("wuauserv");
        using var orchestrator = new ManagedService("UsoSvc");
        using var transfer = new ManagedService("BITS");
        ServiceTransaction.Execute([update, orchestrator, transfer], () =>
        {
            token.ThrowIfCancellationRequested();
            Clean();
        }, log);
    }

    private static void ClearEventLogs(IProgress<string> log, CancellationToken token)
    {
        var list = Command(["el"], token);
        if (list.Code != 0) throw new IOException("Cannot enumerate event logs: " + list.Error);
        int cleared = 0, skipped = 0;
        foreach (var name in list.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            token.ThrowIfCancellationRequested();
            log.Report("CLEAR LOG " + name);
            var result = Command(["cl", name], token);
            if (result.Code == 0) { cleared++; log.Report("CLEARED " + name); }
            else { skipped++; log.Report($"SKIP {name}: {result.Error.Trim()} (exit {result.Code})"); }
        }
        log.Report($"Event Logs: {cleared} cleared, {skipped} skipped.");
    }

    private static (int Code, string Output, string Error) Command(string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wevtutil.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start wevtutil.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var deadline = Stopwatch.StartNew();
        try
        {
            while (!process.WaitForExit(100))
            {
                token.ThrowIfCancellationRequested();
                if (deadline.Elapsed > TimeSpan.FromSeconds(45))
                    throw new IOException("wevtutil timed out.");
            }
            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); process.WaitForExit(); }
        }
    }

    private sealed class ManagedService(string name) : IManagedService, IDisposable
    {
        private readonly ServiceController controller = new(name);
        public string Name => name;
        private ServiceControllerStatus Status { get { controller.Refresh(); return controller.Status; } }
        public bool IsRunning => Status == ServiceControllerStatus.Running;
        public bool IsStopped => Status == ServiceControllerStatus.Stopped;
        public void Stop()
        {
            controller.Stop(false); // Never stop unrelated dependent services.
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        public void Start()
        {
            var status = Status;
            if (status == ServiceControllerStatus.StopPending)
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            if (Status == ServiceControllerStatus.Stopped) controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
        public void Dispose() => controller.Dispose();
    }
}
