namespace ModInstaller.Core.Utils;

/// <summary>Prevents overlapping file mutations within this process; callers fail rather than queue stale UI requests.</summary>
public static class MutationGate
{
    private static int _held;
    public static bool IsBusy => Volatile.Read(ref _held) != 0;

    public static IDisposable Enter()
    {
        if (Interlocked.CompareExchange(ref _held, 1, 0) != 0)
            throw new IOException("Another file operation is already in progress. Wait for it to finish and try again.");
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Volatile.Write(ref _held, 0);
        }
    }
}
