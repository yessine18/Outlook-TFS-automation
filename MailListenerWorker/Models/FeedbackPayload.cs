namespace MailListenerWorker.Models;

public class FeedbackPayload
{
    public string rating { get; set; } = string.Empty;
    public string comment { get; set; } = string.Empty;
}
