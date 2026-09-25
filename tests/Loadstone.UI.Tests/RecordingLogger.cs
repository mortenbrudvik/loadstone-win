using Microsoft.Extensions.Logging;

namespace Loadstone.UI.Tests;

/// <summary>
/// An ILogger that keeps what it was told, so a test can ask what was reported: the level, the
/// rendered message, and the named values of the message template.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(p => p.Key, p => p.Value)
            : [];
        Entries.Add((logLevel, formatter(state, exception), values));
    }
}
