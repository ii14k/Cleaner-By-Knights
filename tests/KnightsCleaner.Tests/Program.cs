using KnightsCleaner.Core;

var fixture = Path.Combine(Path.GetTempPath(), "KnightsCleaner-tests-" + Guid.NewGuid());
Directory.CreateDirectory(fixture);
var log = new TestLog();
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
string OldFile(string name)
{
    var path = Path.Combine(fixture, name);
    File.WriteAllText(path, "fixture");
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
    return path;
}
try
{
    var old = OldFile("old.tmp");
    var recent = Path.Combine(fixture, "recent.tmp");
    File.WriteAllText(recent, "keep");
    var locked = OldFile("locked.tmp");
    var readOnly = OldFile("readonly.tmp");
    File.SetAttributes(readOnly, FileAttributes.ReadOnly);
    var target = new CleanupTarget("Fixture", fixture);
    var engine = new CleanerEngine([target]);
    using (var handle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var result = engine.Clean(target, log, CancellationToken.None);
        Assert(!File.Exists(old), "Old file should be deleted.");
        Assert(File.Exists(recent), "Recent file must survive.");
        Assert(File.Exists(locked), "Locked file must survive.");
        Assert(File.Exists(readOnly), "Read-only file must survive.");
        Assert(Directory.Exists(fixture), "Root must survive.");
        Assert(result.Deleted == 1 && result.Skipped == 3 && result.Bytes == 7, "Totals incorrect.");
    }
    var cancelled = OldFile("cancelled.tmp");
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Assert(engine.Clean(target, log, cts.Token).Cancelled && File.Exists(cancelled), "Cancellation failed.");
    bool rejected = false;
    try { engine.Clean(new("Bad", Path.GetPathRoot(fixture)!), log, CancellationToken.None); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "Unapproved root must be rejected.");
    Console.WriteLine("PASS: old/recent/locked/read-only files, totals, cancellation, root protection.");
}
finally
{
    // Delete only the isolated fixture created above.
    File.SetAttributes(Path.Combine(fixture, "readonly.tmp"), FileAttributes.Normal);
    Directory.Delete(fixture, true);
}
// Test rollback without touching real Windows services.
var active = new FakeService("active", true);
var idle = new FakeService("idle", false);
bool cleanupCalled = false;
ServiceTransaction.Execute([active, idle], () =>
{
    if (!active.IsStopped || !idle.IsStopped) throw new Exception("Cleanup ran before stop.");
    cleanupCalled = true;
}, log);
Assert(cleanupCalled && active.IsRunning && idle.IsStopped && idle.Starts == 0, "Service states not preserved.");

active = new FakeService("active", true);
try { ServiceTransaction.Execute([active], () => throw new IOException("fixture failure"), log); }
catch (IOException) { }
Assert(active.IsRunning, "Cleanup failure did not restore service.");

active = new FakeService("active", true) { FailStop = true };
cleanupCalled = false;
try { ServiceTransaction.Execute([active], () => cleanupCalled = true, log); }
catch (IOException) { }
Assert(!cleanupCalled && active.IsRunning, "Stop failure must prevent deletion and restore service.");

active = new FakeService("active", true) { FailStart = true };
bool restoreReported = false;
try { ServiceTransaction.Execute([active], () => { }, log); }
catch (AggregateException) { restoreReported = true; }
Assert(restoreReported, "Restoration failures must be reported.");
Console.WriteLine("PASS: stop-before-cleanup, initial states, rollback, stop failure, restoration failure.");

sealed class FakeService(string name, bool running) : IManagedService
{
    public string Name => name;
    public bool IsRunning { get; private set; } = running;
    public bool IsStopped => !IsRunning;
    public bool FailStop { get; init; }
    public bool FailStart { get; init; }
    public int Starts { get; private set; }
    public void Stop() { IsRunning = false; if (FailStop) throw new IOException("Stop fixture failure."); }
    public void Start() { Starts++; if (FailStart) throw new IOException("Start fixture failure."); IsRunning = true; }
}

sealed class TestLog : IProgress<string>
{
    public void Report(string value) => Console.WriteLine(value);
}
