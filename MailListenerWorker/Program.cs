using MailListenerWorker.Data;
using MailListenerWorker.Services;
using MailListenerWorker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── JSON Enum Serialization ──────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// ── Entity Framework Core + PostgreSQL ───────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Business Services ────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AzureDevOpsService>();
builder.Services.AddSingleton<GroqLlmService>();
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<JobFieldMappingService>>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var csvPath = configuration["JobFieldCsv:Path"] ?? "departements.csv";
    return new JobFieldMappingService(logger, csvPath);
});

// ── Real-Time Pipeline Events (SSE) ────────────────────
builder.Services.AddSingleton<PipelineEventService>();

// ── Background Worker ───────────────────────────────────
builder.Services.AddHostedService<MailPollingService>();

// ── Dashboard API Support ─────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("AllowAll");

// ── Minimal API: Get Tickets ──────────────────────────
app.MapGet("/api/tickets", async (AppDbContext db) =>
{
    return await db.Tickets
        .OrderByDescending(t => t.ReceivedAt)
        .Take(50)
        .ToListAsync();
});

// ── Minimal API: Stats (Comprehensive — Real Data) ───
app.MapGet("/api/stats", async (AppDbContext db) =>
{
    var allTickets = await db.Tickets.ToListAsync();
    var total = allTickets.Count;

    // ── Pipeline Stage Counts ──────────────────────────
    var inbox = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.EmailReceived);
    var llmProcessing = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.LlmProcessing);
    var llmSuccess = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.LlmSuccess);
    var llmFailed = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.LlmFailed);
    var adoCreating = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.AdoCreating);
    var adoCreated = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.AdoCreated);
    var adoFailed = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.AdoFailed);
    var mailFailed = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.MailSendingFailed);
    var pendingValidation = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.PendingClientValidation);
    var clientAccepted = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.ClientAcceptedResolution);
    var clientRejected = allTickets.Count(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.ClientRejectedResolution);

    // ── Derived Metrics ────────────────────────────────
    var processed = allTickets.Count(t => t.AdoWorkItemId != null);
    var totalFailed = llmFailed + adoFailed + mailFailed;
    var aiAutoResolved = pendingValidation + clientAccepted;
    var successRate = total > 0 ? Math.Round((double)processed / total * 100, 1) : 0.0;
    var failRate = total > 0 ? Math.Round((double)totalFailed / total * 100, 1) : 0.0;
    var aiResolvedPct = total > 0 ? Math.Round((double)aiAutoResolved / total * 100, 1) : 0.0;

    // ── In-Queue (tickets not yet fully processed) ─────
    var inQueue = allTickets.Count(t =>
        t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.EmailReceived ||
        t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.LlmProcessing ||
        t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.LlmSuccess ||
        t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.AdoCreating);

    // ── Avg Processing Time (from ReceivedAt to LastUpdatedAt for completed tickets) ──
    var completedTickets = allTickets.Where(t =>
        t.AdoWorkItemId != null &&
        t.CurrentPipelineStatus != MailListenerWorker.Models.Enums.PipelineStatus.EmailReceived &&
        t.CurrentPipelineStatus != MailListenerWorker.Models.Enums.PipelineStatus.LlmProcessing).ToList();

    // Use TicketStateLogs to compute actual pipeline execution time:
    // From the FIRST log entry to the FIRST terminal state (AdoCreated/PendingClientValidation)
    var completedTicketIds = completedTickets.Select(t => t.TicketId).ToList();
    var relevantLogs = await db.TicketStateLogs
        .Where(l => completedTicketIds.Contains(l.TicketId))
        .ToListAsync();
    var logsByTicket = relevantLogs.GroupBy(l => l.TicketId);

    var processingDurations = new List<double>();
    foreach (var group in logsByTicket)
    {
        var logs = group.OrderBy(l => l.CreatedAt).ToList();
        var firstLog = logs.FirstOrDefault();
        var terminalLog = logs.FirstOrDefault(l =>
            l.PipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.AdoCreated ||
            l.PipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.PendingClientValidation ||
            l.PipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.AdoFailed);
        if (firstLog != null && terminalLog != null && terminalLog.CreatedAt > firstLog.CreatedAt)
        {
            processingDurations.Add((terminalLog.CreatedAt - firstLog.CreatedAt).TotalSeconds);
        }
    }
    var avgProcessingSeconds = processingDurations.Count > 0
        ? Math.Round(processingDurations.Average(), 1)
        : 0.0;

    // ── Avg Client Rating ──────────────────────────────
    var ratedTickets = allTickets.Where(t => t.ClientRating.HasValue && t.ClientRating > 0).ToList();
    var avgRating = ratedTickets.Count > 0 ? Math.Round(ratedTickets.Average(t => t.ClientRating!.Value), 1) : 0.0;

    // ── Hourly Processing Trend (24h) ──────────────────
    var hourlyCounts = new int[24];
    foreach (var t in allTickets)
    {
        var localHour = t.ReceivedAt.ToLocalTime().Hour;
        hourlyCounts[localHour]++;
    }

    // ── Department Distribution ────────────────────────
    var departments = allTickets
        .GroupBy(t => t.ExtractedDepartment ?? "Unclassified")
        .Select(g => new { Department = g.Key, Count = g.Count() })
        .OrderByDescending(g => g.Count)
        .Take(10)
        .ToList();

    return new
    {
        // Core totals
        Total = total,
        Processed = processed,
        Failed = totalFailed,
        InQueue = inQueue,
        SuccessRate = successRate,
        FailRate = failRate,

        // Pipeline stages
        Pipeline = new
        {
            Inbox = inbox,
            LlmProcessing = llmProcessing,
            LlmSuccess = llmSuccess,
            LlmFailed = llmFailed,
            AdoCreating = adoCreating,
            AdoCreated = adoCreated,
            AdoFailed = adoFailed,
            MailFailed = mailFailed,
            PendingValidation = pendingValidation,
            ClientAccepted = clientAccepted,
            ClientRejected = clientRejected
        },

        // AI metrics
        AiAutoResolved = aiAutoResolved,
        AiResolvedPct = aiResolvedPct,

        // Performance
        AvgProcessingSeconds = avgProcessingSeconds,
        AvgClientRating = avgRating,
        RatedTicketsCount = ratedTickets.Count,

        // Error breakdown
        Errors = new
        {
            LlmFailed = llmFailed,
            AdoFailed = adoFailed,
            MailFailed = mailFailed,
            LlmFailPct = total > 0 ? Math.Round((double)llmFailed / total * 100, 1) : 0.0,
            AdoFailPct = total > 0 ? Math.Round((double)adoFailed / total * 100, 1) : 0.0,
            MailFailPct = total > 0 ? Math.Round((double)mailFailed / total * 100, 1) : 0.0
        },

        // Charts data
        HourlyCounts = hourlyCounts,
        Departments = departments
    };
});

// ── Minimal API: Validation Endpoint ─────────────────
app.MapGet("/api/ticket/{id:int}/validate", async (int id, bool accepted, AppDbContext db, AzureDevOpsService adoService, ILogger<Program> logger) =>
{
    var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.AdoWorkItemId == id);
    if (ticket == null)
    {
        return Results.Content("<html><body style='font-family:sans-serif;text-align:center;padding:50px;'><h2>Ticket Not Found</h2><p>The ticket ID provided is invalid or does not exist.</p></body></html>", "text/html");
    }

    if (ticket.AdoWorkItemId == null)
    {
         return Results.Content("<html><body style='font-family:sans-serif;text-align:center;padding:50px;'><h2>Not Ready</h2><p>This ticket hasn't been synced to Azure DevOps yet.</p></body></html>", "text/html");
    }

    // Protection to avoid validating multiple times if already processed
    if (ticket.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.ClientAcceptedResolution || 
        ticket.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.ClientRejectedResolution)
    {
        return Results.Content("<html><body style='font-family:sans-serif;text-align:center;padding:50px;'><h2>Already Processed</h2><p>You have already validated this ticket.</p></body></html>", "text/html");
    }

    try 
    {
        if (accepted)
        {
            await adoService.UpdateWorkItemStateAsync(ticket.AdoWorkItemId.Value, "Done");
            ticket.AdoItemState = "Done";
            ticket.CurrentPipelineStatus = MailListenerWorker.Models.Enums.PipelineStatus.ClientAcceptedResolution;
            
            ticket.StateLog.Add(new MailListenerWorker.Models.TicketStateLog
            {
                TicketId = ticket.TicketId,
                PipelineStatus = ticket.CurrentPipelineStatus,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return Results.Content("<html><head><meta charset='UTF-8'></head><body style='font-family:sans-serif;text-align:center;padding:50px;background:#f0fdf4;'><h2 style='color:#166534;'>✅ Validation Successful</h2><p>Thank you! The ticket has been closed.</p></body></html>", "text/html; charset=utf-8");
        }
        else 
        {
            // Even if it was already "To Do", this explicitly keeps it there and updates our DB tracking.
            await adoService.UpdateWorkItemStateAsync(ticket.AdoWorkItemId.Value, "To Do");
            ticket.AdoItemState = "To Do";
            ticket.CurrentPipelineStatus = MailListenerWorker.Models.Enums.PipelineStatus.ClientRejectedResolution;

            ticket.StateLog.Add(new MailListenerWorker.Models.TicketStateLog
            {
                TicketId = ticket.TicketId,
                PipelineStatus = ticket.CurrentPipelineStatus,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return Results.Content("<html><head><meta charset='UTF-8'></head><body style='font-family:sans-serif;text-align:center;padding:50px;background:#fefce8;'><h2 style='color:#854d0e;'>❌ Support Requested</h2><p>We've kept this ticket assigned to our human agents. They will assist you shortly.</p></body></html>", "text/html; charset=utf-8");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to update validation state for ticket {TicketId}", id);
        return Results.Content("<html><head><meta charset='UTF-8'></head><body style='font-family:sans-serif;text-align:center;padding:50px;background:#fef2f2;'><h2 style='color:#991b1b;'>⚠️ Error Processing Validation</h2><p>Please contact IT Support directly.</p></body></html>", "text/html; charset=utf-8");
    }
});
// ── Minimal API: Submit Feedback (Adaptive Card) ─────────
app.MapPost("/api/ticket/{id:int}/feedback", async (int id, [Microsoft.AspNetCore.Mvc.FromBody] MailListenerWorker.Models.FeedbackPayload payload, AppDbContext db, AzureDevOpsService adoService, ILogger<Program> logger) =>
{
    try
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.AdoWorkItemId == id);
        if (ticket == null) return Results.NotFound();

        // Parse rating
        int.TryParse(payload.rating, out int ratingValue);
        
        ticket.ClientRating = ratingValue;
        ticket.ClientFeedback = payload.comment;
        ticket.LastUpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Add beautiful HTML comment to Azure DevOps
        var safeComment = System.Web.HttpUtility.HtmlEncode(payload.comment ?? "").Replace("\n", "<br>");
        var comment = $@"
        <div style='border-left: 4px solid #22c55e; padding: 12px; margin: 10px 0; background-color: #f0fdf4; border-radius: 4px; font-family: ""Segoe UI"", Tahoma, Geneva, Verdana, sans-serif;'>
            <h3 style='margin-top: 0; margin-bottom: 12px; color: #166534; font-size: 16px;'>📊 Client Feedback Received</h3>
            <div style='margin-bottom: 10px;'>
                <strong style='color: #166534;'>Rating:</strong> <span style='font-size: 16px; color: #d97706;'>⭐ {payload.rating} / 5</span>
            </div>
            <div>
                <strong style='color: #166534;'>Comment:</strong>
                <div style='margin-top: 6px; padding: 10px; background-color: #ffffff; border: 1px solid #bbf7d0; border-radius: 4px; color: #374151; font-style: italic;'>
                    {(string.IsNullOrWhiteSpace(safeComment) ? "<span style='color: #9ca3af;'>No additional comment provided.</span>" : safeComment)}
                </div>
            </div>
        </div>";
        await adoService.AddWorkItemCommentAsync(id, comment);

        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process feedback for ticket {TicketId}", id);
        return Results.StatusCode(500);
    }
});

// ── Minimal API: SSE Real-Time Pipeline Events ───────────
app.MapGet("/api/events", async (PipelineEventService eventService, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Connection", "keep-alive");
    ctx.Response.Headers.Append("X-Accel-Buffering", "no");

    // Send recent events as initial payload
    foreach (var evt in eventService.GetRecentEvents())
    {
        var json = System.Text.Json.JsonSerializer.Serialize(evt);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
    }
    await ctx.Response.Body.FlushAsync(ct);

    // Subscribe to live events
    var channel = eventService.Subscribe();
    try
    {
        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(evt);
            await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        eventService.Unsubscribe(channel);
    }
});

app.MapFallbackToFile("index.html");

app.Run();