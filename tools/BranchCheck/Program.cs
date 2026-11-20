using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

// Usage: BranchCheck [modDll] [refsRoot]
// Defaults: the Debug build of the mod, and every branch folder under refs/ (refs/main, refs/beta, ...).
var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var modDll = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(repoRoot, "src", "BingoTheSpire.Mod", "bin", "Debug", "net9.0", "BingoTheSpire.dll"));
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
    var resolved = 0;
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

        // Every game type, method and field the mod's code uses must exist on this branch, or the
        // game throws MissingMethodException the first time that code runs.
        (resolved, var missing) = CheckGameReferences(modDll, sts2);
        problems.AddRange(missing);

        // Harmony patches: their TargetMethod() must find a method in this branch's game code.
        // Read the [PatchTarget] attributes as data, so no game code has to run.
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        foreach (var type in types)
        {
            var targets = type.CustomAttributes.Where(a => a.AttributeType.Name == "PatchTargetAttribute").ToList();
            if (targets.Count == 0)
            {
                continue;
            }
            var found = targets.Any(a =>
            {
                var declaring = (Type)a.ConstructorArguments[0].Value!;
                var name = (string)a.ConstructorArguments[1].Value!;
                var parameters = ((IEnumerable<CustomAttributeTypedArgument>)a.ConstructorArguments[2].Value!)
                    .Select(p => (Type)p.Value!).ToArray();
                return declaring.GetMethod(name, all, null, parameters, null) != null;
            });
            if (found)
            {
                connected++;
            }
            else
            {
                problems.Add($"{type.Name}: no patch target exists on this branch");
            }
        }
    }
    catch (Exception ex)
    {
        problems.Add($"check failed: {(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
    }
    finally
    {
        alc.Unload();
    }

    var version = ReadVersion(refsDir);
    Console.WriteLine($"[{Path.GetFileName(refsDir)}{version}] {connected} hooks and patches connected, {resolved} game members resolved, {problems.Count} problem(s)");
    foreach (var problem in problems)
    {
        Console.WriteLine("  - " + problem);
    }
    return problems.Count;
}

/// <summary>
/// Resolves every reference from the mod into the game's assembly (types, methods, fields) against
/// this branch's sts2.dll. Methods match by name, parameter count and parameter types.
/// </summary>
static (int Resolved, List<string> Missing) CheckGameReferences(string modDll, Assembly sts2)
{
    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    using var stream = File.OpenRead(modDll);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    var names = new SignatureNames();
    var missing = new List<string>();
    var resolved = 0;

    string? GameTypeName(TypeReferenceHandle handle)
    {
        var typeRef = md.GetTypeReference(handle);
        var scope = typeRef.ResolutionScope;
        var name = md.GetString(typeRef.Name);
        if (scope.Kind == HandleKind.TypeReference)
        {
            var outer = GameTypeName((TypeReferenceHandle)scope);
            return outer == null ? null : outer + "+" + name;
        }
        if (scope.Kind != HandleKind.AssemblyReference
            || md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name) != "sts2")
        {
            return null;
        }
        var ns = md.GetString(typeRef.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    foreach (var handle in md.TypeReferences)
    {
        if (GameTypeName(handle) is { } typeName && sts2.GetType(typeName) == null)
        {
            missing.Add($"game type {typeName} doesn't exist");
        }
    }

    foreach (var handle in md.MemberReferences)
    {
        var member = md.GetMemberReference(handle);
        if (member.Parent.Kind != HandleKind.TypeReference || GameTypeName((TypeReferenceHandle)member.Parent) is not { } typeName)
        {
            continue;
        }
        var type = sts2.GetType(typeName);
        if (type == null)
        {
            continue; // already reported above
        }
        var name = md.GetString(member.Name);
        if (member.GetKind() == MemberReferenceKind.Field)
        {
            if (type.GetField(name, all) != null)
            {
                resolved++;
            }
            else
            {
                missing.Add($"field {typeName}.{name} doesn't exist");
            }
            continue;
        }

        var signature = member.DecodeMethodSignature(names, null);
        var wanted = signature.ParameterTypes;
        IEnumerable<MethodBase> candidates = name == ".ctor"
            ? type.GetConstructors(all)
            : type.GetMethods(all).Where(m => m.Name == name)
                .Concat(type.IsInterface ? type.GetInterfaces().SelectMany(i => i.GetMethods().Where(m => m.Name == name)) : []);
        var match = candidates.Any(m =>
        {
            var actual = m.GetParameters();
            return actual.Length == wanted.Length
                && actual.Zip(wanted).All(p => SignatureNames.Same(SignatureNames.Of(p.First.ParameterType), p.Second));
        });
        if (match)
        {
            resolved++;
        }
        else
        {
            missing.Add($"method {typeName}.{name}({string.Join(", ", wanted)}) doesn't exist");
        }
    }
    return (resolved, missing.Distinct().ToList());
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
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BingoTheSpire.sln")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

/// <summary>
/// Type names from metadata signatures, in the same shape as <see cref="Of"/> gives for reflected
/// types: full names, '+' for nested types, generic types by definition only, '?' for generic parameters.
/// </summary>
sealed class SignatureNames : ISignatureTypeProvider<string, object?>
{
    public static string Of(Type t) =>
        t.IsByRef ? Of(t.GetElementType()!) + "&"
        : t.IsPointer ? Of(t.GetElementType()!) + "*"
        : t.IsArray ? Of(t.GetElementType()!) + "[]"
        : t.IsGenericParameter || t.ContainsGenericParameters && !t.IsGenericType ? "?"
        : t.IsGenericType ? t.GetGenericTypeDefinition().FullName ?? t.Name
        : t.FullName ?? t.Name;

    public static bool Same(string a, string b) => a == b || a.Contains('?') || b.Contains('?');

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => "System.Void",
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        if (def.GetDeclaringType() is { IsNil: false } outer)
        {
            return GetTypeFromDefinition(reader, outer, 0) + "+" + name;
        }
        var ns = reader.GetString(def.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        var name = reader.GetString(typeRef.Name);
        if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return GetTypeFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, 0) + "+" + name;
        }
        var ns = reader.GetString(typeRef.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetGenericMethodParameter(object? context, int index) => "?";
    public string GetGenericTypeParameter(object? context, int index) => "?";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "?";
}
