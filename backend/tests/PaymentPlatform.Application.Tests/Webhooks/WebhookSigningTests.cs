using System.Security.Cryptography;
using System.Text;
using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.Application.Tests.Webhooks;

public sealed class WebhookSigningTests
{
    [Fact]
    public void The_signature_matches_the_standard_webhooks_test_vector()
    {
        // The example from the Standard Webhooks specification, so any of its libraries verifies what we send.
        var key = WebhookSecrets.KeyOf("whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw")!;

        var signature = WebhookSignature.Sign(key, "msg_p5jXN8AQM9LWM0D4loKWxJek", 1614265330, """{"test": 2432232314}""");

        Assert.Equal("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=", signature);
    }

    [Fact]
    public void The_signature_covers_the_id_the_timestamp_and_every_byte_of_the_body()
    {
        var key = WebhookSecrets.KeyOf(WebhookSecrets.New())!;
        var signature = WebhookSignature.Sign(key, "evt_1", 1793260000, """{"amount_minor":100}""");

        Assert.NotEqual(signature, WebhookSignature.Sign(key, "evt_2", 1793260000, """{"amount_minor":100}"""));
        Assert.NotEqual(signature, WebhookSignature.Sign(key, "evt_1", 1793260001, """{"amount_minor":100}"""));
        Assert.NotEqual(signature, WebhookSignature.Sign(key, "evt_1", 1793260000, """{"amount_minor":101}"""));
        Assert.NotEqual(signature, WebhookSignature.Sign(key, "evt_1", 1793260000, """{"amount_minor": 100}"""));
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("""evt_1.1793260000.{"amount_minor":100}"""));
        Assert.Equal($"v1,{Convert.ToBase64String(expected)}", signature);
    }

    [Fact]
    public void A_new_secret_is_32_random_bytes_in_standard_base64()
    {
        var first = WebhookSecrets.New();
        var second = WebhookSecrets.New();

        Assert.Matches("^whsec_[A-Za-z0-9+/]{43}=$", first);
        Assert.NotEqual(first, second);
        Assert.Equal(32, WebhookSecrets.KeyOf(first)!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw")]
    [InlineData("whsec_")]
    [InlineData("whsec_not base64!")]
    [InlineData("whsec_c2hvcnQ=")]
    public void Text_that_is_not_a_secret_in_the_whsec_form_has_no_key(string secret)
    {
        Assert.Null(WebhookSecrets.KeyOf(secret));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(9, 2560)]
    [InlineData(10, 3600)]
    [InlineData(11, 3600)]
    [InlineData(100, 3600)]
    public void The_backoff_doubles_from_10_seconds_to_a_one_hour_cap(int failedAttempts, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), WebhookRetrySchedule.Backoff(failedAttempts));
    }

    [Fact]
    public void The_jitter_spreads_the_wait_between_zero_and_the_backoff()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now, WebhookRetrySchedule.NextAttemptAt(3, now, 0));
        Assert.Equal(now.AddSeconds(20), WebhookRetrySchedule.NextAttemptAt(3, now, 0.5));
        Assert.True(WebhookRetrySchedule.NextAttemptAt(3, now, 0.999999) < now.AddSeconds(40));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebhookRetrySchedule.NextAttemptAt(3, now, 1));
    }

    [Fact]
    public void The_last_attempt_dead_letters_and_the_waits_before_it_stay_under_four_hours()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var longest = Enumerable.Range(1, WebhookRetrySchedule.MaxAttempts - 1)
            .Aggregate(TimeSpan.Zero, (total, failed) => total + WebhookRetrySchedule.Backoff(failed));

        Assert.Null(WebhookRetrySchedule.NextAttemptAt(WebhookRetrySchedule.MaxAttempts, now, 0.5));
        Assert.NotNull(WebhookRetrySchedule.NextAttemptAt(WebhookRetrySchedule.MaxAttempts - 1, now, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(12_310), longest);
    }
}
