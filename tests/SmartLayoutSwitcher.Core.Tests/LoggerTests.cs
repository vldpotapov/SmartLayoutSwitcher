using SmartLayoutSwitcher.App.Diagnostics;

namespace SmartLayoutSwitcher.Core.Tests;

public sealed class LoggerTests
{
    [Fact]
    public void DisabledLoggerDoesNotCreateDirectoryOrFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SmartLayoutSwitcher-tests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "diagnostics.log");

        using (var logger = new Logger(false, path))
        {
            Assert.False(logger.IsEnabled);
            WriteAllLevels(logger);
        }

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void DisabledLoggerPreservesExistingLogsWithoutRotation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SmartLayoutSwitcher-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "diagnostics.log");
        var existing = new string('x', 2 * 1024 * 1024);
        try
        {
            File.WriteAllText(path, existing);
            File.WriteAllText(path + ".previous", "previous");
            using (var logger = new Logger(false, path))
                WriteAllLevels(logger);

            Assert.Equal(existing, File.ReadAllText(path));
            Assert.Equal("previous", File.ReadAllText(path + ".previous"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EnabledLoggerWritesAllLevelsAndFlushesOnDispose()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SmartLayoutSwitcher-tests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "diagnostics.log");
        try
        {
            using (var logger = new Logger(true, path))
            {
                Assert.True(logger.IsEnabled);
                WriteAllLevels(logger);
            }

            var log = File.ReadAllText(path);
            Assert.Contains("INFO  info-test", log);
            Assert.Contains("DIAG  diagnostic-test", log);
            Assert.Contains("DEBUG debug-test", log);
            Assert.Contains("WARN  warn-test", log);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteAllLevels(Logger logger)
    {
        logger.Info("info-test");
        logger.Diagnostic("diagnostic-test");
        logger.Debug("debug-test");
        logger.Warn("warn-test");
    }
}
