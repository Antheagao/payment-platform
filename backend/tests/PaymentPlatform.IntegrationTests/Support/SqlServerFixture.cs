using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Persistence;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(PaymentPlatform.IntegrationTests.Support.SqlServerFixture))]

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>
/// One SQL Server container for the whole test run; each test class gets its own freshly migrated database
/// from <see cref="CreateDatabaseAsync"/>, so tests never share rows and never depend on cleanup.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>The same image as <c>docker-compose.yml</c>, so tests run on the engine the stack runs on.</summary>
    public const string Image = "mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database with every migration applied and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"t_{Guid.NewGuid():N}",
        }.ConnectionString;

        // The API's own provider settings, so the migration history lands where the API looks for it
        // (payments.__EFMigrationsHistory) and readiness sees no pending migration.
        var options = new DbContextOptionsBuilder<PaymentsDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, connectionString);
        await using var db = new PaymentsDbContext(options.Options);
        await db.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }
}
