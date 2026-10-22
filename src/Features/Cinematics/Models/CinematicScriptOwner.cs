namespace JadeView;

public sealed record CinematicScriptOwner(
    uint OwnerKey,
    uint BrainKey,
    uint ScriptModelKey,
    uint WorldKey,
    uint EventsKey,
    uint SceneEventsKey,
    string Name,
    string[] CallbackNames,
    CinematicScriptObjectReference[] ObjectReferences,
    CinematicScriptVariable[] Variables,
    CinematicDialogueLine[]? DialogueLines = null)
{
    public uint SerializedGaoFlags { get; init; }
    public uint SerializedStateFlags { get; init; }
    public CinematicScriptCallbackSlot[]? CallbackSlots { get; init; }
    public string[] ModelFunctionNames { get; init; } = [];

    internal CinematicScriptOwnerId Id => new(WorldKey, OwnerKey);

    internal CinematicScriptVariable? EffectiveVariable(string name) =>
        Variables.LastOrDefault(variable => variable.Source == "instance"
            && variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? Variables.LastOrDefault(variable => variable.Source == "model"
            && variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal CinematicScriptIntegerArrayValue[] EffectiveIntegerArray(string name)
    {
        CinematicScriptVariable? model = Variables.LastOrDefault(variable => variable.Source == "model"
            && variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && variable.ArrayValueCount is not null);
        CinematicScriptVariable? instance = Variables.LastOrDefault(variable => variable.Source == "instance"
            && variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && variable.ArrayValueCount is not null);

        if (model == null) return ToEntries(instance, source: "instance");
        if (instance == null) return ToEntries(model, source: "model");
        if (model.IntegerArrayValues == null || instance.IntegerArrayValues == null
            || model.ArrayValueCount != model.IntegerArrayValues.Length
            || instance.ArrayValueCount != instance.IntegerArrayValues.Length)
            return [];

        int count = model.ArrayValueCount.Value;
        int instancePrefixCount = Math.Min(count, instance.IntegerArrayValues.Length);
        return Enumerable.Range(0, count).Select(index => index < instancePrefixCount
            ? new CinematicScriptIntegerArrayValue(index, instance.IntegerArrayValues[index], "instance")
            : new CinematicScriptIntegerArrayValue(index, model.IntegerArrayValues[index], "model"))
            .ToArray();

        static CinematicScriptIntegerArrayValue[] ToEntries(CinematicScriptVariable? variable, string source)
        {
            if (variable?.ArrayValueCount is not { } count || variable.IntegerArrayValues == null
                || count != variable.IntegerArrayValues.Length)
                return [];
            return variable.IntegerArrayValues.Select((value, index) =>
                new CinematicScriptIntegerArrayValue(index, value, source)).ToArray();
        }
    }

    internal CinematicScriptObjectReference? EffectiveObjectReference(string name) =>
        ObjectReferences.LastOrDefault(reference => reference.Source == "instance"
            && reference.VariableName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? ObjectReferences.LastOrDefault(reference => reference.Source == "model"
            && reference.VariableName.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed record CinematicScriptCallbackSlot(int Index, uint FunctionKey, string? FunctionName);

internal readonly record struct CinematicScriptOwnerId(uint WorldKey, uint OwnerKey);

public sealed record CinematicScriptObjectReference(string VariableName, uint TargetKey, string Source,
    string? TargetName, int? ArrayIndex = null);
public sealed record CinematicScriptVariable(string Name, ushort Type, string Source, string Value,
    int[]? IntegerArrayValues = null, int? ArrayValueCount = null);
public sealed record CinematicScriptIntegerArrayValue(int Index, int Value, string Source);
public sealed record CinematicDialogueLine(
    int Slot,
    uint TextBankKey,
    int TextId,
    int? SoundId = null,
    int? SoundVariantMax = null,
    float? TextBox = null,
    float? Volume = null,
    float? Near = null,
    float? Far = null,
    float? Duration = null,
    uint? SpeakerKey = null);
