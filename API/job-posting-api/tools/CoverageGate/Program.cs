using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

try
{
    var root = Directory.GetCurrentDirectory();
    if (!File.Exists(Path.Combine(root, "JobBoard.slnx")))
        throw new InvalidOperationException("Run the coverage gate from API/job-posting-api.");

    if (args.Length == 3 && args[0] == "--verify")
    {
        Verify(root, args[1], args[2]);
        return 0;
    }
    if (args.Length != 0)
        throw new InvalidOperationException("Usage: dotnet run --project tools/CoverageGate [-- --verify Unit|Host report.json]");

    Run("restore", "JobBoard.slnx", "--locked-mode");
    foreach (var suite in new[] { "Unit", "Host" })
    {
        var directory = Path.Combine(root, "tests", "JobPosting.Api.Tests", "TestResults", suite.ToLowerInvariant());
        Directory.CreateDirectory(directory);
        var report = Path.Combine(directory, "coverage.json");
        // Delete only this known generated report, preventing a previous run from satisfying the gate.
        File.Delete(report);
        Run("test", "tests/JobPosting.Api.Tests/JobPosting.Api.Tests.csproj", "--configuration", "Debug",
            "--no-restore", "--filter", $"Category={suite}", "-p:CollectCoverage=true",
            $"-p:CoverageSuite={suite}", $"-p:CoverletOutput={directory}{Path.DirectorySeparatorChar}");
        Verify(root, suite, report);
        NegativeChecks(root, suite, report);
    }
    Console.WriteLine("PASS: complete authored source set; isolated unit and bootstrap host gates both passed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Coverage gate failed: {exception.Message}");
    return 1;
}

static void Run(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"dotnet {arguments[0]} exited with {process.ExitCode}.");
}

static void Verify(string root, string suite, string report)
{
    if (suite is not ("Unit" or "Host")) throw new InvalidOperationException("Unknown coverage suite.");
    var generated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var exclusions = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "coverage-exclusions.json"))))
    {
        foreach (var item in exclusions.RootElement.EnumerateArray())
        {
            var source = Path.GetFullPath(Path.Combine(root, item.GetProperty("path").GetString()!));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(source).Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
            if (hash != item.GetProperty("sha256").GetString())
                throw new InvalidOperationException($"Generated exclusion changed; inspect before reauthorizing: {source}");
            if (string.IsNullOrWhiteSpace(item.GetProperty("reason").GetString()))
                throw new InvalidOperationException("Generated exclusion requires an exact reason.");
            generated.Add(source);
        }
    }
    var projects = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
        .Where(path => !GeneratedPath(path)).ToArray();
    if (projects.Length == 0) throw new InvalidOperationException("No posting production projects found.");
    var expected = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var modules = new HashSet<string>(StringComparer.Ordinal);
    foreach (var project in projects)
    {
        var assembly = XDocument.Load(project).Descendants("AssemblyName").SingleOrDefault()?.Value
            ?? Path.GetFileNameWithoutExtension(project);
        var projectFiles = Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*.cs", SearchOption.AllDirectories)
            .Where(path => !GeneratedPath(path) && !generated.Contains(Path.GetFullPath(path)));
        foreach (var file in projectFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var syntax = tree.GetRoot();
            if (syntax.DescendantNodes().OfType<AttributeSyntax>().Any(attribute =>
                attribute.Name.ToString().Contains("ExcludeFromCodeCoverage", StringComparison.Ordinal)
                || attribute.Name.ToString().Contains("ExcludeFromCoverage", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Coverage suppression is forbidden in authored source: {file}");
            var bootstrap = string.Equals(Path.GetFullPath(file),
                Path.Combine(root, "src", "JobPosting.Api", "Program.cs"), StringComparison.OrdinalIgnoreCase);
            if (!bootstrap && syntax.DescendantNodes().OfType<GlobalStatementSyntax>().Any())
                throw new InvalidOperationException($"Only the API Program.cs may be assigned to bootstrap host coverage: {file}");
            if ((suite == "Host") != bootstrap) continue;
            var types = syntax.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(type => type is not InterfaceDeclarationSyntax)
                .Select(TypeName).ToHashSet(StringComparer.Ordinal);
            if (bootstrap) types.Add("Program");
            // Interface/enum-only files have no executable sequence points. All concrete types remain required.
            if (types.Count == 0) continue;
            expected.Add(Path.GetFullPath(file), types);
            modules.Add(assembly + ".dll");
        }
    }
    if (expected.Count == 0) throw new InvalidOperationException($"No authored sources found for {suite}.");
    using var json = JsonDocument.Parse(File.ReadAllText(report));
    var found = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var foundModules = new HashSet<string>(StringComparer.Ordinal);
    long lines = 0, branches = 0, methods = 0;
    foreach (var module in json.RootElement.EnumerateObject())
    {
        if (!modules.Contains(module.Name)) throw new InvalidOperationException($"Unexpected module: {module.Name}");
        foundModules.Add(module.Name);
        foreach (var document in module.Value.EnumerateObject())
        {
            var source = Path.GetFullPath(document.Name);
            if (!expected.ContainsKey(source)) throw new InvalidOperationException($"Unexpected source in {suite}: {source}");
            if (!found.TryGetValue(source, out var foundTypes)) found.Add(source, foundTypes = new(StringComparer.Ordinal));
            var fileLines = 0;
            foreach (var type in document.Value.EnumerateObject())
            {
                var typeLines = 0;
                foreach (var method in type.Value.EnumerateObject())
                {
                    var methodLines = method.Value.GetProperty("Lines").EnumerateObject().ToArray();
                    if (methodLines.Length > 0) methods++;
                    foreach (var line in methodLines)
                    {
                        lines++;
                        fileLines++;
                        typeLines++;
                        if (line.Value.GetInt64() <= 0)
                            throw new InvalidOperationException($"Uncovered line {line.Name}: {source}, {type.Name}, {method.Name}");
                    }
                    foreach (var branch in method.Value.GetProperty("Branches").EnumerateArray())
                    {
                        branches++;
                        if (branch.GetProperty("Hits").GetInt64() <= 0)
                            throw new InvalidOperationException($"Uncovered branch: {source}, {type.Name}, {method.Name}");
                    }
                }
                if (typeLines > 0) foundTypes.Add(type.Name);
            }
            if (fileLines == 0) throw new InvalidOperationException($"Empty source coverage: {source}");
        }
    }
    if (!modules.SetEquals(foundModules)) throw new InvalidOperationException("Missing production assembly coverage.");
    foreach (var (source, types) in expected)
    {
        if (!found.TryGetValue(source, out var foundTypes)) throw new InvalidOperationException($"Missing source coverage: {source}");
        foreach (var type in types)
            if (!foundTypes.Any(name => name == type || name.StartsWith(type + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Missing type coverage: {source}, {type}");
    }
    if (lines == 0 || methods == 0 || branches == 0) throw new InvalidOperationException("Empty coverage metrics.");
    Console.WriteLine($"{suite}: lines {lines}/{lines}, branches {branches}/{branches}, methods {methods}/{methods}; " +
        $"{expected.Count} authored files, {expected.Sum(item => item.Value.Count)} declared types. All 100% without rounding.");
}

static string TypeName(TypeDeclarationSyntax type)
{
    var ns = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(node => node.Name.ToString()));
    var names = type.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Append(type)
        .Select(node => node.Identifier.ValueText + (node.TypeParameterList is { Parameters.Count: > 0 } parameters ? "`" + parameters.Parameters.Count : ""));
    return (ns.Length == 0 ? "" : ns + ".") + string.Join("/", names);
}

static bool GeneratedPath(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
    .Any(segment => segment is "bin" or "obj");

static void NegativeChecks(string root, string suite, string report)
{
    var fixture = Path.Combine(Path.GetDirectoryName(report)!, "gate-negative.json");
    try
    {
        ExpectRejection(root, suite, fixture + ".missing", "missing report");
        foreach (var text in new[] { "", "{}" })
        {
            File.WriteAllText(fixture, text);
            ExpectRejection(root, suite, fixture, "empty report");
        }
        var original = JsonNode.Parse(File.ReadAllText(report))!.AsObject();
        foreach (var metric in new[] { "line", "branch", "method", "source", "type", "module" })
        {
            var mutated = original.DeepClone().AsObject();
            var module = mutated.First();
            var documents = module.Value!.AsObject();
            var document = documents.First();
            var types = document.Value!.AsObject();
            var type = types.First();
            var method = type.Value!.AsObject().First(item => item.Value!["Lines"]!.AsObject().Count > 0);
            if (metric == "line") method.Value!["Lines"]!.AsObject()[method.Value["Lines"]!.AsObject().First().Key] = 0;
            if (metric == "method") foreach (var line in method.Value!["Lines"]!.AsObject().ToArray()) method.Value["Lines"]![line.Key] = 0;
            if (metric == "branch")
            {
                var branch = documents.SelectMany(doc => doc.Value!.AsObject())
                    .SelectMany(item => item.Value!.AsObject())
                    .SelectMany(item => item.Value!["Branches"]!.AsArray()).First();
                branch!["Hits"] = 0;
            }
            if (metric == "source") documents.Remove(document.Key);
            if (metric == "type")
            {
                var declaredType = type.Key.Split('/')[0];
                foreach (var name in types.Select(item => item.Key).Where(name => name == declaredType || name.StartsWith(declaredType + "/", StringComparison.Ordinal)).ToArray())
                    types.Remove(name);
            }
            if (metric == "module") mutated.Remove(module.Key);
            File.WriteAllText(fixture, mutated.ToJsonString());
            ExpectRejection(root, suite, fixture, $"missing/uncovered {metric}");
        }
        Console.WriteLine($"{suite}: negative checks passed (missing/empty report, uncovered line/branch/method, omitted source/type/module).");
    }
    finally { File.Delete(fixture); }
}

static void ExpectRejection(string root, string suite, string report, string scenario)
{
    try { Verify(root, suite, report); }
    catch (Exception) { return; }
    throw new InvalidOperationException($"Gate incorrectly accepted {scenario}.");
}
