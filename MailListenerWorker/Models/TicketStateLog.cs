using MailListenerWorker.Models.Enums;

namespace MailListenerWorker.Models;

/// <summary>
/// Immutable audit record capturing a single pipeline state transition for a <see cref="Ticket"/>.
/// A new row is appended on every status change; existing rows are never modified.
/// </summary>
public class TicketStateLog
{
    /// <summary>Primary key for the log entry.</summary>
    public Guid LogId { get; set; }

    /// <summary>Foreign key referencing the parent <see cref="Ticket"/>.</summary>
    public Guid TicketId { get; set; }

    /// <summary>
    /// The pipeline status that was entered at <see cref="CreatedAt"/>.
    /// Persisted as a string column for readability and forward-compatibility.
    /// </summary>
    public PipelineStatus PipelineStatus { get; set; }

    /// <summary>
    /// Optional error message captured when the status indicates a failure
    /// (e.g. <see cref="PipelineStatus.LlmFailed"/> or <see cref="PipelineStatus.AdoFailed"/>).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>UTC timestamp when this state transition occurred.</summary>
    public DateTime CreatedAt { get; set; }

    // ───────────────────────── Navigation Properties ───────────

    /// <summary>Back-reference to the parent ticket.</summary>
    public Ticket Ticket { get; set; } = null!;
}
