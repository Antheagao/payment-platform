namespace PaymentPlatform.ServiceDefaults.Tests;

public sealed class CorrelationIdsTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    [InlineData("checkout-2026-10-28.retry_1:abc")]
    public void Well_formed_caller_ids_are_kept(string candidate)
    {
        Assert.True(CorrelationIds.IsAcceptable(candidate));
        Assert.Equal(candidate, CorrelationIds.AcceptOrCreate(candidate));
    }

    [Fact]
    public void An_id_of_exactly_the_maximum_length_is_kept()
    {
        Assert.True(CorrelationIds.IsAcceptable(new string('a', CorrelationIds.MaxLength)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("quote\"inside")]
    [InlineData("a,b")]
    [InlineData("ünïcode")]
    [InlineData("<script>")]
    public void Unsafe_or_empty_caller_ids_are_replaced(string? candidate)
    {
        Assert.False(CorrelationIds.IsAcceptable(candidate));

        var replacement = CorrelationIds.AcceptOrCreate(candidate);

        Assert.NotEqual(candidate, replacement);
        Assert.Matches("^[0-9a-f]{32}$", replacement);
    }

    [Fact]
    public void An_id_one_over_the_maximum_length_is_replaced()
    {
        Assert.False(CorrelationIds.IsAcceptable(new string('a', CorrelationIds.MaxLength + 1)));
    }

    [Fact]
    public void Generated_ids_are_unique()
    {
        var ids = Enumerable.Range(0, 1_000).Select(_ => CorrelationIds.New()).ToHashSet();

        Assert.Equal(1_000, ids.Count);
    }
}
