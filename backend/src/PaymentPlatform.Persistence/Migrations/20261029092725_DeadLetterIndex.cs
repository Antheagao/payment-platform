using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentPlatform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeadLetterIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_DeadLettered",
                schema: "payments",
                table: "WebhookDeliveries",
                column: "Id",
                filter: "[Status] = 'DeadLettered'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_DeadLettered",
                schema: "payments",
                table: "WebhookDeliveries");
        }
    }
}
