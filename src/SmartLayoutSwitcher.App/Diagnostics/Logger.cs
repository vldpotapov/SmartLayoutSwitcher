using System.IO;

namespace SmartLayoutSwitcher.App.Diagnostics;

/// <summary>
/// Minimal file logger (spec §24). Never logs typed characters or user text;
/// only layouts, window changes and hotkey events. Debug level off by default.
/// </summary>
public sealed class Logger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter? _writer;

    public Logger(bool enabled, string path)
    {
        if (!enabled)
            return;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        }
        catch
        {
            _writer = null;
        }
    }

    public bool IsEnabled => _writer is not null;

    public void Info(string message) => Write("INFO ", message);
    public void Debug(string message) => Write("DEBUG", message);
    public void Warn(string message) => Write("WARN ", message);

    private void Write(string level, string message)
    {
        var writer = _writer;
        if (writer is null)
            return;

        lock (_gate)
        {
            writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
        }
    }
}