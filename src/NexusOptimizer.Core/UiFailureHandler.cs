namespace NexusOptimizer.Core;

/// <summary>Unexpected UI failures enter recovery once. Reentrancy must never open more dialogs.</summary>
public sealed class UiFailureHandler(Action<Exception> log, Action<Exception> recover, Action stop)
{
    private int reporting;
    private int stopped;

    public void Report(Exception error)
    {
        // State 1 also protects against an exception raised while constructing the recovery view.
        int state = Interlocked.CompareExchange(ref reporting, 1, 0);
        if (state == 1) return;
        if (state == 2) { StopOnce(); return; }
        try
        {
            try { log(error); } catch (Exception) { /* A logging failure must not suppress recovery. */ }
            try { recover(error); } catch (Exception) { StopOnce(); }
        }
        finally { Volatile.Write(ref reporting, 2); }
    }

    private void StopOnce()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 0)
            stop();
    }
}
