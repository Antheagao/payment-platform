namespace PaymentPlatform.ArchitectureTests;

/// <summary>ADR 002's table, as tests: what each project may reference, declared and used.</summary>
public sealed class ProjectBoundaryTests
{
    // Declared project references per source project; ADR 002 is the source of truth for this table.
    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "PaymentPlatform.Domain", [] },
        { "PaymentPlatform.Persistence", ["PaymentPlatform.Domain"] },
        { "PaymentPlatform.Application", ["PaymentPlatform.Domain", "PaymentPlatform.Persistence"] },
        { "PaymentPlatform.ServiceDefaults", [] },
        { "PaymentPlatform.Api", ["PaymentPlatform.Application", "PaymentPlatform.ServiceDefaults"] },
        { "PaymentPlatform.Worker", ["PaymentPlatform.Application", "PaymentPlatform.ServiceDefaults"] },
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void Each_project_declares_exactly_the_references_adr_002_allows(string project, string[] allowed)
    {
        Assert.Equal(allowed.Order(StringComparer.Ordinal), Solution.ProjectReferencesOf(project).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_source_project_is_covered_by_the_table()
    {
        var covered = AllowedProjectReferences.Select(row => (string)row.Data.Item1).ToHashSet();

        Assert.Equal(covered.Order(StringComparer.Ordinal), Solution.SourceProjectNames().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_domain_uses_nothing_but_the_base_class_library()
    {
        var foreign = Solution.ReferencesOf(Solution.Domain)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name is not "netstandard" and not "mscorlib");

        Assert.Empty(foreign);
    }

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void The_core_never_uses_web_types(string assemblyName)
    {
        var assembly = Solution.All.Single(candidate => candidate.GetName().Name == assemblyName);

        Assert.DoesNotContain(Solution.ReferencesOf(assembly), name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void The_hosts_never_use_each_other()
    {
        Assert.DoesNotContain("PaymentPlatform.Worker", Solution.ReferencesOf(Solution.Api));
        Assert.DoesNotContain("PaymentPlatform.Api", Solution.ReferencesOf(Solution.Worker));
    }

    [Fact]
    public void Service_defaults_know_nothing_about_payments()
    {
        Assert.DoesNotContain(Solution.ReferencesOf(Solution.ServiceDefaults), name => name.StartsWith("PaymentPlatform.", StringComparison.Ordinal));
    }

    public static TheoryData<string> CoreAssemblies =>
        new(new[] { Solution.Domain, Solution.Persistence, Solution.Application }.Select(assembly => assembly.GetName().Name!));
}
