using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentPlatform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MerchantReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AuthorizationReference = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    CardBrand = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CardExpiryMonth = table.Column<byte>(type: "tinyint", nullable: false),
                    CardExpiryYear = table.Column<short>(type: "smallint", nullable: false),
                    CardLast4 = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: false),
                    CardToken = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("CK_Payments_AmountMinor", "[AmountMinor] > 0 AND [AmountMinor] <= 99999999");
                    table.CheckConstraint("CK_Payments_CardBrand", "[CardBrand] IN ('Visa', 'Mastercard', 'Amex', 'Discover')");
                    table.CheckConstraint("CK_Payments_Status", "[Status] IN ('Pending', 'Authorized', 'Captured', 'Settled', 'Failed', 'Refunded')");
                    table.ForeignKey(
                        name: "FK_Payments_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalSchema: "payments",
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ActorKind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.CheckConstraint("CK_PaymentEvents_ActorKind", "[ActorKind] IN ('MerchantUser', 'System')");
                    table.CheckConstraint("CK_PaymentEvents_FromStatus", "[FromStatus] IS NULL OR [FromStatus] IN ('Pending', 'Authorized', 'Captured', 'Settled', 'Failed', 'Refunded')");
                    table.CheckConstraint("CK_PaymentEvents_ToStatus", "[ToStatus] IN ('Pending', 'Authorized', 'Captured', 'Settled', 'Failed', 'Refunded')");
                    table.ForeignKey(
                        name: "FK_PaymentEvents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "payments",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentId_Id",
                schema: "payments",
                table: "PaymentEvents",
                columns: new[] { "PaymentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantId_CreatedAt_Id",
                schema: "payments",
                table: "Payments",
                columns: new[] { "MerchantId", "CreatedAt", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentEvents",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "payments");
        }
    }
}
