using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailListenerWorker.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConversationId",
                table: "Tickets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ConversationId",
                table: "Tickets",
                column: "ConversationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_ConversationId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "Tickets");
        }
    }
}
