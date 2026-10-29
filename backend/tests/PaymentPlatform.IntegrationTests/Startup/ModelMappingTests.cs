using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Startup;

/// <summary>
/// EF Core maps a get-only property only when a configuration names it, and leaves any other one out of the table
/// without a word; the value is then silently lost on save. Every public property of a stored type must be a column.
/// </summary>
public sealed class ModelMappingTests
{
    // Kept in memory on purpose: the changes a save turns into history and outbox rows.
    private static readonly HashSet<string> NotStored = ["Payment.PendingEvents"];

    [Fact]
    public void Every_public_property_of_a_stored_type_is_mapped()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, "Server=localhost;Database=model-only");
        using var db = new PaymentsDbContext(options.Options);

        var unmapped = db.Model.GetEntityTypes().SelectMany(Unmapped).Where(name => !NotStored.Contains(name)).ToList();

        Assert.Empty(unmapped);
    }

    private static IEnumerable<string> Unmapped(ITypeBase type)
    {
        var mapped = type.GetMembers().Select(member => member.Name)
            .Concat(type is IEntityType entity ? entity.GetNavigations().Select(navigation => navigation.Name) : [])
            .ToHashSet(StringComparer.Ordinal);
        var unmapped = type.ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null && !mapped.Contains(property.Name))
            .Select(property => $"{type.ClrType.Name}.{property.Name}");
        return unmapped.Concat(type.GetComplexProperties().SelectMany(complex => Unmapped(complex.ComplexType)));
    }
}
