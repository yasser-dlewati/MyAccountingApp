using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MyAccountingApp.AppServices;

/// <summary>
/// Writes log lines to one file per day in <see cref="Directory"/> (app-private storage) and keeps the last
/// <see cref="RetainDays"/> days. Each line is appended and closed immediately, so nothing is lost on a crash.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int RetainDays = 7;

    public static string Directory => Path.Combine(FileSystem.AppDataDirectory, "logs");

    private static readonly Lock WriteLock = new();

    public FileLoggerProvider()
    {
        System.IO.Directory.CreateDirectory(Directory);
        DeleteOldFiles();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);

    public void Dispose() { }

    /// <summary>Zips all log files into the cache directory (for sharing) and returns the zip path.</summary>
    public static string CreateArchive()
    {
        var zipPath = Path.Combine(FileSystem.CacheDirectory, $"logs-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        lock (WriteLock)
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.log"))
                zip.CreateEntryFromFile(file, Path.GetFileName(file));
        }
        return zipPath;
    }

    internal static void Write(string line)
    {
        var path = Path.Combine(Directory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
        lock (WriteLock)
        {
            try
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (IOException) { } // logging must never take the app down
        }
    }

    private static void DeleteOldFiles()
    {
        var cutoff = DateTime.Now.Date.AddDays(-RetainDays);
        foreach (var file in System.IO.Directory.GetFiles(Directory, "app-*.log"))
        {
            if (File.GetLastWriteTime(file) < cutoff)
                File.Delete(file);
        }
    }

    private sealed class FileLogger(string category) : ILogger
    {
        // "Microsoft.EntityFrameworkCore.Database.Command" → "Database.Command"; keeps lines short.
        private readonly string _category = category.StartsWith("Microsoft.EntityFrameworkCore.")
            ? category["Microsoft.EntityFrameworkCore.".Length..]
            : category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
                .Append(' ').Append(Level(logLevel))
                .Append(" [").Append(_category).Append("] ")
                .AppendLine(formatter(state, exception));
            if (exception is not null)
                sb.AppendLine(exception.ToString());
            Write(sb.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }
}
