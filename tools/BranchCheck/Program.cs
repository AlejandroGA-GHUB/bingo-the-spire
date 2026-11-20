using System.Reflection;
using System.Runtime.Loader;

// Usage: BranchCheck [modDll] [refsRoot]
// Defaults: the Debug build of the mod, and every branch folder under refs/ (refs/main, refs/beta, ...).
var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var modDll = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(repoRoot, "src", "StS2Bingo.Mod", "bin", "Debug", "net9.0", "StS2Bingo.dll"));
var refsRoot = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(repoRoot, "refs"));

if (!File.Exists(modDll))
{
    Console.Error.WriteLine($"Mod DLL not found: {modDll}. Build the solution first.");
    return 2;
}

var branches = Directory.Exists(refsRoot)
    ? Directory.GetDirectories(refsRoot).Where(d => File.Exists(Path.Combine(d, "sts2.dll"))).OrderBy(d => d).ToList()
    : new List<string>();
if (branches.Count == 0)
{
    Console.Error.WriteLine($"No branch folders with sts2.dll under {refsRoot}.");
    return 2;
}

var failures = 0;
foreach (var branchDir in branches)
{
    failures += CheckBranch(modDll, branchDir);
}
return failures == 0 ? 0 : 1;

static int CheckBranch(string modDll, string refsDir)
{
    var alc = new AssemblyLoadContext("branch-" + Path.GetFileName(refsDir), isCollectible: true);
    alc.Resolving += (ctx, name) =>
    {
        var candidate = Path.Combine(refsDir, name.Name + ".dll");
        return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
    };

    var problems = new List<string>();
    var connected = 0;
    try
    {
        var sts2 = alc.LoadFromAssemblyPath(Path.Combine(refsDir, "sts2.dll"));
        var abstractModel = sts2.GetType("MegaCrit.Sts2.Core.Models.AbstractModel", throwOnError: true)!;
        var mod = alc.LoadFromAssemblyPath(modDll);

        Type[] types;
        try
        {
            types = mod.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
            problems.AddRange(ex.LoaderExceptions.Select(e => $"type load failed: {e?.Message}"));
        }

        foreach (var type in types.Where(t => !t.IsAbstract && t.IsSubclassOf(abstractModel)))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!method.CustomAttributes.Any(a => a.AttributeType.Name == "EmitsAttribute"))
                {
                    continue;
                }
                if (method.GetBaseDefinition().DeclaringType == abstractModel)
                {
                    connected++;
                }
                else
                {
                    problems.Add($"{type.Name}.{method.Name} no longer overrides a game hook");
                }
            }
        }
    }
    catch (Exception ex)
    {
        problems.Add($"check failed: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        alc.Unload();
    }

    var version = ReadVersion(refsDir);
    Console.WriteLine($"[{Path.GetFileName(refsDir)}{version}] {connected} hooks connected, {problems.Count} problem(s)");
    foreach (var problem in problems)
    {
        Console.WriteLine("  - " + problem);
    }
    return problems.Count;
}

static string ReadVersion(string refsDir)
{
    var info = Path.Combine(refsDir, "release_info.json");
    if (!File.Exists(info))
    {
        return "";
    }
    var marker = "\"version\": \"";
    var text = File.ReadAllText(info);
    var start = text.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0)
    {
        return "";
    }
    start += marker.Length;
    var end = text.IndexOf('"', start);
    return end > start ? " " + text[start..end] : "";
}

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StS2Bingo.sln")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
