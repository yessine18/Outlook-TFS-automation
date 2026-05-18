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
    private readonly PipelineEventService _events;
    private readonly string _mailboxUser;
    private readonly string _defaultAssignee;
    private readonly string _logoUrl;
    private readonly string _footerLogoUrl;
    private readonly string _supportPhone;
    private readonly string _autoReplyTemplate;
    private readonly string _assigneeNotificationTemplate;
    private readonly string _tmaEmail = "ApplicationSupport@M365x62207154.onmicrosoft.com";
    private readonly List<string> _allowedDomains;
    private readonly string _baseAppUrl;

    public MailPollingService(
        ILogger<MailPollingService> logger,
        IConfiguration configuration,
        AzureDevOpsService adoService,
        GroqLlmService llmService,
        JobFieldMappingService jobFieldService,
        IServiceScopeFactory scopeFactory,
        PipelineEventService events)
    {
        _logger = logger;
        _adoService = adoService;
        _llmService = llmService;
        _jobFieldService = jobFieldService;
        _scopeFactory = scopeFactory;
        _events = events;

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
        _baseAppUrl = configuration["Email:BaseAppUrl"] ?? "http://localhost:5000";
        _allowedDomains = configuration.GetSection("Email:AllowedDomains").Get<List<string>>() ?? new List<string>();

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
        _logger.LogInformation("🚀 MailPollingService started and ready.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("🔄 Starting automation poll cycle at {Time}...", DateTime.Now.ToString("HH:mm:ss"));
                await _events.EmitAsync("system", "info", "🔄 Poll Cycle Started", $"Scanning for new emails at {DateTime.Now:HH:mm:ss}");
                
                await PollMailboxAsync(stoppingToken);
                await PollWorkItemUpdatesAsync(stoppingToken);
                
                _logger.LogInformation("✅ Automation cycle complete. Sleeping for 1 minute...");
                await _events.EmitAsync("system", "completed", "✅ Cycle Complete", "Sleeping for 1 minute before next scan");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "⚠️ Critical error in poll loop. The service will attempt to recover in the next cycle.");
                await SendTmaAlertAsync(_tmaEmail, "Main Service Loop", null, null, ex, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("🛑 MailPollingService stopping.");
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
                    config.QueryParameters.Select = ["id", "subject", "from", "receivedDateTime", "bodyPreview", "isRead", "conversationId", "hasAttachments"];
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
            await SendTmaAlertAsync(_tmaEmail, "Mailbox Polling", null, null, ex, cancellationToken);
        }
    }

    private async Task PollWorkItemUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔍 Checking Azure DevOps for any state updates on tracked tickets...");
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
                            await _events.EmitAsync("system", "info", "🔄 ADO State Synced", $"WorkItem #{item.Id}: {ticket.AdoItemState} ➔ {state}");
                            
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
                        await SendTmaAlertAsync(_tmaEmail, "Sync ADO to DB", $"WorkItem #{item.Id}", null, dbEx, cancellationToken);
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
                                // Look up the original MessageId so we can reply in the same thread
                                string? originalMessageId = null;
                                try
                                {
                                    using var replyScope = _scopeFactory.CreateScope();
                                    var replyDb = replyScope.ServiceProvider.GetRequiredService<AppDbContext>();
                                    var dbTicket = await replyDb.Tickets.FirstOrDefaultAsync(
                                        t => t.AdoWorkItemId == item.Id, cancellationToken);
                                    originalMessageId = dbTicket?.MessageId;
                                }
                                catch (Exception dbEx)
                                {
                                    _logger.LogWarning(dbEx, "Could not retrieve MessageId for WorkItem #{Id}. Will send as new email.", item.Id);
                                    await _events.EmitAsync("system", "info", "⚠️ Missing MessageId", $"WorkItem #{item.Id}. Sending state update as new email instead of thread.");
                                }

                                await SendAutoReplyAsync(
                                    originalMessageId,
                                    email,
                                    string.IsNullOrEmpty(name) ? "User" : name,
                                    title,
                                    item.Id.Value.ToString(),
                                    state,
                                    null,
                                    null,
                                    cancellationToken);

                                await _adoService.AddWorkItemTagAsync(item.Id.Value, tags, expectedTag, cancellationToken);
                                _logger.LogInformation("Sent state update for WorkItem #{Id} to {State} ({Email})", item.Id, state, email);
                                await _events.EmitAsync("ado", "completed", "📬 State Update Email Sent", $"WorkItem #{item.Id} is now {state}. Email sent to {email}");
                            }
                        }
                    }
                }
            }
            _logger.LogInformation("✅ Azure DevOps sync finished. Processed {Count} work items.", updatedItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while polling work item updates");
            await SendTmaAlertAsync(_tmaEmail, "Polling Work Item Updates", null, null, ex, cancellationToken);
        }
    }

    private static string ExtractHtmlAttribute(string html, string attributeName)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(html, $@"{attributeName}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            if (match.Groups[1].Success) return match.Groups[1].Value;
            if (match.Groups[2].Success) return match.Groups[2].Value;
            if (match.Groups[3].Success) return match.Groups[3].Value;
        }
        return string.Empty;
    }

    private async Task ProcessEmailAsync(Message msg, CancellationToken cancellationToken)
    {
        var senderEmail = msg.From?.EmailAddress?.Address;
        var senderName = msg.From?.EmailAddress?.Name ?? senderEmail ?? "Unknown";
        var conversationId = msg.ConversationId;

        _logger.LogInformation(
            "New email - Subject: {Subject}, From: {From}, Received: {ReceivedAt}, ConversationId: {ConvId}",
            msg.Subject,
            senderEmail,
            msg.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss"),
            conversationId);
        await _events.EmitAsync("inbox", "started", "📨 New Email Received", $"From: {senderEmail}", msg.Subject, senderEmail);
        if (senderEmail != null && _allowedDomains.Any(domain => senderEmail.EndsWith(domain, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Email from allowed domain {SenderEmail}. Processing...", senderEmail);
            await _events.EmitAsync("system", "info", "✅ Domain Validated", $"Sender {senderEmail} is authorized.", msg.Subject, senderEmail);
        }
        else
        {
            _logger.LogWarning("Email from unauthorized domain {SenderEmail}. Skipping...", senderEmail);
            await _events.EmitAsync("system", "failed", "🛑 Unauthorized Domain", $"Ignored email from {senderEmail}.", msg.Subject, senderEmail);
            await MarkAsReadAsync(msg.Id!, cancellationToken);
            return;
        }
        // Fetch the full message to get the Body details
        var fullMessage = await _graphClient
            .Users[_mailboxUser]
            .Messages[msg.Id]
            .GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "subject", "body"];
            }, cancellationToken);

        var emailBody = fullMessage?.Body?.Content ?? msg.BodyPreview ?? "No content";

        // ═══════════════════════════════════════════════════════════════
        // THREAD DETECTION — Check if this email belongs to an existing ticket
        // ═══════════════════════════════════════════════════════════════
        if (!string.IsNullOrEmpty(conversationId))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var existingTicket = await db.Tickets.FirstOrDefaultAsync(
                    t => t.ConversationId == conversationId, cancellationToken);

                if (existingTicket != null && existingTicket.AdoWorkItemId.HasValue)
                {
                    await _events.EmitAsync("inbox", "info", "🔗 Thread Reply Detected", $"Follow-up on ADO #{existingTicket.AdoWorkItemId}", msg.Subject, senderEmail);
                    _logger.LogInformation(
                        "🔗 Thread reply detected! ConversationId {ConvId} matches existing Ticket {TicketId} (ADO #{AdoId})",
                        conversationId, existingTicket.TicketId, existingTicket.AdoWorkItemId);

                    // Use LLM to summarize the follow-up reply
                    var summary = await _llmService.SummarizeFollowUpAsync(emailBody, cancellationToken);
                    _logger.LogInformation("📝 LLM Follow-up Summary: {Summary}", summary);
                    await _events.EmitAsync("llm", "completed", "📝 Reply Summarized", summary, msg.Subject, senderEmail);

                    // Append as a styled HTML comment to the ADO Work Item
                    var commentHtml = $"<div style='border-left: 4px solid #3b82f6; padding: 12px; margin: 8px 0; background: #eff6ff;'>" +
                                      $"<strong>📧 Follow-up from {System.Web.HttpUtility.HtmlEncode(senderName)} ({System.Web.HttpUtility.HtmlEncode(senderEmail)})" +
                                      $" — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</strong><br/><br/>" +
                                      $"{System.Web.HttpUtility.HtmlEncode(summary)}</div>";

                    await _adoService.AddWorkItemCommentAsync(
                        existingTicket.AdoWorkItemId.Value, commentHtml, cancellationToken);

                    // Fetch and attach any new attachments from the follow-up
                    await HandleAttachmentsAsync(msg.Id!, existingTicket.AdoWorkItemId.Value, null, cancellationToken);

                    // Send acknowledgment reply to the client (stays in the same thread)
                    var ackHtml = $@"
                        <div style='font-family: Segoe UI, Arial, sans-serif; padding: 20px;'>
                            <p>Hi <strong>{System.Web.HttpUtility.HtmlEncode(senderName)}</strong>,</p>
                            <p>Thank you for your follow-up. Your additional information has been received and appended to your existing ticket 
                            <strong>#{existingTicket.AdoWorkItemId}</strong>.</p>
                            <p>Our support team is actively working on your case and will get back to you as soon as possible.</p>
                            <hr style='border: none; border-top: 1px solid #e5e7eb; margin: 16px 0;'/>
                            <p style='font-size: 12px; color: #6b7280;'>This is an automated acknowledgment. Please do not reply to this email if your issue has already been resolved.</p>
                        </div>";

                    await _graphClient
                        .Users[_mailboxUser]
                        .Messages[msg.Id]
                        .Reply
                        .PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.Reply.ReplyPostRequestBody
                        {
                            Message = new Message
                            {
                                Body = new ItemBody { ContentType = BodyType.Html, Content = ackHtml }
                            },
                            Comment = ""
                        }, cancellationToken: cancellationToken);

                    _logger.LogInformation("📩 Acknowledgment reply sent to {Email} for follow-up on ADO #{AdoId}", senderEmail, existingTicket.AdoWorkItemId);

                    await MarkAsReadAsync(msg.Id!, cancellationToken);
                    _logger.LogInformation("✅ Follow-up appended to ADO #{AdoId}. No duplicate ticket created.", existingTicket.AdoWorkItemId);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "⚠️ Thread detection failed for ConversationId {ConvId}. Proceeding with new ticket creation.", conversationId);
                await _events.EmitAsync("system", "failed", "⚠️ Thread Detection Failed", $"Creating new ticket instead. Error: {ex.Message}", msg.Subject, senderEmail);
            }
        }

        var workItemId = 0;
        var initialState = "To Do";
        var assigneeEmail = _defaultAssignee;
        ExtractedEmailData? extractedData = null;
        RagVerdict? ragVerdict = null;
        Guid ticketId = Guid.NewGuid();

        // ═══════════════════════════════════════════════════════════════
        // STEP 1 — LLM Analysis
        // ═══════════════════════════════════════════════════════════════
        await _events.EmitAsync("llm", "started", "🧠 LLM Analysis Started", "Extracting metadata via Groq AI...", msg.Subject, senderEmail);
        try
        {
            var supportedFields = _jobFieldService.GetAllJobFields();
            extractedData = await _llmService.AnalyzeEmailAsync(
                msg.Subject ?? "No Subject",
                emailBody,
                supportedFields,
                cancellationToken);

            assigneeEmail = _jobFieldService.ResolveEmail(extractedData.JobField, _defaultAssignee);

            _logger.LogInformation(
                "LLM analysis complete — Severity: {Severity}, JobField: {JobField}, Assignee: {Assignee}",
                extractedData.Severity,
                extractedData.JobField,
                assigneeEmail);
            await _events.EmitAsync("llm", "completed", "✅ LLM Analysis Complete", $"Department: {extractedData.JobField} | Severity: {extractedData.Severity} | Assignee: {assigneeEmail}", msg.Subject, senderEmail, System.Text.Json.JsonSerializer.Serialize(new { extractedData.JobField, extractedData.Severity, Assignee = assigneeEmail, extractedData.CoreProblem }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STEP 1 FAILED — LLM analysis error for email: {Subject}", msg.Subject);
            await _events.EmitAsync("llm", "failed", "❌ LLM Analysis Failed", ex.Message, msg.Subject, senderEmail);
            await SendTmaAlertAsync(_tmaEmail, "LLM Analysis", msg.Subject, senderEmail, ex, cancellationToken);
            await MarkAsReadAsync(msg.Id!, cancellationToken);
            return;
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 1.5 — RAG Verdict (AI Auto-Resolve Evaluation)
        // ═══════════════════════════════════════════════════════════════
        try
        {
            if (extractedData != null && !string.IsNullOrEmpty(extractedData.DetailedDescription))
            {
                ragVerdict = await _llmService.EvaluateRagSolutionAsync(
                    extractedData.DetailedDescription, 
                    senderEmail,
                    cancellationToken);

                if (ragVerdict.HasSolution && ragVerdict.ConfidenceScore > 0.70)
                {
                    await _events.EmitAsync("rag", "completed", "🎯 AI Solution Found!", $"Confidence: {ragVerdict.ConfidenceScore:P0} — Proposing solution to client", msg.Subject, senderEmail);
                    _logger.LogInformation("🎯 PERFECT MATCH! AI Found a solution with Confidence: {Conf}", ragVerdict.ConfidenceScore);
                    
                    // Prepend the AI solution to the detailed description so Azure DevOps logs it clearly!
                    var cleanSolution = System.Web.HttpUtility.HtmlEncode(ragVerdict.ProposedSolution).Replace("\n", "<br>");
                    extractedData.DetailedDescription = $"<h3>🤖 AI AUTO-RESOLVED SOLUTION 🤖</h3>" +
                                                        $"<p><strong>Solution:</strong><br>{cleanSolution}</p>" +
                                                        $"<p><strong>References:</strong><br>{string.Join("<br>", ragVerdict.ReferenceUrls)}</p><hr><br>" + 
                                                        extractedData.DetailedDescription;
                }
                else if (!string.IsNullOrEmpty(ragVerdict.ProposedSolution) && 
                         ragVerdict.ToolUsed.Contains("search_historical_graph_knowledge") &&
                         !ragVerdict.ProposedSolution.Contains("No historical data found"))
                {
                    // IT-ONLY HISTORICAL FIX! We don't send this to the client (HasSolution = false),
                    // but we MUST inject it into the ADO ticket and IT engineer email!
                    _logger.LogInformation("🔧 IT-Only Solution found. Injecting into ADO WorkItem description.");
                    var cleanSolution = System.Web.HttpUtility.HtmlEncode(ragVerdict.ProposedSolution).Replace("\n", "<br>");
                    extractedData.DetailedDescription = $"<h3>🔧 AI HISTORICAL IT FIX (DO NOT SHARE WITH CLIENT) 🔧</h3>" +
                                                        $"<p><strong>Historical Resolution:</strong><br>{cleanSolution}</p><hr><br>" + 
                                                        extractedData.DetailedDescription;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ STEP 1.5 FAILED — RAG Evaluation error for email: {Subject}. Pipeline will continue normally.", msg.Subject);
            await _events.EmitAsync("rag", "failed", "⚠️ RAG Evaluation Error", ex.Message, msg.Subject, senderEmail);
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 2 — Persist to PostgreSQL (DATABASE FIRST)
        // ═══════════════════════════════════════════════════════════════
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ticket = new Ticket
            {
                TicketId = ticketId,
                MessageId = msg.Id ?? "Unknown",
                ConversationId = conversationId,
                SenderEmail = senderEmail ?? "Unknown",
                Subject = msg.Subject ?? "No Subject",
                BodyExcerpt = emailBody.Length > 4000 ? emailBody[..3997] + "..." : emailBody,
                ReceivedAt = msg.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                CurrentPipelineStatus = PipelineStatus.LlmSuccess,

                // Extracted data
                ExtractedDepartment = extractedData.JobField,
                ExtractedIntent = extractedData.CoreProblem,
                LlmConfidenceScore = extractedData.Confidence,

                // ADO mapping — not yet created, will be back-filled in Step 4
                AdoWorkItemId = null,
                AdoAssignee = assigneeEmail,
                AdoItemState = null,
                AdoUrl = null
            };

            ticket.StateLog.Add(new TicketStateLog
            {
                TicketId = ticket.TicketId,
                PipelineStatus = PipelineStatus.LlmSuccess,
                CreatedAt = DateTime.UtcNow
            });

            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("✅ Ticket {TicketId} persisted to PostgreSQL database (pre-ADO).", ticketId);
            await _events.EmitAsync("db", "completed", "💾 Saved to Database", $"Ticket {ticketId:N} persisted to PostgreSQL", msg.Subject, senderEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STEP 2 FAILED — Database persist error for email: {Subject}", msg.Subject);
            await _events.EmitAsync("db", "failed", "❌ Database Error", ex.Message, msg.Subject, senderEmail);
            await SendTmaAlertAsync(_tmaEmail, "Database Persist", msg.Subject, senderEmail, ex, cancellationToken);
            await MarkAsReadAsync(msg.Id!, cancellationToken);
            return;
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 3 — Mark email as read (after successful DB persist)
        // ═══════════════════════════════════════════════════════════════
        await MarkAsReadAsync(msg.Id!, cancellationToken);

        // ═══════════════════════════════════════════════════════════════
        // STEP 4 — Create Azure DevOps Work Item + Update DB
        // ═══════════════════════════════════════════════════════════════
        try
        {
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
            await _events.EmitAsync("ado", "completed", $"📋 ADO Work Item #{workItemId} Created", $"State: {initialState} | Assigned to: {assigneeEmail}", msg.Subject, senderEmail);

            // Back-fill the DB ticket with ADO data
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken);
                if (ticket != null)
                {
                    ticket.AdoWorkItemId = workItemId > 0 ? workItemId : null;
                    ticket.AdoItemState = initialState;
                    ticket.AdoUrl = workItemId > 0 ? $"https://dev.azure.com/yessinefakhfakh/PFE-automation/_workitems/edit/{workItemId}" : null;
                    
                    if (ragVerdict != null && ragVerdict.HasSolution && ragVerdict.ConfidenceScore > 0.70)
                    {
                        ticket.CurrentPipelineStatus = PipelineStatus.PendingClientValidation;
                    }
                    else
                    {
                        ticket.CurrentPipelineStatus = PipelineStatus.AdoCreated;
                    }
                    
                    ticket.LastUpdatedAt = DateTime.UtcNow;

                    ticket.StateLog.Add(new TicketStateLog
                    {
                        TicketId = ticket.TicketId,
                        PipelineStatus = ticket.CurrentPipelineStatus,
                        CreatedAt = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("✅ Ticket {TicketId} updated with ADO WorkItem #{WorkItemId}.", ticketId, workItemId);
                }
            }

            // Handle attachments (inline images + file attachments)
            if (workItemId > 0)
            {
                await HandleAttachmentsAsync(msg.Id!, workItemId, null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STEP 4 FAILED — ADO work item creation error for email: {Subject}", msg.Subject);
            await _events.EmitAsync("ado", "failed", "❌ ADO Creation Failed", ex.Message, msg.Subject, senderEmail);

            // Update DB ticket to reflect the ADO failure
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken);
                if (ticket != null)
                {
                    ticket.CurrentPipelineStatus = PipelineStatus.AdoFailed;
                    ticket.LastUpdatedAt = DateTime.UtcNow;
                    ticket.StateLog.Add(new TicketStateLog
                    {
                        TicketId = ticket.TicketId,
                        PipelineStatus = PipelineStatus.AdoFailed,
                        ErrorMessage = ex.Message,
                        CreatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to update ticket status to AdoFailed in database.");
            }

            await SendTmaAlertAsync(_tmaEmail, "ADO Work Item Creation", msg.Subject, senderEmail, ex, cancellationToken);
            return;
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 5 — Send Emails (auto-reply + assignee notification)
        //          On error: alert TMA but do NOT abort
        // ═══════════════════════════════════════════════════════════════
        try
        {
            // Auto-reply to original sender (using .Reply to stay in the same thread)
            if (!string.IsNullOrEmpty(senderEmail) && workItemId > 0)
            {
                await SendAutoReplyAsync(
                    msg.Id!,
                    senderEmail,
                    senderName ?? "Unknown",
                    msg.Subject,
                    workItemId.ToString(),
                    initialState,
                    extractedData,
                    ragVerdict,
                    cancellationToken);
                _logger.LogInformation("Auto-reply sent to: {Email}", senderEmail);
                await _events.EmitAsync("notify", "completed", "📩 Auto-Reply Sent", $"HTML response sent to {senderEmail}", msg.Subject, senderEmail);
            }

            // Assignee notification (if different from default)
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

            // Microsoft Teams Notification (via Job Mapping)
            // Send Adaptive Card alert to the correct Teams Channel Webhook
            var deptMapping = _jobFieldService.GetMapping(extractedData.JobField);
            if (deptMapping != null && !string.IsNullOrWhiteSpace(deptMapping.WebhookUrl))
            {
                bool isRagResolved = ragVerdict != null && ragVerdict.HasSolution && ragVerdict.ConfidenceScore > 0.70;
                await SendTeamsNotificationAsync(
                    deptMapping.WebhookUrl,
                    workItemId.ToString(),
                    extractedData,
                    senderName ?? "Unknown",
                    senderEmail,
                    isRagResolved,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STEP 5 FAILED — Mail sending error for email: {Subject}", msg.Subject);
            await _events.EmitAsync("notify", "failed", "❌ Email Notification Failed", ex.Message, msg.Subject, senderEmail);

            // Update DB ticket to reflect mail sending failure
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken);
                if (ticket != null)
                {
                    ticket.CurrentPipelineStatus = PipelineStatus.MailSendingFailed;
                    ticket.LastUpdatedAt = DateTime.UtcNow;
                    ticket.StateLog.Add(new TicketStateLog
                    {
                        TicketId = ticket.TicketId,
                        PipelineStatus = PipelineStatus.MailSendingFailed,
                        ErrorMessage = ex.Message,
                        CreatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to update ticket status to MailSendingFailed in database.");
            }

            // Alert TMA but do NOT abort — ticket and ADO item are already safe
            await SendTmaAlertAsync(_tmaEmail, "Mail Sending (Auto-Reply / Assignee Notification)", msg.Subject, senderEmail, ex, cancellationToken);
        }

        _logger.LogInformation("✅ Full pipeline complete for email: {Subject} (Ticket: {TicketId}, ADO: #{WorkItemId})", msg.Subject, ticketId, workItemId);
        await _events.EmitAsync("complete", "completed", "🎉 Pipeline Complete!", $"Ticket processed successfully — ADO #{workItemId}", msg.Subject, senderEmail);
    }

    /// <summary>
    /// Fetches email attachments via Microsoft Graph and handles them:
    /// - Inline images: Embeds as base64 in an ADO comment for visibility
    /// - File attachments: Uploads to ADO Work Item as linked attachments
    /// </summary>
    private async Task HandleAttachmentsAsync(string messageId, int workItemId, System.Text.StringBuilder? descBuilder, CancellationToken cancellationToken)
    {
        try
        {
            var attachments = await _graphClient
                .Users[_mailboxUser]
                .Messages[messageId]
                .Attachments
                .GetAsync(cancellationToken: cancellationToken);

            if (attachments?.Value is null || attachments.Value.Count == 0) return;

            _logger.LogInformation("📎 Found {Count} attachment(s) for message {MessageId}", attachments.Value.Count, messageId);

            foreach (var attachment in attachments.Value)
            {
                if (attachment is Microsoft.Graph.Models.FileAttachment fileAttachment && fileAttachment.ContentBytes != null)
                {
                    var fileName = fileAttachment.Name ?? "attachment";
                    var contentType = fileAttachment.ContentType ?? "application/octet-stream";
                    var sizeKb = fileAttachment.ContentBytes.Length / 1024.0;

                    // Skip oversized attachments (> 4MB) to prevent ADO storage issues
                    if (fileAttachment.ContentBytes.Length > 4 * 1024 * 1024)
                    {
                        _logger.LogWarning("⚠️ Skipping oversized attachment '{FileName}' ({SizeKb:F0} KB)", fileName, sizeKb);
                        continue;
                    }

                    if (fileAttachment.IsInline == true && contentType.StartsWith("image/"))
                    {
                        // Inline image → Append as base64 HTML comment to ADO
                        var base64 = Convert.ToBase64String(fileAttachment.ContentBytes);
                        var imgHtml = $"<div style='margin: 8px 0; padding: 12px; border: 1px solid #e5e7eb; border-radius: 8px;'>" +
                                      $"<strong>📷 Inline Image: {System.Web.HttpUtility.HtmlEncode(fileName)}</strong><br/>" +
                                      $"<img src='data:{contentType};base64,{base64}' style='max-width: 600px; margin-top: 8px;' alt='{System.Web.HttpUtility.HtmlEncode(fileName)}'/>" +
                                      $"</div>";

                        await _adoService.AddWorkItemCommentAsync(workItemId, imgHtml, cancellationToken);
                        _logger.LogInformation("🖼️ Inline image '{FileName}' ({SizeKb:F0} KB) embedded in ADO #{WorkItemId}", fileName, sizeKb, workItemId);
                    }
                    else
                    {
                        // File attachment → Upload to ADO
                        await _adoService.UploadAttachmentAsync(workItemId, fileName, fileAttachment.ContentBytes, cancellationToken);
                        _logger.LogInformation("📄 File '{FileName}' ({SizeKb:F0} KB) uploaded to ADO #{WorkItemId}", fileName, sizeKb, workItemId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Error handling attachments for message {MessageId}. Pipeline continues.", messageId);
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
        string? originalMessageId,
        string recipientEmail,
        string recipientName,
        string? originalSubject,
        string ticketId,
        string ticketStatus,
        ExtractedEmailData? extractedData,
        RagVerdict? ragVerdict,
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

          string aiSolutionHtml = "";
          if (ragVerdict != null && ragVerdict.HasSolution && ragVerdict.ConfidenceScore > 0.70)
          {
              var refs = string.Join("<br>", ragVerdict.ReferenceUrls.Select(url => $"<a href='{url}' style='color: #16a34a;'>{url}</a>"));
              
              var clientSolution = ragVerdict.ProposedSolution;
              if (clientSolution.Contains("=== AI GRAPH RECONNAISSANCE ==="))
              {
                  var gatewaysIndex = clientSolution.IndexOf("- Tenant Servers/Gateways:");
                  if (gatewaysIndex != -1)
                  {
                      var newlineIndex = clientSolution.IndexOf('\n', gatewaysIndex);
                      if (newlineIndex != -1)
                      {
                          clientSolution = clientSolution.Substring(newlineIndex + 1).Trim();
                          
                          // If the LLM included a separator line like equal signs or hyphens, strip it as well
                          if (clientSolution.StartsWith("=") || clientSolution.StartsWith("-") || clientSolution.StartsWith("*"))
                          {
                              var separatorNewline = clientSolution.IndexOf('\n');
                              if (separatorNewline != -1)
                              {
                                  clientSolution = clientSolution.Substring(separatorNewline + 1).Trim();
                              }
                          }
                      }
                  }
              }
              var escapedSolution = System.Web.HttpUtility.HtmlEncode(clientSolution).Replace("\n", "<br>");

              aiSolutionHtml = $@"
              <div style='background-color: #dcfce7; border: 1px solid #22c55e; border-radius: 8px; padding: 24px; margin: 0 32px 24px; text-align: left;'>
                  <h3 style='color: #166534; margin-bottom: 12px; font-size: 18px;'>🤖 We have an instant solution for your issue!</h3>
                  <p style='color: #15803d; font-size: 15px; margin-bottom: 16px; line-height: 1.5;'>{escapedSolution}</p>
                  
                  <div style='margin-top: 24px; margin-bottom: 24px; padding: 16px; background-color: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 8px; text-align: center;'>
                      <p style='color: #166534; font-size: 14px; font-weight: bold; margin-bottom: 16px;'>Did this AI solution solve your problem?</p>
                      <a href='{_baseAppUrl}/api/ticket/{ticketId}/validate?accepted=true' style='display: inline-block; background-color: #22c55e; color: white; padding: 10px 20px; text-decoration: none; border-radius: 6px; font-weight: bold; margin-right: 12px;'>✅ Yes, close ticket</a>
                      <a href='{_baseAppUrl}/api/ticket/{ticketId}/validate?accepted=false' style='display: inline-block; background-color: #ef4444; color: white; padding: 10px 20px; text-decoration: none; border-radius: 6px; font-weight: bold;'>❌ No, I need support</a>
                  </div>

                  <div style='font-size: 13px; color: #166534; padding-top: 10px; border-top: 1px solid #86efac;'>
                      <strong>Official Documentation:</strong><br>{refs}
                  </div>
              </div>";
          }

          string heroText = $"Hi <span class=\"username\">{recipientName}</span>, your request point has been secured. Our technical team is actively reviewing your issue parameters.";
          string adaptiveCardScript = "";

          if (ticketStatus.Equals("Done", StringComparison.OrdinalIgnoreCase) || 
              ticketStatus.Equals("Closed", StringComparison.OrdinalIgnoreCase))
          {
              heroText = $"Hi <span class=\"username\">{recipientName}</span>, we are confirming that your issue is officially closed. If you have another problem, please don't hesitate to contact us.";
              
              adaptiveCardScript = $@"
              <script type=""application/adaptivecard+json"">
              {{
                  ""type"": ""AdaptiveCard"",
                  ""version"": ""1.0"",
                  ""originator"": ""0750482f-1650-4378-b145-f8efedf44dde"",
                  ""hideOriginalBody"": false,
                  ""body"": [
                      {{
                          ""type"": ""Container"",
                          ""style"": ""emphasis"",
                          ""bleed"": true,
                          ""items"": [
                              {{
                                  ""type"": ""ColumnSet"",
                                  ""columns"": [
                                      {{
                                          ""type"": ""Column"",
                                          ""width"": ""auto"",
                                          ""items"": [
                                              {{
                                                  ""type"": ""Image"",
                                                  ""url"": ""{_logoUrl}"",
                                                  ""size"": ""Medium""
                                              }}
                                          ]
                                      }},
                                      {{
                                          ""type"": ""Column"",
                                          ""width"": ""stretch"",
                                          ""verticalContentAlignment"": ""Center"",
                                          ""items"": [
                                              {{
                                                  ""type"": ""TextBlock"",
                                                  ""text"": ""Helpdesk Feedback"",
                                                  ""weight"": ""Bolder"",
                                                  ""size"": ""Large"",
                                                  ""color"": ""Accent""
                                              }},
                                              {{
                                                  ""type"": ""TextBlock"",
                                                  ""text"": ""Ticket #{ticketId} has been closed. We'd love to hear about your experience!"",
                                                  ""isSubtle"": true,
                                                  ""wrap"": true
                                              }}
                                          ]
                                      }}
                                  ]
                              }}
                          ]
                      }},
                      {{
                          ""type"": ""TextBlock"",
                          ""text"": ""How would you rate the support you received?"",
                          ""weight"": ""Bolder"",
                          ""size"": ""Medium"",
                          ""spacing"": ""Medium""
                      }},
                      {{
                          ""type"": ""Input.ChoiceSet"",
                          ""id"": ""rating"",
                          ""style"": ""expanded"",
                          ""choices"": [
                              {{ ""title"": ""⭐⭐⭐⭐⭐  Excellent & Fast"", ""value"": ""5"" }},
                              {{ ""title"": ""⭐⭐⭐⭐  Good"", ""value"": ""4"" }},
                              {{ ""title"": ""⭐⭐⭐  Average"", ""value"": ""3"" }},
                              {{ ""title"": ""⭐⭐  Fair"", ""value"": ""2"" }},
                              {{ ""title"": ""⭐  Poor"", ""value"": ""1"" }}
                          ]
                      }},
                      {{
                          ""type"": ""TextBlock"",
                          ""text"": ""Any additional comments? (Optional)"",
                          ""weight"": ""Bolder"",
                          ""spacing"": ""Medium""
                      }},
                      {{
                          ""type"": ""Input.Text"",
                          ""id"": ""comment"",
                          ""placeholder"": ""Tell us what we did well or how we can improve..."",
                          ""isMultiline"": true
                      }}
                  ],
                  ""actions"": [
                      {{
                          ""type"": ""Action.Http"",
                          ""title"": ""Submit Feedback"",
                          ""method"": ""POST"",
                          ""url"": ""{_baseAppUrl}/api/ticket/{ticketId}/feedback"",
                          ""body"": ""{{ \""rating\"": \""{{{{rating.value}}}}\"", \""comment\"": \""{{{{comment.value}}}}\"" }}"",
                          ""headers"": [
                              {{ ""name"": ""Content-Type"", ""value"": ""application/json"" }}
                          ]
                      }}
                  ]
              }}
              </script>";
          }

          var htmlContent = _autoReplyTemplate
              .Replace("{{AdaptiveCardScript}}", adaptiveCardScript)
              .Replace("{{HeroText}}", heroText)
              .Replace("{{LogoUrl}}", _logoUrl)
              .Replace("{{AiSolutionHtml}}", aiSolutionHtml)
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
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = htmlContent
            }
        };

        // Use .Reply if we have the original message ID (keeps same thread),
        // otherwise fall back to SendMail for state-change notifications
        if (!string.IsNullOrEmpty(originalMessageId))
        {
            await _graphClient
                .Users[_mailboxUser]
                .Messages[originalMessageId]
                .Reply
                .PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.Reply.ReplyPostRequestBody
                {
                    Message = replyMessage,
                    Comment = ""
                }, cancellationToken: cancellationToken);
        }
        else
        {
            replyMessage.Subject = $"Re: {subject} [TKT-{ticketId}]";
            replyMessage.ToRecipients =
            [
                new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = recipientEmail,
                        Name = recipientName
                    }
                }
            ];

            await _graphClient
                .Users[_mailboxUser]
                .SendMail
                .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                {
                    Message = replyMessage,
                    SaveToSentItems = true
                }, cancellationToken: cancellationToken);
        }
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

    private async Task SendTmaAlertAsync(
        string tmaEmail,
        string failedStep,
        string? emailSubject,
        string? senderEmail,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var htmlContent = $@"
                <html>
                <body style=""font-family: Arial, sans-serif; padding: 20px;"">
                    <h2 style=""color: #d32f2f;"">⚠️ Pipeline Error Alert</h2>
                    <p>An error occurred in the automated helpdesk pipeline. Immediate attention may be required.</p>
                    <table style=""border-collapse: collapse; width: 100%; max-width: 600px;"">
                        <tr style=""background: #fce4ec;"">
                            <td style=""padding: 10px; border: 1px solid #ddd; font-weight: bold;"">Failed Step</td>
                            <td style=""padding: 10px; border: 1px solid #ddd;"">{failedStep}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 10px; border: 1px solid #ddd; font-weight: bold;"">Email Subject</td>
                            <td style=""padding: 10px; border: 1px solid #ddd;"">{emailSubject ?? "N/A"}</td>
                        </tr>
                        <tr style=""background: #f5f5f5;"">
                            <td style=""padding: 10px; border: 1px solid #ddd; font-weight: bold;"">Sender</td>
                            <td style=""padding: 10px; border: 1px solid #ddd;"">{senderEmail ?? "N/A"}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 10px; border: 1px solid #ddd; font-weight: bold;"">Error Type</td>
                            <td style=""padding: 10px; border: 1px solid #ddd;"">{exception.GetType().Name}</td>
                        </tr>
                        <tr style=""background: #f5f5f5;"">
                            <td style=""padding: 10px; border: 1px solid #ddd; font-weight: bold;"">Error Message</td>
                            <td style=""padding: 10px; border: 1px solid #ddd;"">{System.Web.HttpUtility.HtmlEncode(exception.Message)}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 10px; border: 1px solid #ddd; font-weight: bold;"">Timestamp (UTC)</td>
                            <td style=""padding: 10px; border: 1px solid #ddd;"">{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</td>
                        </tr>
                    </table>
                    <hr style=""margin-top: 20px;""/>
                    <p style=""font-size: 12px; color: #888;"">This is an automated alert from the Helpdesk Automation Pipeline.</p>
                </body>
                </html>";

            var message = new Message
            {
                Subject = $"🚨 Pipeline Error: {failedStep} — {emailSubject ?? "Unknown"}",
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlContent
                },
                ToRecipients =
                [
                    new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = tmaEmail }
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

            _logger.LogInformation("🚨 TMA alert sent to {TmaEmail} for failed step: {FailedStep}", tmaEmail, failedStep);
            await _events.EmitAsync("system", "failed", "🚨 TMA Alert Sent", $"Pipeline halted at {failedStep}. Alert sent to Support Team.", emailSubject ?? "Unknown", tmaEmail);
        }
        catch (Exception ex)
        {
            // If we can't even send the alert, log it — but don't throw
            _logger.LogCritical(ex, "CRITICAL: Failed to send TMA alert email to {TmaEmail}. Original error step: {FailedStep}", tmaEmail, failedStep);
        }

        // Abort the process immediately as requested to prevent cascading errors
        _logger.LogCritical("🛑 Halting process immediately due to pipeline error in step: {Step}", failedStep);
        Environment.Exit(1);
    }

    private async Task SendTeamsNotificationAsync(
        string webhookUrl,
        string ticketId,
        ExtractedEmailData extractedData,
        string senderName,
        string senderEmail,
        bool isRagResolved,
        CancellationToken cancellationToken)
    {
        try
        {
            var ticketUrl = $"https://dev.azure.com/yessinefakhfakh/PFE-automation/_workitems/edit/{ticketId}";
            var themeColor = isRagResolved ? "good" : "warning";
            var statusText = isRagResolved ? "Instant RAG Solution Suggested ✅" : "Needs Human Assignment ⚠️";
            var descriptionJson = System.Text.Json.JsonSerializer.Serialize(extractedData.Description ?? "No description");
            var titleJson = System.Text.Json.JsonSerializer.Serialize(extractedData.CoreProblem ?? "New Ticket");

            var cardJson = $$""""
            {
                "type": "AdaptiveCard",
                "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                "version": "1.4",
                "body": [
                    {
                        "type": "Container",
                        "style": "{{themeColor}}",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "🎯 New Azure DevOps Ticket #{{ticketId}}",
                                "weight": "Bolder",
                                "size": "Large"
                            }
                        ]
                    },
                    {
                        "type": "TextBlock",
                        "text": {{titleJson}},
                        "weight": "Bolder",
                        "size": "Medium",
                        "wrap": true
                    },
                    {
                        "type": "FactSet",
                        "facts": [
                            { "title": "Sender:", "value": "{{senderName}} ({{senderEmail}})" },
                            { "title": "Severity:", "value": "{{extractedData.Severity}}" },
                            { "title": "Priority:", "value": "P{{extractedData.GetPriority()}}" },
                            { "title": "Job Field:", "value": "{{extractedData.JobField}}" },
                            { "title": "Status:", "value": "{{statusText}}" }
                        ]
                    },
                    {
                        "type": "TextBlock",
                        "text": {{descriptionJson}},
                        "wrap": true,
                        "isSubtle": true
                    }
                ],
                "actions": [
                    {
                        "type": "Action.OpenUrl",
                        "title": "Open Work Item in ADO",
                        "url": "{{ticketUrl}}"
                    }
                ]
            }
            """";

            // Wrap the Adaptive Card in the mandatory 'message' format for Power Automate Webhooks
            var payloadJson = $$""""
            {
                "type": "message",
                "attachments": [
                    {
                        "contentType": "application/vnd.microsoft.card.adaptive",
                        "contentUrl": null,
                        "content": {{cardJson}}
                    }
                ]
            }
            """";

            using var httpClient = new HttpClient();
            var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(webhookUrl, content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("✅ Sent Adaptive Card notification to Webhook for ticket {TicketId}", ticketId);
            }
            else
            {
                var errorString = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("⚠️ Failed to send Teams notification. Status: {Status}, Error: {Error}", response.StatusCode, errorString);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send Teams notification to Webhook. Exception: {Message}", ex.Message);
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
