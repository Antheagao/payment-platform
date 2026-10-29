using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentPlatform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Settlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Payments_Captured",
                schema: "payments",
                table: "Payments",
                column: "UpdatedAt",
                filter: "[Status] = 'Captured'");

            migrationBuilder.Sql(SettlementProcedure.AsExec(SettlementProcedure.Version1));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SettlementProcedure.DropSql);

            migrationBuilder.DropIndex(
                name: "IX_Payments_Captured",
                schema: "payments",
                table: "Payments");
        }
    }
}
