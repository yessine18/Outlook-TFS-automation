using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MailListenerWorker.Data;
using MailListenerWorker.Services;
using MailListenerWorker.Models;
using MailListenerWorker.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MailListenerWorker;

public class MailPollingService : BackgroundService
{
    private readonly ILogger<MailPollingService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly AzureDevOpsService _adoService;
    private readonly GroqLlmService _llmService;
    private readonly JobFieldMappingService _jobFieldService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _mailboxUser;
    private readonly string _defaultAssignee;
    private readonly string _logoUrl;
    private readonly string _footerLogoUrl;
    private readonly string _supportPhone;
    private readonly string _autoReplyTemplate;
    private readonly string _assigneeNotificationTemplate;

    public MailPollingService(
        ILogger<MailPollingService> logger,
        IConfiguration configuration,
        AzureDevOpsService adoService,
        GroqLlmService llmService,
        JobFieldMappingService jobFieldService,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _adoService = adoService;
        _llmService = llmService;
        _jobFieldService = jobFieldService;
        _scopeFactory = scopeFactory;

        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        var clientSecret = configuration["AzureAd:ClientSecret"];
        _mailboxUser = configuration["AzureAd:MailboxUser"]
            ?? throw new InvalidOperationException("Missing AzureAd:MailboxUser setting.");
        _defaultAssignee = configuration["JobFieldCsv:DefaultAssignee"]
            ?? throw new InvalidOperationException("Missing JobFieldCsv:DefaultAssignee setting.");
        _logoUrl = configuration["Email:LogoUrl"] ?? "https://via.placeholder.com/150x40?text=Support";
        _footerLogoUrl = configuration["Email:FooterLogoUrl"] ?? _logoUrl;
        _supportPhone = configuration["Email:SupportPhone"] ?? "+216 56 646 677";

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Missing AzureAd settings. Configure TenantId, ClientId, ClientSecret (User Secrets recommended).");
        }

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _graphClient = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        _autoReplyTemplate = LoadEmailTemplate("AutoReplyTemplate.html");
        _assigneeNotificationTemplate = LoadEmailTemplate("AssigneeNotificationTemplate.html");
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
                    _logger.LogDebug("Checking ADO WorkItem #{Id}: State='{State}', Tags='{Tags}'", item.Id, state, tags);

                    // 1. ALWAYS SYNC WITH POSTGRESQL ───────────────────
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        
                        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.AdoWorkItemId == item.Id, cancellationToken);
                        if (ticket != null && !string.Equals(ticket.AdoItemState, state, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("🔄 Syncing ADO WorkItem #{Id}: DB State '{DbState}' -> ADO State '{AdoState}'", 
                                item.Id, ticket.AdoItemState, state);
                            
                            ticket.AdoItemState = state;
                            ticket.LastUpdatedAt = DateTime.UtcNow;
                            
                            // Update internal status if it reaches terminal ADO states
                            if (state.Equals("Done", StringComparison.OrdinalIgnoreCase) || 
                                state.Equals("Closed", StringComparison.OrdinalIgnoreCase))
                            {
                                ticket.CurrentPipelineStatus = PipelineStatus.AdoCreated;
                            }

                            ticket.StateLog.Add(new TicketStateLog
                            {
                                LogId = Guid.NewGuid(),
                                TicketId = ticket.TicketId,
                                PipelineStatus = ticket.CurrentPipelineStatus,
                                ErrorMessage = $"ADO State updated to: {state}",
                                CreatedAt = DateTime.UtcNow
                            });

                            await db.SaveChangesAsync(cancellationToken);
                        }
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "Failed to sync ADO update to database for WorkItem #{Id}", item.Id);
                    }

                    // 2. ONLY SEND AUTO-REPLY ONCE PER STATE ───────────
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
                                    null,
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
        var senderName = msg.From?.EmailAddress?.Name ?? senderEmail ?? "Unknown";

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
        var assigneeEmail = _defaultAssignee;
        ExtractedEmailData? extractedData = null;

        try
        {
            // Analyze email with Groq LLM to extract key information
            extractedData = await _llmService.AnalyzeEmailAsync(
                msg.Subject ?? "No Subject",
                emailBody,
                cancellationToken);

            // Resolve assignee based on extracted job field
            assigneeEmail = _jobFieldService.ResolveEmail(extractedData.JobField, _defaultAssignee);
            var jobFieldMapping = _jobFieldService.GetMapping(extractedData.JobField);

            // Create Azure DevOps work item with extracted data
            var createdItem = await _adoService.CreateEmailWorkItemAsync(
                msg.Subject ?? "No Subject",
                emailBody,
                senderEmail ?? "Unknown",
                senderName ?? "Unknown",
                msg.ReceivedDateTime,
                extractedData,
                assigneeEmail,
                cancellationToken);

            workItemId = createdItem.Id ?? 0;
            if (createdItem.Fields != null && createdItem.Fields.TryGetValue("System.State", out var stateObj))
            {
                initialState = stateObj?.ToString() ?? "To Do";
            }

            _logger.LogInformation(
                "Created ADO work item #{WorkItemId} for email from {Email} (Severity: {Severity}, Priority: {Priority}, JobField: {JobField}, Assignee: {Assignee})",
                workItemId,
                senderEmail,
                extractedData.Severity,
                extractedData.GetPriority(),
                extractedData.JobField,
                assigneeEmail);

            // Send email notification to assignee (if different from default)
            if (!string.IsNullOrEmpty(assigneeEmail) && !assigneeEmail.Equals(_defaultAssignee, StringComparison.OrdinalIgnoreCase))
            {
                await SendAssigneeNotificationAsync(
                    assigneeEmail,
                    workItemId.ToString(),
                    extractedData.CoreProblem ?? msg.Subject ?? "No Subject",
                    senderName ?? "Unknown",
                    extractedData.Severity ?? "Medium",
                    extractedData.EstimatedHours,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ADO work item for email: {Subject}", msg.Subject);
        }

        if (!string.IsNullOrEmpty(senderEmail) && workItemId > 0)
        {
            await SendAutoReplyAsync(
                senderEmail,
                senderName ?? "Unknown",
                msg.Subject,
                workItemId.ToString(),
                initialState,
                extractedData,
                cancellationToken);
            _logger.LogInformation("Auto-reply sent to: {Email}", senderEmail);
        }

        await MarkAsReadAsync(msg.Id!, cancellationToken);

        // ── PERSIST TO POSTGRESQL ───────────────────
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ticket = new Ticket
            {
                TicketId = Guid.NewGuid(),
                MessageId = msg.Id ?? "Unknown",
                SenderEmail = senderEmail ?? "Unknown",
                Subject = msg.Subject ?? "No Subject",
                BodyExcerpt = emailBody.Length > 4000 ? emailBody[..3997] + "..." : emailBody,
                ReceivedAt = msg.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                CurrentPipelineStatus = PipelineStatus.AdoCreated,
                
                // Extracted data (if available)
                ExtractedDepartment = extractedData?.JobField,
                ExtractedIntent = extractedData?.CoreProblem,
                LlmConfidenceScore = extractedData != null ? 0.95 : 0.0, // Mocked confidence for now
                
                // ADO mapping
                AdoWorkItemId = workItemId > 0 ? workItemId : null,
                AdoAssignee = assigneeEmail,
                AdoItemState = initialState,
                AdoUrl = workItemId > 0 ? $"https://dev.azure.com/yessinefakhfakh/PFE-automation/_workitems/edit/{workItemId}" : null
            };

            ticket.StateLog.Add(new TicketStateLog
            {
                LogId = Guid.NewGuid(),
                TicketId = ticket.TicketId,
                PipelineStatus = PipelineStatus.AdoCreated,
                CreatedAt = DateTime.UtcNow
            });

            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("✅ Ticket #{WorkItemId} persisted to PostgreSQL database successfully.", workItemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to persist ticket to PostgreSQL database.");
        }
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
        ExtractedEmailData? extractedData,
        CancellationToken cancellationToken)
    {
        var subject = originalSubject ?? "Your Request";

        var estimatedHours = extractedData?.EstimatedHours.ToString() ?? "4";
        var severity = extractedData?.Severity ?? "Medium";
        var severityClass = severity.ToLower();
        var expectedResponseTime = extractedData?.GetExpectedResponseHours().ToString() ?? "24";

          var ticketUrl = $"https://dev.azure.com/yessinefakhfakh/PFE-automation/_workitems/edit/{ticketId}";
          
          // Generate QR Code base64 Data URI
          var qrCodeResult = await Utilities.QrCodeGenerator.GenerateQrCodeBase64Async(ticketUrl);
          var qrCodeHtml = string.IsNullOrEmpty(qrCodeResult) ? "" : $@"
              <div style=""text-align: center; padding-top: 0; margin-bottom: 20px;"">
                  <div style=""margin: 0 auto; padding: 20px; background: #fff; display: inline-block; border: 1px dashed #ccc; border-radius: 8px;"">
                      <p style=""font-size: 12px; color: #888; margin-bottom: 10px; text-transform: uppercase;"">Scan for Quick Access</p>
                      <img src=""{qrCodeResult}"" alt=""QR Code"" style=""width: 120px; height: 120px;"" width=""120"" height=""120"">
                  </div>
              </div>";

          var htmlContent = _autoReplyTemplate
              .Replace("{{LogoUrl}}", _logoUrl)
              .Replace("{{FooterLogoUrl}}", _footerLogoUrl)
              .Replace("{{RecipientName}}", recipientName)
              .Replace("{{TicketNumber}}", ticketId)
              .Replace("{{TicketStatus}}", ticketStatus)
              .Replace("{{OriginalSubject}}", subject)
              .Replace("{{TicketTitle}}", subject)
              .Replace("{{TicketUrl}}", ticketUrl)
              .Replace("{{QrCodeHtml}}", qrCodeHtml)
              .Replace("{{Year}}", DateTime.UtcNow.Year.ToString())
              .Replace("{{SupportEmail}}", _mailboxUser)
              .Replace("{{SupportPhone}}", _supportPhone)
              .Replace("{{EstimatedHours}}", estimatedHours)
              .Replace("{{Severity}}", severity)
              .Replace("{{SeverityClass}}", severityClass)
              .Replace("{{ExpectedResponseTime}}", expectedResponseTime);

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

    private async Task SendAssigneeNotificationAsync(
        string assigneeEmail,
        string ticketId,
        string ticketTitle,
        string senderName,
        string severity,
        int estimatedHours,
        CancellationToken cancellationToken)
    {
        try
        {
            var severityClass = severity.ToLower();
            var ticketUrl = $"https://dev.azure.com/yessinefakhfakh/PFE-automation/_workitems/edit/{ticketId}";
            
            // Generate QR Code base64 Data URI
            var qrCodeResult = await Utilities.QrCodeGenerator.GenerateQrCodeBase64Async(ticketUrl);
            var qrCodeHtml = string.IsNullOrEmpty(qrCodeResult) ? "" : $@"
            <div class=""section"" style=""text-align: center; border-top: none; padding-top: 0;"">
                <div style=""margin: 20px auto; padding: 20px; background: #fff; display: inline-block; border: 1px dashed #ccc; border-radius: 8px;"">
                    <p style=""font-size: 12px; color: #888; margin-bottom: 10px; text-transform: uppercase;"">Scan for Quick Access</p>
                    <img src=""{qrCodeResult}"" alt=""QR Code"" style=""width: 120px; height: 120px;"" width=""120"" height=""120"">
                </div>
            </div>";

            var notificationBody = _assigneeNotificationTemplate
                .Replace("{{LogoUrl}}", _logoUrl)                  .Replace("{{FooterLogoUrl}}", _footerLogoUrl)                .Replace("{{TicketNumber}}", ticketId)
                .Replace("{{ReporterName}}", senderName)
                .Replace("{{TicketTitle}}", ticketTitle)
                .Replace("{{Severity}}", severity)
                .Replace("{{SeverityClass}}", severityClass)
                .Replace("{{EstimatedHours}}", estimatedHours.ToString())
                .Replace("{{Year}}", DateTime.UtcNow.Year.ToString())
                .Replace("{{TicketUrl}}", ticketUrl)
                .Replace("{{QrCodeHtml}}", qrCodeHtml);

            var message = new Message
            {
                Subject = $"[TKT-{ticketId}] New ticket assigned: {ticketTitle}",
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = notificationBody
                },
                ToRecipients =
                [
                    new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = assigneeEmail }
                    }
                ]
            };

            await _graphClient
                .Users[_mailboxUser]
                .SendMail
                .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                }, cancellationToken: cancellationToken);

            _logger.LogInformation("Assignee notification sent to {Email} for ticket {TicketId}", assigneeEmail, ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send assignee notification to {Email} for ticket {TicketId}", assigneeEmail, ticketId);
        }
    }

    private static string LoadEmailTemplate(string templateName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"MailListenerWorker.Templates.{templateName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Email template not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
