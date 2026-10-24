namespace JadeView;

/// <summary>Source-backed stack contracts for selected global-AI engine calls.</summary>
internal static class PcAiFunctionContractCatalog
{
    private static readonly IReadOnlyDictionary<string, PcAiFunctionContract> Contracts =
        new Dictionary<string, PcAiFunctionContract>(StringComparer.Ordinal)
        {
            ["WORLoadList"] = new("WORLoadList", 1, "INT",
                "external/irisEng/ai/ai_worldfunctions.c:100-120; external/jaded/src/libraries/AIinterp/Sources/Functions/AIdeffct.h:213"),
            ["WORActive"] = new("WORActive", 1, "VOID",
                "external/irisEng/ai/ai_worldfunctions.c:43-55; external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_wor.c:747-758"),
            ["WORLoaded"] = new("WORLoaded", 0, "INT",
                "external/irisEng/ai/ai_worldfunctions.c:8-17; external/jaded/src/libraries/AIinterp/Sources/Functions/AIdeffct.h:223; external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_wor.c:667-676",
                ConstantReturnValue: 1)
        };

    internal static bool TryGet(string operation, out PcAiFunctionContract contract) =>
        Contracts.TryGetValue(operation, out contract!);
}

internal sealed record PcAiFunctionContract(string Name, int ArgumentCount, string ReturnType, string Evidence,
    int? ConstantReturnValue = null);
