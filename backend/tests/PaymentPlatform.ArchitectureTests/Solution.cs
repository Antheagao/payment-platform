using System.Reflection;
using System.Xml.Linq;

namespace PaymentPlatform.ArchitectureTests;

/// <summary>The source projects, located both as loaded assemblies and as project files on disk.</summary>
internal static class Solution
{
    public static readonly Assembly Domain = typeof(Domain.Payments.Payment).Assembly;
    public static readonly Assembly Persistence = typeof(Persistence.PaymentsDbContext).Assembly;
    public static readonly Assembly Application = typeof(Application.Acquiring.IAcquirer).Assembly;
    public static readonly Assembly ServiceDefaults = typeof(ServiceDefaults.CorrelationIds).Assembly;
    public static readonly Assembly Api = typeof(Api.Endpoints.ApiRoutes).Assembly;
    public static readonly Assembly Worker = typeof(Worker.Jobs.PeriodicJob).Assembly;

    public static IEnumerable<Assembly> All => [Domain, Persistence, Application, ServiceDefaults, Api, Worker];

    public static string BackendDirectory { get; } = FindBackendDirectory();

    /// <summary>The assemblies an assembly actually uses, by simple name.</summary>
    public static IReadOnlySet<string> ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToHashSet(StringComparer.Ordinal);

    /// <summary>The <c>ProjectReference</c> names declared in a source project's file.</summary>
    public static IReadOnlySet<string> ProjectReferencesOf(string projectName)
    {
        var path = Path.Combine(BackendDirectory, "src", projectName, $"{projectName}.csproj");
        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToHashSet(StringComparer.Ordinal);
    }

    public static IEnumerable<string> SourceProjectNames() =>
        Directory.GetDirectories(Path.Combine(BackendDirectory, "src")).Select(Path.GetFileName).OfType<string>();

    private static string FindBackendDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PaymentPlatform.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("PaymentPlatform.slnx not found above the test output directory");
    }
}
