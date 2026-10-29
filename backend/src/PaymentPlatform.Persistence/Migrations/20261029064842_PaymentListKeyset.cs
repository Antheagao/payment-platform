using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentPlatform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentListKeyset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_MerchantId_CreatedAt_Id",
                schema: "payments",
                table: "Payments");

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                schema: "payments",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantId_CreatedAt_Sequence",
                schema: "payments",
                table: "Payments",
                columns: new[] { "MerchantId", "CreatedAt", "Sequence" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantId_MerchantReference",
                schema: "payments",
                table: "Payments",
                columns: new[] { "MerchantId", "MerchantReference" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantId_Status_CreatedAt_Sequence",
                schema: "payments",
                table: "Payments",
                columns: new[] { "MerchantId", "Status", "CreatedAt", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_MerchantId_CreatedAt_Sequence",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_MerchantId_MerchantReference",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_MerchantId_Status_CreatedAt_Sequence",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Sequence",
                schema: "payments",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantId_CreatedAt_Id",
                schema: "payments",
                table: "Payments",
                columns: new[] { "MerchantId", "CreatedAt", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }
    }
}
