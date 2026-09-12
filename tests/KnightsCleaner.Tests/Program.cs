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
sealed class TestLog : IProgress<string>
{
    public void Report(string value) => Console.WriteLine(value);
}
