using MailListenerWorker.Models.Enums;

namespace MailListenerWorker.Models;

/// <summary>
/// Core entity representing a helpdesk email that flows through the processing pipeline.
/// Stores the raw email metadata, LLM extraction results, and the final Azure DevOps mapping.
/// </summary>
public class Ticket
{
    // ───────────────────────── Identity ─────────────────────────

    /// <summary>Primary key – a new GUID is generated for every incoming email.</summary>
    public Guid TicketId { get; set; }

    /// <summary>
    /// The immutable Internet Message-ID header returned by MS Graph.
    /// Used for idempotent ingestion (prevents duplicate processing).
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    // ───────────────────────── Email Metadata ──────────────────

    /// <summary>The sender's email address.</summary>
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>The email subject line.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>A truncated excerpt of the email body used for display and auditing.</summary>
    public string BodyExcerpt { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the email was received by the mail server.</summary>
    public DateTime ReceivedAt { get; set; }

    // ───────────────────────── LLM Extraction ──────────────────

    /// <summary>Department/team extracted by the LLM (e.g. "Network", "Billing").</summary>
    public string? ExtractedDepartment { get; set; }

    /// <summary>User intent or issue category extracted by the LLM.</summary>
    public string? ExtractedIntent { get; set; }

    /// <summary>
    /// Confidence score (0.0 – 1.0) returned by the LLM for its extraction.
    /// Null until LLM processing completes.
    /// </summary>
    public double? LlmConfidenceScore { get; set; }

    // ───────────────────────── Azure DevOps Mapping ────────────

    /// <summary>The numeric ID of the created ADO work item. Null until creation succeeds.</summary>
    public int? AdoWorkItemId { get; set; }

    /// <summary>The display name or email of the ADO assignee.</summary>
    public string? AdoAssignee { get; set; }

    /// <summary>Direct URL to the ADO work item in the web portal.</summary>
    public string? AdoUrl { get; set; }

    /// <summary>
    /// The external ADO board state (e.g. "To Do", "Doing", "Done").
    /// Updated when the worker syncs back from ADO.
    /// </summary>
    public string? AdoItemState { get; set; }

    // ───────────────────────── Pipeline Tracking ───────────────

    /// <summary>
    /// The current internal pipeline status of this ticket.
    /// Persisted as a string column for readability and forward-compatibility.
    /// </summary>
    public PipelineStatus CurrentPipelineStatus { get; set; }

    /// <summary>UTC timestamp of the last status or data change on this ticket.</summary>
    public DateTime LastUpdatedAt { get; set; }

    // ───────────────────────── Navigation Properties ───────────

    /// <summary>
    /// Ordered log of every pipeline state transition and associated errors.
    /// One ticket → many state log entries.
    /// </summary>
    public ICollection<TicketStateLog> StateLog { get; set; } = new List<TicketStateLog>();
}
