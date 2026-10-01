using System.Collections.Concurrent;
using System.IO;

namespace SmartLayoutSwitcher.App.Diagnostics;

/// <summary>
/// Minimal bounded file logger. Never logs typed characters or user text;
/// only layouts, window changes and hotkey state. Diagnostic entries are always
/// available; the user setting controls only the extra verbose debug entries.
/// </summary>
public sealed class Logger : IDisposable
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private const int QueueCapacity = 4096;

    private readonly bool _debugEnabled;
    private readonly string? _path;
    private readonly BlockingCollection<string>? _queue;
    private readonly Thread? _writerThread;
    private long _droppedEntries;
    private int _disposed;

    public Logger(bool debugEnabled, string path)
    {
        _debugEnabled = debugEnabled;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            RotateIfNeeded(path);
            using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                // Verify that the destination is writable before starting the worker.
            }
            _path = path;
            _queue = new BlockingCollection<string>(
                new ConcurrentQueue<string>(), QueueCapacity);
            _writerThread = new Thread(WriteQueuedEntries)
            {
                IsBackground = true,
                Name = "SmartLayoutSwitcher diagnostic log writer",
            };
            _writerThread.Start();
        }
        catch
        {
            // Diagnostics are best-effort and must never prevent startup.
        }
    }

    public bool IsEnabled => _queue is not null;

    public void Info(string message) => Write("INFO ", message);
    public void Diagnostic(string message) => Write("DIAG ", message);
    public void Debug(string message)
    {
        if (_debugEnabled)
            Write("DEBUG", message);
    }
    public void Warn(string message) => Write("WARN ", message);

    private static void RotateIfNeeded(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < MaxLogBytes)
            return;

        Rotate(path);
    }

    private static void Rotate(string path)
    {
        var previousPath = path + ".previous";
        if (File.Exists(previousPath))
            File.Delete(previousPath);
        if (File.Exists(path))
            File.Move(path, previousPath);
    }

    private void Write(string level, string message)
    {
        var queue = _queue;
        if (queue is null || Volatile.Read(ref _disposed) != 0)
            return;

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        try
        {
            if (!queue.TryAdd(line))
                Interlocked.Increment(ref _droppedEntries);
        }
        catch (InvalidOperationException)
        {
            // Dispose may complete the queue immediately after the state check.
        }
    }

    private void WriteQueuedEntries()
    {
        var queue = _queue;
        var path = _path;
        if (queue is null || path is null)
            return;

        StreamWriter? writer = null;
        try
        {
            writer = OpenWriter(path);
            foreach (var line in queue.GetConsumingEnumerable())
            {
                if (writer.BaseStream.Length >= MaxLogBytes)
                {
                    writer.Dispose();
                    Rotate(path);
                    writer = OpenWriter(path);
                }

                var dropped = Interlocked.Exchange(ref _droppedEntries, 0);
                if (dropped > 0)
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} WARN  Diagnostic queue dropped {dropped} entr{(dropped == 1 ? "y" : "ies")}.");

                writer.WriteLine(line);
            }
        }
        catch
        {
            // Diagnostics must never destabilize the keyboard service.
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static StreamWriter OpenWriter(string path) =>
        new(path, append: true) { AutoFlush = true };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue?.CompleteAdding();
        _writerThread?.Join(TimeSpan.FromSeconds(2));
    }
}
