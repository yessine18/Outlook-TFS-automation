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

app.MapFallbackToFile("index.html");

app.Run();