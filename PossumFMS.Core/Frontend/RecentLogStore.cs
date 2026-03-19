using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PossumFMS.Core.Frontend;

public sealed record RecentLogEntry(
    long Id,
    DateTime TimestampUtc,
    string Level,
    string Category,
    string Message);

public sealed class RecentLogStore
{
    public const int Capacity = 100;

    private readonly Lock _lock = new();
    private readonly Queue<RecentLogEntry> _entries = [];
    private long _nextId;

    public event Action<RecentLogEntry>? EntryAdded;

    public IReadOnlyList<RecentLogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }

    public void Add(LogLevel level, string category, string message)
    {
        if (level == LogLevel.None)
            return;

        var entry = new RecentLogEntry(
            Id: Interlocked.Increment(ref _nextId),
            TimestampUtc: DateTime.UtcNow,
            Level: level.ToString(),
            Category: category,
            Message: message);

        lock (_lock)
        {
            _entries.Enqueue(entry);

            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }

        EntryAdded?.Invoke(entry);
    }
}

public sealed class RecentLogBufferLoggerProvider(RecentLogStore logStore) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RecentLogBufferLogger(categoryName, logStore);

    public void Dispose()
    {
    }

    private sealed class RecentLogBufferLogger(string categoryName, RecentLogStore logStore) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
                return;

            if (exception is not null)
            {
                message = string.IsNullOrWhiteSpace(message)
                    ? exception.ToString()
                    : $"{message}{Environment.NewLine}{exception}";
            }

            logStore.Add(logLevel, categoryName, message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class RecentLogBroadcaster(
    RecentLogStore logStore,
    IHubContext<FmsHub> hubContext) : BackgroundService
{
    private readonly Channel<RecentLogEntry> _channel = Channel.CreateUnbounded<RecentLogEntry>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logStore.EntryAdded += OnEntryAdded;

        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await hubContext.Clients.All.SendAsync("LogEntry", entry, stoppingToken);
            }
        }
        finally
        {
            logStore.EntryAdded -= OnEntryAdded;
            _channel.Writer.TryComplete();
        }
    }

    private void OnEntryAdded(RecentLogEntry entry)
    {
        _channel.Writer.TryWrite(entry);
    }
}