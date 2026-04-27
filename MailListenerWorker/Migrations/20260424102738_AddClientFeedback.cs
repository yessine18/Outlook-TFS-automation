using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailListenerWorker.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientFeedback",
                table: "Tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientRating",
                table: "Tickets",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientFeedback",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ClientRating",
                table: "Tickets");
        }
    }
}
