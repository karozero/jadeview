using System.Text.RegularExpressions;

namespace JadeView;

internal static partial class CinematicAudioLocalization
{
    private static readonly Regex LocalizedStem = new("^(?<stem>.+)_(?<locale>[A-Z]{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static CinematicAudioCue[] Expand(CinematicAudioCue[] cues, IReadOnlyList<Entry> entries)
    {
        var entriesByKey = entries.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.First());
        var localizedGroups = entries
            .Select(entry => (Entry: entry, Match: LocalizedStem.Match(Path.GetFileNameWithoutExtension(entry.Name))))
            .Where(item => item.Match.Success && Path.GetExtension(item.Entry.Name).Equals(".wad", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Match.Groups["stem"].Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Match.Groups["locale"].Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(item => item.Match.Groups["locale"].Value, StringComparer.OrdinalIgnoreCase)
                .ToArray(), StringComparer.OrdinalIgnoreCase);

        var expanded = new List<CinematicAudioCue>(cues.Length);
        foreach (var cue in cues)
        {
            if (!entriesByKey.TryGetValue(cue.SoundKey, out var sourceEntry)
                || !localizedGroups.TryGetValue(GetLocalizedStem(sourceEntry.Name), out var variants))
            {
                expanded.Add(cue);
                continue;
            }

            string choiceGroup = cue.ChoiceGroup
                ?? $"localized-audio:{cue.OwnerKey:X8}:{cue.ModifierId}:{cue.Start:R}:{cue.SoundKey:X8}";
            var orderedVariants = OrderCurrentFirst(variants, cue.SoundKey).ToArray();
            for (int variantIndex = 0; variantIndex < orderedVariants.Length; variantIndex++)
            {
                var variant = orderedVariants[variantIndex];
                string locale = variant.Match.Groups["locale"].Value;
                string label = $"Audio bank · {locale} · {variant.Entry.Name}";
                expanded.Add(cue with
                {
                    SoundKey = variant.Entry.Key,
                    Label = label,
                    ChoiceGroup = choiceGroup,
                    ChoiceIndex = variantIndex,
                    ChoiceCount = variants.Length
                });
            }
        }

        return expanded.OrderBy(cue => cue.Start).ThenBy(cue => cue.OwnerKey)
            .ThenBy(cue => cue.ModifierId).ThenBy(cue => cue.ChoiceIndex).ToArray();
    }

    private static IEnumerable<(Entry Entry, Match Match)> OrderCurrentFirst(
        (Entry Entry, Match Match)[] variants, uint currentKey) =>
        variants.OrderByDescending(variant => variant.Entry.Key == currentKey)
            .ThenBy(variant => variant.Match.Groups["locale"].Value, StringComparer.OrdinalIgnoreCase);

    private static string GetLocalizedStem(string name)
    {
        Match match = LocalizedStem.Match(Path.GetFileNameWithoutExtension(name));
        return match.Success ? match.Groups["stem"].Value : string.Empty;
    }
}
