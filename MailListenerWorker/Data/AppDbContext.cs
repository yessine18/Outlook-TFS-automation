using MailListenerWorker.Models;
using MailListenerWorker.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MailListenerWorker.Data;

/// <summary>
/// Entity Framework Core database context for the helpdesk ticket pipeline.
/// Targets PostgreSQL via Npgsql and uses Fluent API configuration exclusively.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>The set of helpdesk tickets tracked by the pipeline.</summary>
    public DbSet<Ticket> Tickets => Set<Ticket>();

    /// <summary>Append-only audit log of every pipeline state transition.</summary>
    public DbSet<TicketStateLog> TicketStateLogs => Set<TicketStateLog>();

    /// <inheritdoc />
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureTicket(modelBuilder);
        ConfigureTicketStateLog(modelBuilder);
    }

    // ────────────────────────────────────────────────────────────
    //  Ticket configuration
    // ────────────────────────────────────────────────────────────

    /// <summary>Fluent API configuration for the <see cref="Ticket"/> entity.</summary>
    private static void ConfigureTicket(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(entity =>
        {
            // ── Table & Key ──────────────────────────────────────
            entity.ToTable("Tickets");
            entity.HasKey(t => t.TicketId);

            // ── MessageId – unique, indexed ──────────────────────
            entity.Property(t => t.MessageId)
                  .IsRequired()
                  .HasMaxLength(512);

            entity.HasIndex(t => t.MessageId)
                  .IsUnique()
                  .HasDatabaseName("IX_Tickets_MessageId");

            // ── Email metadata ───────────────────────────────────
            entity.Property(t => t.SenderEmail)
                  .IsRequired()
                  .HasMaxLength(320);           // RFC 5321 max email length

            entity.Property(t => t.Subject)
                  .IsRequired()
                  .HasMaxLength(1000);

            entity.Property(t => t.BodyExcerpt)
                  .IsRequired()
                  .HasMaxLength(4000);

            entity.Property(t => t.ReceivedAt)
                  .IsRequired();

            // ── LLM extraction (all nullable) ────────────────────
            entity.Property(t => t.ExtractedDepartment)
                  .HasMaxLength(200);

            entity.Property(t => t.ExtractedIntent)
                  .HasMaxLength(500);

            // ── ADO mapping (all nullable) ───────────────────────
            entity.Property(t => t.AdoAssignee)
                  .HasMaxLength(320);

            entity.Property(t => t.AdoUrl)
                  .HasMaxLength(2048);

            entity.Property(t => t.AdoItemState)
                  .HasMaxLength(100);

            // ── Pipeline status – stored as string ───────────────
            entity.Property(t => t.CurrentPipelineStatus)
                  .IsRequired()
                  .HasMaxLength(50)
                  .HasConversion<string>();

            // ── Timestamps ───────────────────────────────────────
            entity.Property(t => t.LastUpdatedAt)
                  .IsRequired();

            // ── Indexes for fast querying ────────────────────────
            entity.HasIndex(t => t.CurrentPipelineStatus)
                  .HasDatabaseName("IX_Tickets_CurrentPipelineStatus");

            entity.HasIndex(t => t.AdoItemState)
                  .HasDatabaseName("IX_Tickets_AdoItemState");

            // ── One-to-Many: Ticket → TicketStateLogs ────────────
            entity.HasMany(t => t.StateLog)
                  .WithOne(l => l.Ticket)
                  .HasForeignKey(l => l.TicketId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // ────────────────────────────────────────────────────────────
    //  TicketStateLog configuration
    // ────────────────────────────────────────────────────────────

    /// <summary>Fluent API configuration for the <see cref="TicketStateLog"/> entity.</summary>
    private static void ConfigureTicketStateLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketStateLog>(entity =>
        {
            // ── Table & Key ──────────────────────────────────────
            entity.ToTable("TicketStateLogs");
            entity.HasKey(l => l.LogId);

            // ── Pipeline status – stored as string ───────────────
            entity.Property(l => l.PipelineStatus)
                  .IsRequired()
                  .HasMaxLength(50)
                  .HasConversion<string>();

            // ── Error message (nullable, for failure states) ─────
            entity.Property(l => l.ErrorMessage)
                  .HasMaxLength(4000);

            // ── Timestamp ────────────────────────────────────────
            entity.Property(l => l.CreatedAt)
                  .IsRequired();

            // ── Index on FK for join performance ─────────────────
            entity.HasIndex(l => l.TicketId)
                  .HasDatabaseName("IX_TicketStateLogs_TicketId");
        });
    }
}
