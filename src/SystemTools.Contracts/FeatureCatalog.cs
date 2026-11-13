using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace SystemTools.Contracts;

public enum CapabilityState { Available, Disabled, MissingDependency, PermissionRequired, MissingDevice, RestartRequired, Unsupported }
public sealed record Capability(CapabilityState State, string Reason = "", bool RuntimeVerified = false)
{
    public bool CanExecute => State == CapabilityState.Available;
}
public sealed record FeatureDefinition(string Id, string Name, string Kind, string? RegistrationId = null, string? Group = null);
public sealed record FeatureStatus(FeatureDefinition Feature, Capability Capability);

/// <summary>Read-only probes are evaluated at use time; discovery never requests permissions.</summary>
public sealed class FeatureCatalog
{
    private readonly ConcurrentDictionary<string, (FeatureDefinition Definition, Func<Capability> Probe)> _features = new(StringComparer.Ordinal);
    public string RuntimeId { get; }
    public FeatureCatalog(string runtimeId)
    {
        RuntimeId = runtimeId;
        using var stream = typeof(FeatureCatalog).Assembly.GetManifestResourceStream("SystemTools.Features.json")!;
        foreach (var feature in JsonSerializer.Deserialize<FeatureDefinition[]>(stream)!)
            _features[feature.Id] = (feature, () => new(CapabilityState.Unsupported, "当前平台尚无实现。"));
    }
    public void Bind(string id, Func<Capability> probe)
    {
        if (!_features.TryGetValue(id, out var feature)) throw new ArgumentException("未知功能 ID：" + id);
        _features[id] = (feature.Definition, probe);
    }
    public void BindForRegistration(string id, Func<Capability> probe)
    {
        var initial = probe();
        Bind(id, () =>
        {
            var current = probe();
            return current.CanExecute && !initial.CanExecute
                ? new(CapabilityState.RestartRequired, "条件已就绪，请重启 ClassIsland 完成注册。") : current;
        });
    }
    public Capability Get(string id) => _features.TryGetValue(id, out var feature)
        ? feature.Probe() : new(CapabilityState.Unsupported, "功能未登记。" );
    public bool CanCreate(string id) => !Owns(id) || Get(id).CanExecute;
    public static bool Owns(string id) => id.StartsWith("SystemTools.", StringComparison.Ordinal);
    public void EnsureExecutable(string id)
    {
        var result = Get(id);
        if (!result.CanExecute) throw new InvalidOperationException($"{id} 不可用：{result.Reason}");
    }
    public IReadOnlyList<FeatureStatus> Snapshot() => _features.Values.OrderBy(x => x.Definition.Id, StringComparer.Ordinal)
        .Select(x => new FeatureStatus(x.Definition, x.Probe())).ToArray();
}
