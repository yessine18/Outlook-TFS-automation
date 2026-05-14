using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailListenerWorker.Services;

/// <summary>
/// Real-time pipeline event bus using Server-Sent Events (SSE).
/// Broadcasts pipeline step events to all connected browser clients.
/// </summary>
public class PipelineEventService
{
    private readonly List<Channel<PipelineEvent>> _subscribers = new();
    private readonly List<PipelineEvent> _recentEvents = new();
    private readonly object _lock = new();
    private int _nextId = 1;
    private const int MaxRecentEvents = 200;

    /// <summary>Returns the most recent events for initial page load.</summary>
    public IReadOnlyList<PipelineEvent> GetRecentEvents()
    {
        lock (_lock) return _recentEvents.ToList();
    }

    /// <summary>Creates a new subscriber channel for SSE streaming.</summary>
    public Channel<PipelineEvent> Subscribe()
    {
        var channel = Channel.CreateUnbounded<PipelineEvent>();
        lock (_lock) _subscribers.Add(channel);
        return channel;
    }

    /// <summary>Removes a subscriber channel when the client disconnects.</summary>
    public void Unsubscribe(Channel<PipelineEvent> channel)
    {
        lock (_lock) _subscribers.Remove(channel);
    }

    /// <summary>Emits an event to all connected SSE clients.</summary>
    public async Task EmitAsync(string stage, string status, string title, string message,
        string? ticketSubject = null, string? senderEmail = null, string? extraData = null)
    {
        PipelineEvent evt;
        lock (_lock)
        {
            evt = new PipelineEvent(
                _nextId++, stage, status, title, message,
                DateTime.UtcNow, ticketSubject, senderEmail, extraData);

            _recentEvents.Add(evt);
            if (_recentEvents.Count > MaxRecentEvents)
                _recentEvents.RemoveAt(0);
        }

        List<Channel<PipelineEvent>> subs;
        lock (_lock) subs = _subscribers.ToList();

        foreach (var sub in subs)
        {
            try { sub.Writer.TryWrite(evt); }
            catch { lock (_lock) _subscribers.Remove(sub); }
        }
    }
}

/// <summary>
/// Represents a single pipeline processing event for SSE streaming.
/// </summary>
public record PipelineEvent(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("ticketSubject")] string? TicketSubject = null,
    [property: JsonPropertyName("senderEmail")] string? SenderEmail = null,
    [property: JsonPropertyName("extraData")] string? ExtraData = null
);
