namespace MailListenerWorker.Models;

/// <summary>
/// Represents a job field entry from the CSV (Job Field → Email mapping)
/// </summary>
public class JobFieldMapping
{
    public string JobField { get; set; } = string.Empty;  // e.g., "Administrator", "Dynamics/CRM"
    public string Email { get; set; } = string.Empty;     // User principal name
    public string Department { get; set; } = string.Empty; // Category (organization grouping)
}
