using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Tests.Acquiring;

public sealed class SimulatedAcquirerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Money Amount = Money.Create(4_200, "USD").Value;

    private readonly CancellationToken _cancellation = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_ordinary_token_is_approved_with_a_reference_derived_from_the_payment()
    {
        var paymentId = PaymentId.New(Now);

        var response = await Instant().AuthorizeAsync(new AuthorizationRequest(paymentId, Amount, "tok_9f2c1a"), _cancellation);

        Assert.Equal(AcquirerDecision.Approved, response.Decision);
        Assert.Equal($"sim_auth_{paymentId.Value:N}", response.Reference);
        Assert.Null(response.DeclineCode);
    }

    [Fact]
    public async Task Authorizing_the_same_payment_twice_returns_the_same_answer()
    {
        var request = new AuthorizationRequest(PaymentId.New(Now), Amount, "tok_9f2c1a");
        var acquirer = Instant();

        var first = await acquirer.AuthorizeAsync(request, _cancellation);
        var second = await acquirer.AuthorizeAsync(request, _cancellation);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Two_payments_get_different_references()
    {
        var acquirer = Instant();

        var first = await acquirer.AuthorizeAsync(new AuthorizationRequest(PaymentId.New(Now), Amount, "tok_a"), _cancellation);
        var second = await acquirer.AuthorizeAsync(new AuthorizationRequest(PaymentId.New(Now), Amount, "tok_a"), _cancellation);

        Assert.NotEqual(first.Reference, second.Reference);
    }

    [Theory]
    [InlineData(SimulatedCardTokens.CardDeclined, "card_declined")]
    [InlineData(SimulatedCardTokens.InsufficientFunds, "insufficient_funds")]
    public async Task Scenario_tokens_decline_with_their_code(string token, string declineCode)
    {
        var response = await Instant().AuthorizeAsync(new AuthorizationRequest(PaymentId.New(Now), Amount, token), _cancellation);

        Assert.Equal(AcquirerDecision.Declined, response.Decision);
        Assert.Equal(declineCode, response.DeclineCode);
        Assert.Null(response.Reference);
    }

    [Fact]
    public async Task The_unavailable_token_gets_no_answer()
    {
        var response = await Instant().AuthorizeAsync(
            new AuthorizationRequest(PaymentId.New(Now), Amount, SimulatedCardTokens.AcquirerUnavailable), _cancellation);

        Assert.Equal(AcquirerDecision.Unavailable, response.Decision);
    }

    [Fact]
    public async Task Capture_and_refund_need_the_payments_own_authorization()
    {
        var paymentId = PaymentId.New(Now);
        var acquirer = Instant();
        var authorization = await acquirer.AuthorizeAsync(new AuthorizationRequest(paymentId, Amount, "tok_a"), _cancellation);
        var followUp = new FollowUpRequest(paymentId, Amount, authorization.Reference!);

        var capture = await acquirer.CaptureAsync(followUp, _cancellation);
        var refund = await acquirer.RefundAsync(followUp, _cancellation);

        Assert.Equal($"sim_cap_{paymentId.Value:N}", capture.Reference);
        Assert.Equal($"sim_ref_{paymentId.Value:N}", refund.Reference);
    }

    [Fact]
    public async Task Capturing_with_another_payments_authorization_is_declined()
    {
        var acquirer = Instant();
        var other = await acquirer.AuthorizeAsync(new AuthorizationRequest(PaymentId.New(Now), Amount, "tok_a"), _cancellation);

        var capture = await acquirer.CaptureAsync(new FollowUpRequest(PaymentId.New(Now), Amount, other.Reference!), _cancellation);

        Assert.Equal("authorization_not_found", capture.DeclineCode);
    }

    [Fact]
    public async Task The_simulated_latency_runs_on_the_injected_clock()
    {
        var clock = new FakeTimeProvider(Now);
        var acquirer = new SimulatedAcquirer(Options.Create(new SimulatedAcquirerOptions { LatencyMilliseconds = 80 }), clock);

        var pending = acquirer.AuthorizeAsync(new AuthorizationRequest(PaymentId.New(Now), Amount, "tok_a"), _cancellation);
        clock.Advance(TimeSpan.FromMilliseconds(79));
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(AcquirerDecision.Approved, (await pending).Decision);
    }

    [Fact]
    public async Task A_cancelled_call_stops_waiting()
    {
        var clock = new FakeTimeProvider(Now);
        var acquirer = new SimulatedAcquirer(Options.Create(new SimulatedAcquirerOptions { LatencyMilliseconds = 80 }), clock);
        using var cancellation = new CancellationTokenSource();

        var pending = acquirer.AuthorizeAsync(new AuthorizationRequest(PaymentId.New(Now), Amount, "tok_a"), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public void An_out_of_range_latency_fails_options_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Acquirer:Simulated:LatencyMilliseconds"] = "60000" })
            .Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddApplication()
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SimulatedAcquirerOptions>>().Value);
    }

    private static SimulatedAcquirer Instant() =>
        new(Options.Create(new SimulatedAcquirerOptions { LatencyMilliseconds = 0 }), TimeProvider.System);
}
