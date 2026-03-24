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
    private readonly string _mailboxUser;
    private readonly string _htmlTemplate;

    public MailPollingService(ILogger<MailPollingService> logger, IConfiguration configuration)
    {
        _logger = logger;

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

    private async Task ProcessEmailAsync(Message msg, CancellationToken cancellationToken)
    {
        var senderEmail = msg.From?.EmailAddress?.Address;
        var senderName = msg.From?.EmailAddress?.Name ?? senderEmail;

        _logger.LogInformation(
            "New email - Subject: {Subject}, From: {From}, Received: {ReceivedAt}",
            msg.Subject,
            senderEmail,
            msg.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss"));

        // TODO: Call Python Agent A (via HTTP) and store in SQL database

        if (!string.IsNullOrEmpty(senderEmail))
        {
            await SendAutoReplyAsync(senderEmail, senderName!, msg.Subject, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var ticketNumber = $"TKT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
        var subject = originalSubject ?? "Your Request";

        var htmlContent = _htmlTemplate
            .Replace("{{RecipientName}}", recipientName)
            .Replace("{{TicketNumber}}", ticketNumber)
            .Replace("{{OriginalSubject}}", subject)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString())
            .Replace("{{SupportEmail}}", _mailboxUser)
            .Replace("{{SupportPhone}}", "+216 56 646 677");

        var replyMessage = new Message
        {
            Subject = $"Re: {subject} [{ticketNumber}]",
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
