using System.Reflection;
using System.Runtime.CompilerServices;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.ArchitectureTests;

/// <summary>Rules from the clean-code and design overlays that a reviewer would otherwise have to remember.</summary>
public sealed class TypeRuleTests
{
    [Fact]
    public void Concrete_classes_are_sealed_unless_they_are_designed_for_inheritance()
    {
        var unsealed = Solution.All
            .SelectMany(WrittenTypes)
            .Where(type => type is { IsClass: true, IsAbstract: false, IsSealed: false })
            .Where(type => type.Name != "Program")
            .Select(type => type.FullName);

        Assert.Empty(unsealed);
    }

    [Fact]
    public void Only_the_payment_itself_can_change_its_status()
    {
        var status = typeof(Payment).GetProperty(nameof(Payment.Status))!;

        Assert.False(status.SetMethod?.IsPublic ?? false, "Payment.Status must not have a public setter");
        Assert.False(status.SetMethod?.IsAssembly ?? false, "Payment.Status must not have an internal setter");
    }

    [Fact]
    public void Domain_types_expose_no_public_mutable_fields()
    {
        var fields = WrittenTypes(Solution.Domain)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(field => !field.IsInitOnly && !field.IsLiteral && !field.DeclaringType!.IsEnum)
            .Select(field => $"{field.DeclaringType!.Name}.{field.Name}");

        Assert.Empty(fields);
    }

    // Types a person wrote: closures, iterators, and other compiler output follow their own rules.
    private static IEnumerable<Type> WrittenTypes(Assembly assembly) =>
        assembly.GetTypes().Where(type =>
            !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            && !type.Name.Contains('<', StringComparison.Ordinal)
            && (type.DeclaringType is null || !type.DeclaringType.Name.Contains('<', StringComparison.Ordinal)));
}
