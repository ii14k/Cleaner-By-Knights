namespace KnightsCleaner.Core;

public sealed record CleanupResult(int Deleted, int Skipped, long Bytes, bool Cancelled);

public sealed class CleanerEngine
{
    private readonly HashSet<string> allowedRoots;
    public CleanerEngine(IEnumerable<CleanupTarget> targets)
    {
        allowedRoots = targets.Select(t => Normalize(t.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));

    private static bool IsExpectedError(Exception e) =>
        e is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    // Check every ancestor to avoid following junctions or symbolic links.
    private static void CheckPath(string path)
    {
        for (DirectoryInfo? current = new(path); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse point in path; skipped for safety.");
    }

    public CleanupResult Clean(CleanupTarget target, IProgress<string> log, CancellationToken token)
    {
        int deleted = 0, skipped = 0;
        long bytes = 0;
        var root = Normalize(target.Path);
        var cutoff = DateTime.UtcNow.AddHours(-24);
        if (!allowedRoots.Contains(root) ||
            string.Equals(root, Normalize(System.IO.Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cleanup root is not allowed.");

        log.Report($"START {target.Name}: {root}");
        void Walk(string directory)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                CheckPath(directory);
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        var full = System.IO.Path.GetFullPath(entry);
                        if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Entry outside cleanup root.");
                        CheckPath(directory);
                        var attributes = File.GetAttributes(full);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            log.Report($"SKIP link: {full}");
                        }
                        else if ((attributes & FileAttributes.Directory) != 0)
                        {
                            Walk(full); // Keep directories, including the root.
                        }
                        else if (File.GetLastWriteTimeUtc(full) >= cutoff)
                        {
                            skipped++;
                            log.Report($"SKIP recent file: {full}");
                        }
                        else
                        {
                            var length = new FileInfo(full).Length;
                            // Do not clear read-only flags or retry access/sharing failures.
                            File.Delete(full);
                            deleted++;
                            bytes += length;
                            log.Report($"DELETE {full} ({length:N0} bytes)");
                        }
                    }
                    catch (Exception e) when (IsExpectedError(e))
                    {
                        skipped++;
                        log.Report($"SKIP {entry}: {e.Message}");
                    }
                }
            }
            catch (Exception e) when (IsExpectedError(e))
            {
                skipped++;
                log.Report($"SKIP directory {directory}: {e.Message}");
            }
        }
        Walk(root);
        log.Report($"END {target.Name}: deleted {deleted}, skipped {skipped}, bytes {bytes:N0}" +
            (token.IsCancellationRequested ? " (cancelled)" : ""));
        return new(deleted, skipped, bytes, token.IsCancellationRequested);
    }
}
