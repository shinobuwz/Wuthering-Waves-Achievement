using System.Collections.ObjectModel;

namespace Wuwa.Core;

public enum RotationBindingAction
{
    Start,
    Reset,
    Reselect,
    Basic,
    Skill,
    Liberation,
    Echo,
    Jump,
    Dodge,
    Execution,
    Intro1,
    Intro2,
    Intro3
}

public enum RotationInputDevice { Keyboard, Mouse }

public readonly record struct RotationPhysicalInput(RotationInputDevice Device, int Code)
{
    public override string ToString() => Device == RotationInputDevice.Keyboard ? $"Key {Code}" : $"Mouse {Code}";
}

public sealed record RotationBinding(RotationBindingAction Action, RotationPhysicalInput Input);

public sealed class RotationBindingSet
{
    private readonly IReadOnlyDictionary<RotationBindingAction, RotationPhysicalInput> _bindings;

    public RotationBindingSet(IEnumerable<RotationBinding>? bindings = null)
    {
        var dictionary = new Dictionary<RotationBindingAction, RotationPhysicalInput>();
        foreach (var binding in bindings ?? Array.Empty<RotationBinding>()) dictionary[binding.Action] = binding.Input;
        _bindings = new ReadOnlyDictionary<RotationBindingAction, RotationPhysicalInput>(dictionary);
    }

    public IReadOnlyDictionary<RotationBindingAction, RotationPhysicalInput> Bindings => _bindings;
    public bool TryGet(RotationBindingAction action, out RotationPhysicalInput input) => _bindings.TryGetValue(action, out input);

    public RotationBindingSet With(RotationBindingAction action, RotationPhysicalInput? input)
    {
        var bindings = _bindings.Select(pair => new RotationBinding(pair.Key, pair.Value)).ToList();
        bindings.RemoveAll(binding => binding.Action == action);
        if (input is not null) bindings.Add(new(action, input.Value));
        return new(bindings);
    }

    public static RotationBindingSet CreateDefaults() => new(new[]
    {
        new RotationBinding(RotationBindingAction.Start, new(RotationInputDevice.Keyboard, 0x74)), // F5
        new RotationBinding(RotationBindingAction.Reset, new(RotationInputDevice.Keyboard, 0x75)), // F6
        new RotationBinding(RotationBindingAction.Reselect, new(RotationInputDevice.Keyboard, 0x76)), // F7
        new RotationBinding(RotationBindingAction.Basic, new(RotationInputDevice.Mouse, 1)),
        new RotationBinding(RotationBindingAction.Skill, new(RotationInputDevice.Keyboard, 0x45)), // E
        new RotationBinding(RotationBindingAction.Liberation, new(RotationInputDevice.Keyboard, 0x52)), // R
        new RotationBinding(RotationBindingAction.Echo, new(RotationInputDevice.Keyboard, 0x51)), // Q
        new RotationBinding(RotationBindingAction.Jump, new(RotationInputDevice.Keyboard, 0x20)),
        new RotationBinding(RotationBindingAction.Dodge, new(RotationInputDevice.Keyboard, 0x10)),
        new RotationBinding(RotationBindingAction.Execution, new(RotationInputDevice.Mouse, 2)),
        new RotationBinding(RotationBindingAction.Intro1, new(RotationInputDevice.Keyboard, 0x31)),
        new RotationBinding(RotationBindingAction.Intro2, new(RotationInputDevice.Keyboard, 0x32)),
        new RotationBinding(RotationBindingAction.Intro3, new(RotationInputDevice.Keyboard, 0x33))
    });
}

public sealed record RotationSettings(
    RotationBindingSet Bindings,
    TimeSpan HeavyThreshold,
    RotationProfileId? SelectedProfileId = null)
{
    public static RotationSettings Default { get; } = new(RotationBindingSet.CreateDefaults(), TimeSpan.FromMilliseconds(500));
}

public static class RotationBindingValidator
{
    public static RotationValidationResult Validate(RotationProfile? profile, RotationBindingSet? bindings)
    {
        var issues = new List<RotationIssue>();
        if (bindings is null)
        {
            issues.Add(new("bindings.missing", "按键设置不存在。"));
            return new(issues);
        }

        foreach (var duplicate in bindings.Bindings
                     .GroupBy(pair => pair.Value)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new(
                "bindings.duplicate",
                $"物理输入 {duplicate.Key} 同时绑定到：{string.Join("、", duplicate.Select(pair => pair.Key))}。"));
        }

        var required = RequiredActions(profile);
        foreach (var action in required.Where(action => !bindings.Bindings.ContainsKey(action)))
            issues.Add(new("bindings.required", $"当前流程缺少必需绑定：{GetDisplayName(action)}。"));
        return new(issues.AsReadOnly());
    }

    public static IReadOnlySet<RotationBindingAction> RequiredActions(RotationProfile? profile)
    {
        var actions = new HashSet<RotationBindingAction>
        {
            RotationBindingAction.Start,
            RotationBindingAction.Reset,
            RotationBindingAction.Reselect
        };
        if (profile is null) return actions;
        foreach (var step in profile.Opener.Concat(profile.Loop)) actions.Add(ToBindingAction(step));
        return actions;
    }

    public static RotationBindingAction ToBindingAction(RotationStep step) => step.Action switch
    {
        RotationActionKind.Basic or RotationActionKind.Heavy => RotationBindingAction.Basic,
        RotationActionKind.Skill => RotationBindingAction.Skill,
        RotationActionKind.Liberation => RotationBindingAction.Liberation,
        RotationActionKind.Echo => RotationBindingAction.Echo,
        RotationActionKind.Jump => RotationBindingAction.Jump,
        RotationActionKind.Dodge => RotationBindingAction.Dodge,
        RotationActionKind.Execution => RotationBindingAction.Execution,
        RotationActionKind.Intro when step.TargetSlot == 1 => RotationBindingAction.Intro1,
        RotationActionKind.Intro when step.TargetSlot == 2 => RotationBindingAction.Intro2,
        RotationActionKind.Intro when step.TargetSlot == 3 => RotationBindingAction.Intro3,
        _ => throw new InvalidDataException("Rotation step cannot be mapped to a binding action.")
    };

    public static string GetDisplayName(RotationBindingAction action) => action switch
    {
        RotationBindingAction.Start => "Start",
        RotationBindingAction.Reset => "Reset",
        RotationBindingAction.Reselect => "Reselect",
        RotationBindingAction.Basic => "Basic/Heavy",
        RotationBindingAction.Skill => "Skill",
        RotationBindingAction.Liberation => "Liberation",
        RotationBindingAction.Echo => "Echo",
        RotationBindingAction.Jump => "Jump",
        RotationBindingAction.Dodge => "Dodge",
        RotationBindingAction.Execution => "Execution",
        RotationBindingAction.Intro1 => "Intro 1",
        RotationBindingAction.Intro2 => "Intro 2",
        RotationBindingAction.Intro3 => "Intro 3",
        _ => action.ToString()
    };
}
