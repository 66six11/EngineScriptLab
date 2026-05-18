using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

namespace ScriptLab;

public sealed record DebugInspectableField(
    int EntityId,
    string BehaviorId,
    string FieldId,
    string Name,
    string Type,
    string Accessibility,
    string Serialization,
    object? Value);

public sealed record DebugRuntimeProbeEvent(long Sequence, string Kind, int ProbeId, string? PinId, object? Value);

public sealed class DebugScriptHost
{
    private readonly AssemblyLoadContext loadContext;
    private readonly Assembly assembly;
    private readonly DebugProbeManifest? probeManifest;
    private readonly Dictionary<DebugScriptInstanceKey, DebugScriptInstance> instances = new();

    private DebugScriptHost(
        AssemblyLoadContext loadContext,
        Assembly assembly,
        DebugProbeManifest? probeManifest)
    {
        this.loadContext = loadContext;
        this.assembly = assembly;
        this.probeManifest = probeManifest;
    }

    public AssemblyLoadContext LoadContext => loadContext;

    public Assembly Assembly => assembly;

    public DebugProbeManifest? ProbeManifest => probeManifest;

    public static DebugScriptHost Load(DebugScriptEmitResult emit)
    {
        return Load(emit.AssemblyPath, emit.PdbPath, emit.ProbeManifest);
    }

    public static DebugScriptHost Load(string assemblyPath, string? pdbPath = null)
    {
        return Load(assemblyPath, pdbPath, probeManifest: null);
    }

    public static DebugScriptHost Load(
        string assemblyPath,
        string? pdbPath,
        DebugProbeManifest? probeManifest)
    {
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var pdbBytes = !string.IsNullOrWhiteSpace(pdbPath) && File.Exists(pdbPath)
            ? File.ReadAllBytes(pdbPath)
            : null;
        var loadContext = new AssemblyLoadContext(
            $"ScriptLab.Debug.{Path.GetFileNameWithoutExtension(assemblyPath)}.{Guid.NewGuid():N}",
            isCollectible: true);
        var assembly = pdbBytes is null
            ? loadContext.LoadFromStream(new MemoryStream(assemblyBytes))
            : loadContext.LoadFromStream(
                new MemoryStream(assemblyBytes),
                new MemoryStream(pdbBytes));

        return new DebugScriptHost(loadContext, assembly, probeManifest);
    }

    public DebugScriptInstance MountBehavior(int entityId, string behaviorId)
    {
        var key = new DebugScriptInstanceKey(entityId, behaviorId);
        if (instances.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var behaviorType = ResolveBehaviorType(behaviorId);
        var target = Activator.CreateInstance(behaviorType, nonPublic: true)
            ?? throw new InvalidOperationException($"Could not create behavior '{behaviorId}'.");
        var instance = new DebugScriptInstance(entityId, behaviorId, behaviorType, target);
        instances.Add(key, instance);
        return instance;
    }

    public DebugScriptInstance GetInstance(int entityId, string behaviorId)
    {
        var key = new DebugScriptInstanceKey(entityId, behaviorId);
        if (instances.TryGetValue(key, out var instance))
        {
            return instance;
        }

        throw new InvalidOperationException(
            $"Behavior '{behaviorId}' is not mounted on entity {entityId.ToString(CultureInfo.InvariantCulture)}.");
    }

    public IReadOnlyList<DebugInspectableField> GetFields(int entityId, string behaviorId)
    {
        return GetInstance(entityId, behaviorId).GetFields();
    }

    public object? GetFieldValue(int entityId, string behaviorId, string fieldId)
    {
        return GetInstance(entityId, behaviorId).GetFieldValue(fieldId);
    }

    public void SetFieldValue(int entityId, string behaviorId, string fieldId, object? value)
    {
        GetInstance(entityId, behaviorId).SetFieldValue(fieldId, value);
    }

    public void SetInputKeyDown(string keyName, bool isDown)
    {
        var keyType = assembly.GetType("Asharia.Behavior.Key", throwOnError: true)!;
        var inputType = assembly.GetType("Asharia.Behavior.Input", throwOnError: true)!;
        var key = Enum.Parse(keyType, keyName, ignoreCase: false);
        inputType
            .GetMethod("SetKeyDown", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new[] { key, isDown });
    }

    public void ClearInput()
    {
        assembly
            .GetType("Asharia.Behavior.Input", throwOnError: true)!
            .GetMethod("Clear", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
    }

    public void SetBreakpoint(int probeId, bool enabled)
    {
        GetDebugProbeType()
            .GetMethod("SetBreakpoint", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { probeId, enabled });
    }

    public void SetBreakpointByGraphNodeId(string graphNodeId, bool enabled)
    {
        var site = ResolveProbeSiteByGraphNodeId(graphNodeId);
        SetBreakpoint(site.ProbeId, enabled);
    }

    public void SetBreakpointByDebugSiteId(string debugSiteId, bool enabled)
    {
        var site = ResolveProbeSiteByDebugSiteId(debugSiteId);
        SetBreakpoint(site.ProbeId, enabled);
    }

    public void ClearBreakpoints()
    {
        GetDebugProbeType()
            .GetMethod("ClearBreakpoints", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
    }

    public IReadOnlyList<int> GetBreakpointProbeIds()
    {
        var breakpoints = (System.Collections.IEnumerable)GetDebugProbeType()
            .GetProperty("Breakpoints", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        return breakpoints.Cast<object>().Select(Convert.ToInt32).Order().ToArray();
    }

    public void ClearProbeEvents()
    {
        GetDebugProbeType()
            .GetMethod("Clear", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
    }

    public IReadOnlyList<DebugRuntimeProbeEvent> GetProbeEvents()
    {
        var events = (System.Collections.IEnumerable)GetDebugProbeType()
            .GetProperty("Events", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        return events
            .Cast<object>()
            .Select(probeEvent => new DebugRuntimeProbeEvent(
                GetProperty<long>(probeEvent, "Sequence"),
                GetRequiredProperty<string>(probeEvent, "Kind"),
                GetRequiredProperty<int>(probeEvent, "ProbeId"),
                GetProperty<string?>(probeEvent, "PinId"),
                GetProperty<object?>(probeEvent, "Value")))
            .ToArray();
    }

    public DebugProbeSite ResolveProbeSiteByGraphNodeId(string graphNodeId)
    {
        if (probeManifest is null)
        {
            throw new InvalidOperationException("Cannot resolve graph node breakpoints without a probe manifest.");
        }

        return probeManifest.Probes.SingleOrDefault(site =>
                string.Equals(site.GraphNodeId, graphNodeId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Could not resolve graph node '{graphNodeId}'.");
    }

    public DebugProbeSite ResolveProbeSiteByDebugSiteId(string debugSiteId)
    {
        if (probeManifest is null)
        {
            throw new InvalidOperationException("Cannot resolve debug site breakpoints without a probe manifest.");
        }

        return probeManifest.Probes.SingleOrDefault(site =>
                string.Equals(site.DebugSiteId, debugSiteId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Could not resolve debug site '{debugSiteId}'.");
    }

    private Type GetDebugProbeType()
    {
        return assembly.GetType("Asharia.Behavior.DebugProbe", throwOnError: true)!;
    }

    private Type ResolveBehaviorType(string behaviorId)
    {
        var type = assembly.GetType(behaviorId, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        type = assembly
            .GetTypes()
            .FirstOrDefault(candidate => HasBehaviorId(candidate, behaviorId));

        if (type is not null)
        {
            return type;
        }

        throw new InvalidOperationException($"Could not resolve behavior '{behaviorId}'.");
    }

    private static bool HasBehaviorId(Type type, string behaviorId)
    {
        return type.GetCustomAttributes(inherit: false)
            .Any(attribute =>
                DebugScriptReflection.AttributeMatches(attribute.GetType(), "Behavior") &&
                string.Equals(GetStringProperty(attribute, "Id"), behaviorId, StringComparison.Ordinal));
    }

    private static string? GetStringProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName)?.GetValue(instance) as string;
    }

    private static T GetRequiredProperty<T>(object instance, string propertyName)
    {
        return (T)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        return (T)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;
    }

    private readonly record struct DebugScriptInstanceKey(int EntityId, string BehaviorId);
}

public sealed class DebugScriptInstance
{
    private readonly Type behaviorType;
    private readonly object target;

    internal DebugScriptInstance(int entityId, string behaviorId, Type behaviorType, object target)
    {
        EntityId = entityId;
        BehaviorId = behaviorId;
        this.behaviorType = behaviorType;
        this.target = target;
    }

    public int EntityId { get; }

    public string BehaviorId { get; }

    public object Target => target;

    public object? InvokeUpdate(float delta)
    {
        return Invoke("Update", delta);
    }

    public object? Invoke(string methodName, params object?[] arguments)
    {
        var method = behaviorType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Could not resolve method '{methodName}' on behavior '{BehaviorId}'.");

        return method.Invoke(target, arguments);
    }

    public IReadOnlyList<DebugInspectableField> GetFields()
    {
        return GetInspectableFields()
            .Select(field => new DebugInspectableField(
                EntityId,
                BehaviorId,
                GetFieldId(field),
                field.Name,
                GetFriendlyTypeName(field.FieldType),
                GetAccessibility(field),
                GetSerialization(field),
                field.GetValue(target)))
            .ToArray();
    }

    public object? GetFieldValue(string fieldId)
    {
        return ResolveField(fieldId).GetValue(target);
    }

    public void SetFieldValue(string fieldId, object? value)
    {
        var field = ResolveField(fieldId);
        field.SetValue(target, ConvertValue(value, field.FieldType));
    }

    private FieldInfo ResolveField(string fieldId)
    {
        return GetInspectableFields().FirstOrDefault(field =>
                string.Equals(GetFieldId(field), fieldId, StringComparison.Ordinal) ||
                string.Equals(field.Name, fieldId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Could not resolve field '{fieldId}' on behavior '{BehaviorId}'.");
    }

    private IReadOnlyList<FieldInfo> GetInspectableFields()
    {
        return behaviorType
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(IsInspectableField)
            .OrderBy(field => field.MetadataToken)
            .ToArray();
    }

    private static bool IsInspectableField(FieldInfo field)
    {
        if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
        {
            return false;
        }

        return field.IsPublic ||
            HasAttribute(field, "Field") ||
            HasAttribute(field, "SerializeField");
    }

    private static string GetFieldId(FieldInfo field)
    {
        return field.Name;
    }

    private static string GetSerialization(FieldInfo field)
    {
        return HasAttribute(field, "Field") || HasAttribute(field, "SerializeField")
            ? "explicit"
            : "public";
    }

    private static string GetAccessibility(FieldInfo field)
    {
        if (field.IsPublic)
        {
            return "public";
        }

        if (field.IsFamily)
        {
            return "protected";
        }

        if (field.IsAssembly)
        {
            return "internal";
        }

        if (field.IsFamilyOrAssembly)
        {
            return "protected internal";
        }

        return "private";
    }

    private static object? ConvertValue(object? value, Type fieldType)
    {
        if (value is null)
        {
            return Nullable.GetUnderlyingType(fieldType) is null && fieldType.IsValueType
                ? Activator.CreateInstance(fieldType)
                : null;
        }

        var targetType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType.IsEnum)
        {
            return value is string enumName
                ? Enum.Parse(targetType, enumName, ignoreCase: false)
                : Enum.ToObject(targetType, value);
        }

        if (targetType == typeof(Guid) && value is string guidText)
        {
            return Guid.Parse(guidText);
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
        {
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException(
            $"Cannot assign value of type '{value.GetType().FullName}' to field type '{fieldType.FullName}'.");
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        var typeName = targetType == typeof(bool) ? "bool" :
            targetType == typeof(byte) ? "byte" :
            targetType == typeof(short) ? "short" :
            targetType == typeof(int) ? "int" :
            targetType == typeof(long) ? "long" :
            targetType == typeof(float) ? "float" :
            targetType == typeof(double) ? "double" :
            targetType == typeof(decimal) ? "decimal" :
            targetType == typeof(string) ? "string" :
            targetType.FullName ?? targetType.Name;

        return Nullable.GetUnderlyingType(type) is null ? typeName : $"{typeName}?";
    }

    private static bool HasAttribute(FieldInfo field, string expectedName)
    {
        return field
            .GetCustomAttributes(inherit: false)
            .Any(attribute => DebugScriptReflection.AttributeMatches(attribute.GetType(), expectedName));
    }
}

public static class DebugScriptHostReporter
{
    public static void Write(DebugScriptInstance instance, TextWriter writer)
    {
        writer.WriteLine("Inspector:");
        writer.WriteLine($"  Entity: {instance.EntityId.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine($"  BehaviorId: {instance.BehaviorId}");
        writer.WriteLine("  Fields:");

        var fields = instance.GetFields();
        if (fields.Count == 0)
        {
            writer.WriteLine("    <none>");
            return;
        }

        foreach (var field in fields)
        {
            writer.WriteLine(
                $"    {field.FieldId} : {field.Type} = {FormatValue(field.Value)} [{field.Accessibility}, {field.Serialization}]");
        }
    }

    private static string FormatValue(object? value)
    {
        return value is null
            ? "null"
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty;
    }
}

internal static class DebugScriptReflection
{
    public static bool AttributeMatches(Type attributeType, string expectedName)
    {
        return attributeType.Name == expectedName ||
            attributeType.Name == $"{expectedName}Attribute";
    }
}
