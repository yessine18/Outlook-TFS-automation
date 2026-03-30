using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Reflection;

namespace MailListenerWorker;

public class MailPollingService : BackgroundService
{
    private readonly ILogger<MailPollingService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly AzureDevOpsService _adoService;
    private readonly string _mailboxUser;
    private readonly string _htmlTemplate;

    public MailPollingService(ILogger<MailPollingService> logger, IConfiguration configuration, AzureDevOpsService adoService)
    {
        _logger = logger;
        _adoService = adoService;

        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        var clientSecret = configuration["AzureAd:ClientSecret"];
        _mailboxUser = configuration["AzureAd:MailboxUser"]
            ?? throw new InvalidOperationException("Missing AzureAd:MailboxUser setting.");

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Missing AzureAd settings. Configure TenantId, ClientId, ClientSecret (User Secrets recommended).");
        }

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _graphClient = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        _htmlTemplate = LoadEmailTemplate();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MailPollingService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollMailboxAsync(stoppingToken);
            await PollWorkItemUpdatesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("MailPollingService stopping");
    }

    private async Task PollMailboxAsync(CancellationToken cancellationToken)
    {
        try
        {
            var messages = await _graphClient
                .Users[_mailboxUser]
                .MailFolders["Inbox"]
                .Messages
                .GetAsync(config =>
                {
                    config.QueryParameters.Filter = "isRead eq false";
                    config.QueryParameters.Top = 10;
                    config.QueryParameters.Select = ["id", "subject", "from", "receivedDateTime", "bodyPreview", "isRead"];
                }, cancellationToken);

            if (messages?.Value is null) return;

            foreach (var msg in messages.Value)
            {
                if (msg.IsRead == true) continue;

                await ProcessEmailAsync(msg, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while polling mailbox");
        }
    }

    private async Task PollWorkItemUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var updatedItems = await _adoService.GetUpdatedWorkItemsAsync("AutoCreated", cancellationToken);
            foreach (var item in updatedItems)
            {
                if (item.Id == null) continue;

                if (item.Fields.TryGetValue("System.State", out var stateObj) && 
                    item.Fields.TryGetValue("System.Tags", out var tagsObj))
                {
                    var state = stateObj?.ToString() ?? "Unknown";
                    var tags = tagsObj?.ToString() ?? "";
                    
                    // We remove spaces to make a valid tag, e.g. "To Do" -> "EmailSent_ToDo" -> "EmailSent_ToDo" (wait, tags can have spaces but let's remove them for the flag)
                    var stateTag = state.Replace(" ", "");
                    var expectedTag = $"EmailSent_{stateTag}";

                    if (!tags.Contains(expectedTag))
                    {
                        if (item.Fields.TryGetValue("System.Description", out var descObj))
                        {
                            var description = descObj?.ToString() ?? "";
                            var email = ExtractHtmlAttribute(description, "data-sender-email");
                            var name = ExtractHtmlAttribute(description, "data-sender-name");
                            var title = item.Fields.TryGetValue("System.Title", out var tObj) 
                                ? tObj?.ToString()?.Replace("[EMAIL] ", "") 
                                : "Your Request";

                            if (!string.IsNullOrEmpty(email))
                            {
                                await SendAutoReplyAsync(
                                    email, 
                                    string.IsNullOrEmpty(name) ? "User" : name, 
                                    title, 
                                    item.Id.Value.ToString(), 
                                    state, 
                                    cancellationToken);
                                
                                await _adoService.AddWorkItemTagAsync(item.Id.Value, tags, expectedTag, cancellationToken);
                                _logger.LogInformation("Sent state update for WorkItem #{Id} to {State} ({Email})", item.Id, state, email);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while polling work item updates");
        }
    }

    private static string ExtractHtmlAttribute(string html, string attributeName)
    {
        var searchString = $"{attributeName}=\"";
        var startIndex = html.IndexOf(searchString);
        if (startIndex == -1) return string.Empty;

        startIndex += searchString.Length;
        var endIndex = html.IndexOf("\"", startIndex);
        if (endIndex == -1) return string.Empty;

        return html.Substring(startIndex, endIndex - startIndex);
    }

    private async Task ProcessEmailAsync(Message msg, CancellationToken cancellationToken)
    {
        var senderEmail = msg.From?.EmailAddress?.Address;
        var senderName = msg.From?.EmailAddress?.Name ?? senderEmail;

        _logger.LogInformation(
            "New email - Subject: {Subject}, From: {From}, Received: {ReceivedAt}",
            msg.Subject,
            senderEmail,
            msg.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss"));

        // Fetch the full message to get the Body details
        var fullMessage = await _graphClient
            .Users[_mailboxUser]
            .Messages[msg.Id]
            .GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "subject", "body"];
            }, cancellationToken);

        var emailBody = fullMessage?.Body?.Content ?? msg.BodyPreview ?? "No content";
        var workItemId = 0;
        var initialState = "To Do";

        try
        {
            // Create Azure DevOps work item
            var createdItem = await _adoService.CreateEmailWorkItemAsync(
                msg.Subject ?? "No Subject",
                emailBody,
                senderEmail ?? "Unknown",
                senderName ?? "Unknown",
                msg.ReceivedDateTime,
                cancellationToken);
                
            workItemId = createdItem.Id ?? 0;
            if (createdItem.Fields.TryGetValue("System.State", out var stateObj))
            {
                initialState = stateObj?.ToString() ?? "To Do";
            }

            _logger.LogInformation(
                "Created ADO work item #{WorkItemId} for email from {Email}",
                workItemId,
                senderEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ADO work item for email: {Subject}", msg.Subject);
        }

        // TODO: Call Python Agent A (via HTTP) and store in SQL database

        if (!string.IsNullOrEmpty(senderEmail) && workItemId > 0)
        {
            await SendAutoReplyAsync(senderEmail, senderName!, msg.Subject, workItemId.ToString(), initialState, cancellationToken);
            _logger.LogInformation("Auto-reply sent to: {Email}", senderEmail);
        }

        await MarkAsReadAsync(msg.Id!, cancellationToken);
    }

    private async Task MarkAsReadAsync(string messageId, CancellationToken cancellationToken)
    {
        await _graphClient
            .Users[_mailboxUser]
            .Messages[messageId]
            .PatchAsync(new Message { IsRead = true }, cancellationToken: cancellationToken);

        _logger.LogInformation("Marked as read: {Id}", messageId);
    }

    private async Task SendAutoReplyAsync(
        string recipientEmail,
        string recipientName,
        string? originalSubject,
        string ticketId,
        string ticketStatus,
        CancellationToken cancellationToken)
    {
        var subject = originalSubject ?? "Your Request";

        var htmlContent = _htmlTemplate
            .Replace("{{RecipientName}}", recipientName)
            .Replace("{{TicketNumber}}", ticketId)
            .Replace("{{TicketStatus}}", ticketStatus)
            .Replace("{{OriginalSubject}}", subject)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString())
            .Replace("{{SupportEmail}}", _mailboxUser)
            .Replace("{{SupportPhone}}", "+216 56 646 677");

        var replyMessage = new Message
        {
            Subject = $"Re: {subject} [TKT-{ticketId}]",
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = htmlContent
            },
            ToRecipients =
            [
                new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = recipientEmail,
                        Name = recipientName
                    }
                }
            ]
        };

        await _graphClient
            .Users[_mailboxUser]
            .SendMail
            .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = replyMessage,
                SaveToSentItems = true
            }, cancellationToken: cancellationToken);
    }

    private static string LoadEmailTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "MailListenerWorker.Templates.AutoReplyTemplate.html";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Email template not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
