namespace MailListenerWorker.Models;

public class ExtractedEmailData
{
    public string CoreProblem { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EstimatedHours { get; set; }
    public string Severity { get; set; } = "Medium"; // Critical, High, Medium, Low
    public string JobField { get; set; } = string.Empty; // Job field/responsibility from CSV
    public int LinksCount { get; set; }
    public int AttachmentCount { get; set; }
    public double Confidence { get; set; }

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
