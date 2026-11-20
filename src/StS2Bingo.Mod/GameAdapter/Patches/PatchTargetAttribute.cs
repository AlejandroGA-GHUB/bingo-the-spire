using System.Reflection;
using HarmonyLib;

namespace StS2Bingo.Mod.GameAdapter.Patches;

/// <summary>
/// A method a Harmony patch may attach to. Put several on one patch class when the method has a
/// different name or parameters on another branch; the first that exists is used (lowest
/// <see cref="Order"/> first, main's target conventionally first).
/// Declared as data so tools/BranchCheck can verify targets without running game code.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class PatchTargetAttribute(Type declaringType, string methodName, params Type[] parameters) : Attribute
{
    public Type DeclaringType { get; } = declaringType;

    public string MethodName { get; } = methodName;

    public Type[] Parameters { get; } = parameters;

    public int Order { get; init; }

    /// <summary>The first declared target that exists in the running game, or null.</summary>
    public static MethodBase? Resolve(Type patchClass) =>
        patchClass.GetCustomAttributes<PatchTargetAttribute>()
            .OrderBy(t => t.Order)
            .Select(t => (MethodBase?)AccessTools.Method(t.DeclaringType, t.MethodName, t.Parameters))
            .FirstOrDefault(m => m != null);
}
