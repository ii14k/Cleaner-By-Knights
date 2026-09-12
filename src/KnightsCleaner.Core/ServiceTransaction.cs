namespace KnightsCleaner.Core;

public interface IManagedService
{
    string Name { get; }
    bool IsRunning { get; }
    bool IsStopped { get; }
    void Stop();
    void Start();
}

public static class ServiceTransaction
{
    public static void Execute(IReadOnlyList<IManagedService> services, Action cleanup, IProgress<string> log)
    {
        // Validate all initial states before stopping anything.
        var running = services.Where(s => s.IsRunning).ToArray();
        if (services.Any(s => !s.IsRunning && !s.IsStopped))
            throw new InvalidOperationException("A service is changing state. Try again later.");
        var requested = new List<IManagedService>();
        Exception? failure = null;
        var restorationErrors = new List<Exception>();
        try
        {
            foreach (var service in running)
            {
                requested.Add(service); // Restore even if Stop times out after sending its request.
                log.Report($"STOP {service.Name}");
                service.Stop();
            }
            if (services.Any(s => !s.IsStopped))
                throw new InvalidOperationException("Update services are not all stopped; cleanup aborted.");
            cleanup();
        }
        catch (Exception e) { failure = e; }
        finally
        {
            foreach (var service in requested.AsEnumerable().Reverse())
            {
                try
                {
                    log.Report($"RESTORE {service.Name}");
                    service.Start();
                    log.Report($"RUNNING {service.Name}");
                }
                catch (Exception e)
                {
                    restorationErrors.Add(e);
                    log.Report($"RESTORE FAILED {service.Name}: {e.Message}. Restart Windows to recover.");
                }
            }
        }
        if (restorationErrors.Count > 0)
        {
            if (failure != null) restorationErrors.Add(failure);
            throw new AggregateException("One or more services could not be restored. Restart Windows.", restorationErrors);
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
