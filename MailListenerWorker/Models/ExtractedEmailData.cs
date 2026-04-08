namespace MailListenerWorker.Models;

public class ExtractedEmailData
{
    // ─── Core Fields (existing) ─────────────────────────────────
    public string CoreProblem { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EstimatedHours { get; set; }
    public string Severity { get; set; } = "Medium"; // Critical, High, Medium, Low
    public string JobField { get; set; } = string.Empty; // Job field/responsibility from CSV
    public int LinksCount { get; set; }
    public int AttachmentCount { get; set; }
    public double Confidence { get; set; }

    // ─── Detail Fields (new — preserve ALL email data) ──────────

    /// <summary>
    /// Thorough description preserving every specific fact, number, name,
    /// date, error message, and technical detail from the email.
    /// </summary>
    public string DetailedDescription { get; set; } = string.Empty;

    /// <summary>
    /// Specific systems, servers, applications, databases, or services mentioned.
    /// Example: "DB-PROD-03, Oracle 19c, SAP ERP"
    /// </summary>
    public string AffectedSystems { get; set; } = string.Empty;

    /// <summary>
    /// Error codes, exception messages, or diagnostic identifiers found in the email.
    /// Example: "ORA-12541, HTTP 503, BSOD 0x0000007E"
    /// </summary>
    public string ErrorCodes { get; set; } = string.Empty;

    /// <summary>
    /// Steps or sequence of events described by the sender, if any.
    /// </summary>
    public string StepsToReproduce { get; set; } = string.Empty;

    /// <summary>
    /// Who or what is affected — user count, departments, environments, regions.
    /// Example: "200 users in the Tunis office, production environment"
    /// </summary>
    public string ImpactScope { get; set; } = string.Empty;

    /// <summary>
    /// What the sender is explicitly requesting or expecting as resolution.
    /// Example: "Restart the database server and restore the latest backup"
    /// </summary>
    public string RequestedAction { get; set; } = string.Empty;

    /// <summary>
    /// Maps severity to Azure DevOps priority (1-4)
    /// Critical=1, High=2, Medium=3, Low=4
    /// </summary>
    public int GetPriority() => Severity switch
    {
        "Critical" => 1,
        "High" => 2,
        "Low" => 4,
        _ => 3 // Medium
    };

    /// <summary>
    /// Gets expected response time in hours based on severity
    /// </summary>
    public int GetExpectedResponseHours() => Severity switch
    {
        "Critical" => 2,
        "High" => 8,
        "Medium" => 24,
        _ => 48 // Low
    };
}
