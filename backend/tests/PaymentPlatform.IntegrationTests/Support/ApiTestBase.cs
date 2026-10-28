namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>
/// Gives a test class its own migrated database and an API bound to it, created once per class.
/// </summary>
public abstract class ApiTestBase(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private ApiFactory? _factory;

    protected ApiFactory Factory => _factory ?? throw new InvalidOperationException("the database is not ready yet");

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await sqlServer.CreateDatabaseAsync(Cancellation);
        _factory = new ApiFactory(connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
