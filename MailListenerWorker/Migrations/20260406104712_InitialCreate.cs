using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailListenerWorker.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SenderEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BodyExcerpt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExtractedDepartment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExtractedIntent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LlmConfidenceScore = table.Column<double>(type: "double precision", nullable: true),
                    AdoWorkItemId = table.Column<int>(type: "integer", nullable: true),
                    AdoAssignee = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    AdoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AdoItemState = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CurrentPipelineStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.TicketId);
                });

            migrationBuilder.CreateTable(
                name: "TicketStateLogs",
                columns: table => new
                {
                    LogId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketStateLogs", x => x.LogId);
                    table.ForeignKey(
                        name: "FK_TicketStateLogs_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "TicketId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AdoItemState",
                table: "Tickets",
                column: "AdoItemState");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CurrentPipelineStatus",
                table: "Tickets",
                column: "CurrentPipelineStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_MessageId",
                table: "Tickets",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketStateLogs_TicketId",
                table: "TicketStateLogs",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketStateLogs");

            migrationBuilder.DropTable(
                name: "Tickets");
        }
    }
}
