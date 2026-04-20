namespace MailListenerWorker.Models.Enums;

/// <summary>
/// Represents the internal pipeline processing stages for a helpdesk ticket.
/// Each value maps to a discrete step in the email-to-work-item lifecycle.
/// Stored as a string in PostgreSQL via EF Core value conversion.
/// </summary>
public enum PipelineStatus
{
    /// <summary>The email has been received from MS Graph and persisted.</summary>
    EmailReceived,

    /// <summary>The ticket body is currently being analysed by the Groq LLM.</summary>
    LlmProcessing,

    /// <summary>The LLM successfully extracted structured data from the email.</summary>
    LlmSuccess,

    /// <summary>The LLM call failed (timeout, rate-limit, parse error, etc.).</summary>
    LlmFailed,

    /// <summary>An Azure DevOps work item is being created for this ticket.</summary>
    AdoCreating,

    /// <summary>The Azure DevOps work item was created successfully.</summary>
    AdoCreated,

    /// <summary>The Azure DevOps work item creation failed.</summary>
    AdoFailed,

    /// <summary>The email notification (auto-reply or assignee) failed to send.</summary>
    MailSendingFailed,

    /// <summary>Waiting for client to validate the AI resolution.</summary>
    PendingClientValidation,

    /// <summary>Client successfully validated the AI resolution.</summary>
    ClientAcceptedResolution,

    /// <summary>Client rejected the AI resolution.</summary>
    AdoCreated
}
