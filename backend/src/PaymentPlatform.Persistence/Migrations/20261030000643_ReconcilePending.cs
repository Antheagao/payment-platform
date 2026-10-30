using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentPlatform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcilePending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconcileAfter",
                schema: "payments",
                table: "Payments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Pending",
                schema: "payments",
                table: "Payments",
                column: "CreatedAt",
                filter: "[Status] = 'Pending'")
                .Annotation("SqlServer:Include", new[] { "ReconcileAfter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_Pending",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReconcileAfter",
                schema: "payments",
                table: "Payments");
        }
    }
}
