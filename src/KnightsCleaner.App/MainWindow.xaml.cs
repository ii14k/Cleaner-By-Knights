using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KnightsCleaner.App.Services;

namespace KnightsCleaner.App;

public partial class MainWindow : Window
{
    private readonly ConcurrentQueue<string> pending = new();
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? cancellation;
    private bool running;
    private bool changingSelection;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var option in ScriptActions.Options())
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = option.Name, FontSize = 16, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = option.Description, FontSize = 11, Margin = new Thickness(0,5,0,0),
                Foreground = new SolidColorBrush(Color.FromRgb(147,165,190)), TextWrapping = TextWrapping.Wrap });
            var card = new CheckBox { Content = content, Tag = option, Style = (Style)FindResource("ServiceCard"),
                IsChecked = option.Name is "User Temp" or "Windows Temp", ToolTip = option.Path ?? "All Windows event logs" };
            card.Checked += SelectionChanged;
            card.Unchecked += SelectionChanged;
            Services.Children.Add(card);
        }
        AccessStatus.Text = ScriptActions.IsAdministrator ? "Administrator access" : "Standard access";
        AdminButton.Visibility = ScriptActions.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => FlushLog();
        timer.Start();
        RefreshDrives();
        Log("Knights Cleaner — based on Start_up_Cleaner.bat.");
        Log("Select services, then Run. Advanced operations ask for confirmation.");
        Log("Files are permanently deleted; locked files and links are skipped.");
        SelectionChanged(this, new RoutedEventArgs());
        Closing += OnClosing;
        Closed += (_, _) => timer.Stop();
    }

    private void Log(string message)
    {
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
        var system = Path.GetPathRoot(ScriptActions.WindowsFolder);
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
                    Tag = drive.Name, IsChecked = ready && isSystem,
                    IsEnabled = ready && drive.DriveType is DriveType.Fixed or DriveType.Removable
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Log($"SKIP drive {drive.Name}: {e.Message}"); }
        }
        Log("Drives detected. External and non-system drives start unchecked.");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDrives();
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectAll.IsChecked == true;
        changingSelection = true;
        foreach (CheckBox card in Services.Children) card.IsChecked = selected;
        changingSelection = false;
        SelectionChanged(sender, e);
    }
    private void SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (changingSelection) return;
        var cards = Services.Children.OfType<CheckBox>().ToArray();
        SelectAll.IsChecked = cards.All(c => c.IsChecked == true) ? true :
            cards.All(c => c.IsChecked != true) ? false : null;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (running) return;
        var roots = Drives.Children.OfType<CheckBox>().Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = Services.Children.OfType<CheckBox>().Where(c => c.IsChecked == true)
            .Select(c => (CleanerOption)c.Tag).Where(o => roots.Contains(
                Path.GetPathRoot(o.Path ?? ScriptActions.WindowsFolder)!)).ToArray();
        if (targets.Length == 0) { Log("Nothing to run. Select a service and its drive."); return; }
        if (targets.Any(o => o.RequiresAdmin) && !ScriptActions.IsAdministrator)
        {
            Log("Selected services need administrator access. Click Administrator, then choose them again.");
            Status.Text = "Admin required";
            return;
        }
        if (targets.Any(o => o.Advanced) && MessageBox.Show(this,
            "You selected:\n" + string.Join("\n", targets.Where(o => o.Advanced).Select(o => "• " + o.Name)) +
            "\n\nEvent Logs erases diagnostic history. Windows Update removes its local cache and history, " +
            "temporarily stops update services, then restores services that were running. " +
            "Do not continue while Windows is installing updates.\n\nContinue?",
            "Confirm advanced cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;

        running = true;
        RunButton.IsEnabled = SelectAll.IsEnabled = Services.IsEnabled = DrivePanel.IsEnabled = AdminButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        Status.Text = "Running...";
        cancellation = new();
        var token = cancellation.Token;
        try
        {
            int errors = await Task.Run(() =>
            {
                int failures = 0;
                foreach (var option in targets)
                {
                    token.ThrowIfCancellationRequested();
                    Log("START " + option.Name);
                    try { ScriptActions.Execute(option, new LogProgress(Log), token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { failures++; Log($"FAILED {option.Name}: {ex.Message}"); }
                }
                return failures;
            });
            Status.Text = errors == 0 ? "Finished" : "Finished with errors";
            Log($"{Status.Text}. {errors} service operation(s) failed. See DELETE / SKIP totals above.");
        }
        catch (OperationCanceledException) { Status.Text = "Cancelled"; Log("Cancelled. Completed deletions cannot be undone."); }
        catch (Exception ex) { Status.Text = "Stopped"; Log("ERROR: " + ex.Message); }
        finally
        {
            running = false;
            cancellation.Dispose(); cancellation = null;
            RunButton.IsEnabled = SelectAll.IsEnabled = Services.IsEnabled = DrivePanel.IsEnabled = AdminButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Environment.ProcessPath ?? throw new IOException("Cannot locate application.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Exception ex) { Log("Administrator launch cancelled or failed: " + ex.Message); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel(); Log("Cancellation requested. Service restoration will finish before stopping.");
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!running) return;
        e.Cancel = true; cancellation?.Cancel();
        Log("Stopping safely. Wait for service restoration, then close.");
    }
    private sealed class LogProgress(Action<string> write) : IProgress<string>
    { public void Report(string value) => write(value); }
}
