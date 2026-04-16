using System.Text.Json.Serialization;

namespace MailListenerWorker.Models;

public class RagVerdict
{
    public bool HasSolution { get; set; }
    
    public double ConfidenceScore { get; set; }
    
    public string ProposedSolution { get; set; } = string.Empty;
    
    public List<string> ReferenceUrls { get; set; } = new();
}
