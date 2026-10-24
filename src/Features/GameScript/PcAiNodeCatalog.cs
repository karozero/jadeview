using System.Reflection;
using System.Text.Json;

namespace JadeView;

internal static class PcAiNodeCatalog
{
    private const string KeywordResourceName = "JadeView.GameScriptEngineKeywords";
    private static readonly Lazy<IReadOnlyDictionary<ushort, string>> KeywordNames = new(ReadKeywords);

    internal static string CategoryName(byte category) => category switch
    {
        0x01 => "Type",
        0x02 => "Keyword",
        0x03 => "Function",
        0x04 => "Field",
        0x05 => "Local variable",
        0x06 => "Local array",
        0x07 => "Local array 2D",
        0x08 => "Local array 3D",
        0x14 => "Global variable",
        0x15 => "Global array",
        0x16 => "Global array 2D",
        0x17 => "Global array 3D",
        0x18 => "Text",
        0x19 => "Initialize local array",
        0x1A => "Event parameter",
        0x1F => "End",
        _ => $"Unknown category 0x{category:X2}"
    };

    internal static string TypeName(ushort type) => type switch
    {
        0x20 => "bool",
        0x21 => "int",
        0x22 => "int (secondary encoding)",
        0x25 => "vector",
        0x26 => "string",
        0x27 => "pointer reference",
        0x28 => "GAO reference",
        0x29 => "message",
        0x2A => "pointer reference",
        0x2B => "pointer reference",
        0x2C => "text",
        0x2D => "key",
        0x2E => "color",
        >= 0x7C and <= 0x85 => "vector variant",
        _ => $"unknown type 0x{type:X4}"
    };

    internal static string? SymbolName(byte category, ushort value2)
    {
        if (category == 0x02 && KeywordNames.Value.TryGetValue(value2, out string? keyword))
            return keyword.StartsWith("AI_EvalKeyword_", StringComparison.Ordinal)
                ? keyword[15..]
                : keyword;

        if (category == 0x03)
        {
            string function = CinematicEngineFunctionCatalog.NameFor(value2);
            return function.StartsWith("AI_EvalFunc_", StringComparison.Ordinal)
                ? function[12..]
                : function;
        }

        return null;
    }

    private static IReadOnlyDictionary<ushort, string> ReadKeywords()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(KeywordResourceName)
            ?? throw new InvalidDataException($"Embedded keyword map '{KeywordResourceName}' is missing.");
        var serialized = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("Embedded keyword map is empty.");
        return serialized.ToDictionary(pair => Convert.ToUInt16(pair.Key, 16), pair => pair.Value);
    }
}
