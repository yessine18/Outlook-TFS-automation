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

// ── Minimal API: Stats ───────────────────────────────
app.MapGet("/api/stats", async (AppDbContext db) =>
{
    var total = await db.Tickets.CountAsync();
    var processed = await db.Tickets.CountAsync(t => t.AdoWorkItemId != null);
    var failed = await db.Tickets.CountAsync(t => t.CurrentPipelineStatus == MailListenerWorker.Models.Enums.PipelineStatus.AdoFailed);
    
    return new { Total = total, Processed = processed, Failed = failed };
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

app.MapFallbackToFile("index.html");

app.Run();