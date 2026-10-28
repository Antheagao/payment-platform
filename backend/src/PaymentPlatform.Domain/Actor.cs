namespace PaymentPlatform.Domain;

/// <summary>Who or what caused a change; written on every audit row.</summary>
public sealed record Actor
{
    private Actor(ActorKind kind, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Kind = kind;
        Id = id;
    }

    public ActorKind Kind { get; }

    /// <summary>The user id for a merchant user, the job name for the system.</summary>
    public string Id { get; }

    public static Actor MerchantUser(Guid userId) => new(ActorKind.MerchantUser, userId.ToString());

    /// <summary>A background job, named for what it does: <c>settlement</c>, <c>reconciliation</c>.</summary>
    public static Actor System(string jobName) => new(ActorKind.System, jobName);

    public override string ToString() => $"{Kind}:{Id}";
}
