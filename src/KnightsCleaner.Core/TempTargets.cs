namespace KnightsCleaner.Core;

public sealed record CleanupTarget(string Name, string Path);

public static class TempTargets
{
    public static IReadOnlyList<CleanupTarget> Discover() =>
    [
        new("User Temp", System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")),
        new("Windows Temp", System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"))
    ];
}
