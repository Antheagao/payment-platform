using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentPlatform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Azure SQL runs read committed snapshot isolation by default and a SQL Server container does not;
            // turning it on keeps reads from blocking on writers the same way in both (database overlay).
            // ALTER DATABASE cannot run inside a transaction, so it is this migration's only operation; ROLLBACK
            // IMMEDIATE ends the other sessions it would wait for, and the check makes it a no-op where it is
            // already on, as in Azure SQL.
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_read_committed_snapshot_on = 1)
                    ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Left on: turning it off would make a local database behave unlike Azure SQL again, and nothing
            // later depends on it being off.
        }
    }
}
