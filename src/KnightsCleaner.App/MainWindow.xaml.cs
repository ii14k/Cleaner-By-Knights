using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KnightsCleaner.Core;

namespace KnightsCleaner.App;

public partial class MainWindow : Window
{
    private readonly ConcurrentQueue<string> pending = new();
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? cancellation;
    private bool running;

    public MainWindow()
    {
        InitializeComponent();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => FlushLog();
        timer.Start();
        RefreshDrives();
        Log("Ready. Only User Temp and Windows Temp are supported.");
        Log("Files newer than 24 hours and links are skipped. Deletion is permanent.");
        Closing += OnClosing;
        Closed += (_, _) => timer.Stop();
    }

    private void Log(string message)
    {
        // Apply backpressure on the worker so a huge cleanup cannot exhaust memory.
        while (!Dispatcher.CheckAccess() && pending.Count >= 4000) Thread.Sleep(10);
        pending.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
    private void FlushLog()
    {
        for (int i = 0; i < 250 && pending.TryDequeue(out var line); i++)
        {
            Activity.Items.Add(line);
            if (Activity.Items.Count > 2000) Activity.Items.RemoveAt(0);
        }
        if (Activity.Items.Count > 0) Activity.ScrollIntoView(Activity.Items[Activity.Items.Count - 1]);
    }

    private void RefreshDrives()
    {
        Drives.Children.Clear();
        var system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                bool ready = drive.IsReady;
                bool isSystem = string.Equals(drive.Name, system, StringComparison.OrdinalIgnoreCase);
                Drives.Children.Add(new CheckBox
                {
                    Content = $"{drive.Name}  {(isSystem ? "SYSTEM" : drive.DriveType.ToString())}" +
                        (ready ? $"  {drive.VolumeLabel}" : "  Unavailable"),
                    Tag = drive.Name,
                    IsChecked = ready && isSystem,
                    IsEnabled = ready && drive.DriveType is DriveType.Fixed or DriveType.Removable
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log($"SKIP drive {drive.Name}: {e.Message}");
            }
        }
        Log("Drives refreshed. Only the system drive is selected by default.");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDrives();
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool selected = SelectAll.IsChecked != false;
        UserTemp.IsChecked = selected;
        WindowsTemp.IsChecked = selected;
    }
    private void SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (SelectAll == null || UserTemp == null || WindowsTemp == null) return;
        SelectAll.IsChecked = UserTemp.IsChecked == WindowsTemp.IsChecked ? UserTemp.IsChecked : null;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (running) return;
        var roots = Drives.Children.OfType<CheckBox>().Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = TempTargets.Discover();
        var targets = known.Where((t, i) => (i == 0 ? UserTemp.IsChecked == true : WindowsTemp.IsChecked == true)
            && roots.Contains(Path.GetPathRoot(t.Path)!)).ToArray();
        if (targets.Length == 0)
        {
            Log("Nothing to run. Select a service and the drive containing its Temp folder.");
            return;
        }
        foreach (var target in targets) Log($"SELECTED {target.Name}: {target.Path}");
        running = true;
        RunButton.IsEnabled = SelectAll.IsEnabled = Services.IsEnabled = DrivePanel.IsEnabled = false;
        CancelButton.IsEnabled = true;
        Status.Text = "Cleaning...";
        cancellation = new();
        var token = cancellation.Token;
        try
        {
            var engine = new CleanerEngine(known);
            var progress = new LogProgress(Log);
            var results = await Task.Run(() =>
            {
                var list = new List<CleanupResult>();
                foreach (var target in targets)
                {
                    if (token.IsCancellationRequested) break;
                    list.Add(engine.Clean(target, progress, token));
                }
                return list;
            });
            Status.Text = token.IsCancellationRequested ? "Cancelled" : "Completed";
            Log($"{Status.Text}: {results.Sum(r => r.Deleted)} files deleted, " +
                $"{results.Sum(r => r.Skipped)} skipped, {results.Sum(r => r.Bytes):N0} bytes removed.");
        }
        catch (Exception ex)
        {
            Status.Text = "Stopped";
            Log($"ERROR: {ex.Message}");
        }
        finally
        {
            running = false;
            cancellation.Dispose();
            cancellation = null;
            RunButton.IsEnabled = SelectAll.IsEnabled = Services.IsEnabled = DrivePanel.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        Log("Cancellation requested; finishing the current file.");
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!running) return;
        e.Cancel = true;
        cancellation?.Cancel();
        Log("Stopping cleanup. Close the window after cancellation completes.");
    }
    private sealed class LogProgress(Action<string> write) : IProgress<string>
    {
        public void Report(string value) => write(value);
    }
}
