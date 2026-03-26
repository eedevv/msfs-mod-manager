namespace ModInstaller.Core.Services;

/// <summary>
/// Monitors a folder (typically Downloads) for new mod archives (.zip, .7z, .rar).
/// Debounces file-write events and ignores files smaller than 10 KB.
/// Thread-safe.
/// </summary>
public sealed class WatchFolderService : IDisposable
{
    private static readonly string[] WatchedExtensions = [".zip", ".7z", ".rar"];
    private const long MinFileSizeBytes = 10 * 1024; // 10 KB
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    private FileSystemWatcher? _watcher;
    private readonly object _lock = new();
    private readonly Dictionary<string, CancellationTokenSource> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Raised when a new mod archive is detected and the file write has stabilized.
    /// </summary>
    public event Action<string>? NewModDetected;

    /// <summary>
    /// Whether the watcher is currently monitoring a folder.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _watcher is { EnableRaisingEvents: true };
            }
        }
    }

    /// <summary>
    /// Starts monitoring the specified folder for new mod archives.
    /// </summary>
    public void Start(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Watch folder does not exist: {folderPath}");

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            Stop();

            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false
            };

            foreach (var ext in WatchedExtensions)
            {
                _watcher.Filters.Add($"*{ext}");
            }

            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;

            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>
    /// Stops monitoring the folder.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileEvent;
                _watcher.Changed -= OnFileEvent;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            // Cancel all pending debounce operations
            foreach (var cts in _pendingFiles.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _pendingFiles.Clear();
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        ScheduleDebounce(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsWatchedExtension(e.FullPath))
            ScheduleDebounce(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // FileSystemWatcher can die on buffer overflow; attempt restart
        lock (_lock)
        {
            if (_watcher != null && !_disposed)
            {
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.EnableRaisingEvents = true;
                }
                catch
                {
                    // Watcher is unrecoverable
                }
            }
        }
    }

    private void ScheduleDebounce(string filePath)
    {
        lock (_lock)
        {
            if (_disposed) return;

            // Cancel any existing debounce for this file
            if (_pendingFiles.TryGetValue(filePath, out var existingCts))
            {
                existingCts.Cancel();
                existingCts.Dispose();
                _pendingFiles.Remove(filePath);
            }

            var cts = new CancellationTokenSource();
            _pendingFiles[filePath] = cts;

            _ = DebounceAndFireAsync(filePath, cts.Token);
        }
    }

    private async Task DebounceAndFireAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceDelay, cancellationToken).ConfigureAwait(false);

            // Verify the file still exists and meets size requirements
            if (!File.Exists(filePath))
                return;

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < MinFileSizeBytes)
                return;

            // Verify the file is not still being written to
            if (!IsFileReady(filePath))
            {
                // Retry once after another delay
                await Task.Delay(DebounceDelay, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(filePath) || !IsFileReady(filePath))
                    return;
            }

            NewModDetected?.Invoke(filePath);
        }
        catch (OperationCanceledException)
        {
            // Debounce was superseded by a newer event — expected
        }
        finally
        {
            lock (_lock)
            {
                if (_pendingFiles.TryGetValue(filePath, out var stored) && stored.Token == cancellationToken)
                {
                    _pendingFiles.Remove(filePath);
                    stored.Dispose();
                }
            }
        }
    }

    private static bool IsFileReady(string filePath)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsWatchedExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return WatchedExtensions.Any(we => we.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Stop();
    }
}
