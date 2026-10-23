using System.Numerics;

namespace JadeView;

internal sealed class CinematicScreenFadeTimeline
{
    private sealed record Fade(float Time, float[] Durations, Vector4 Start, Vector4 End);
    private readonly List<Fade> fades = [];
    internal int Count => fades.Count;
    internal string[] Describe() => fades.Select(fade =>
        $"t={fade.Time:F3}s durations=[{string.Join(",", fade.Durations.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}] "
        + $"from={fade.Start} to={fade.End}").ToArray();

    internal CinematicScreenFadeTimeline(AnimationClip source, WorldData world, SceneDocument document)
    {
        var receivers = world.Objects.Select(item => item.GaoKey)
            .Where(document.CinematicFadeReceivers.Contains).ToHashSet();
        foreach (var track in source.Tracks.Where(track => track.Bone < 0))
            foreach (var key in track.Keys)
                foreach (var payload in key.OpaquePayloads)
                {
                    foreach (uint context in CinematicOwnerContext.ForTrack(source, track))
                        if (ReadFade(key.Time, context, payload, receivers) is { } fade) fades.Add(fade);
                }
        fades = fades.OrderBy(fade => fade.Time).ToList();
    }

    private static Fade? ReadFade(float time, uint context, AnimationOpaquePayload payload, HashSet<uint> receivers)
    {
        if (!CinematicFacialEvent.TryDecodeMessage(payload, out var message, allowLiteralFlags: true)
            || BitConverter.ToUInt32(message, 48) != 50) return null;
        uint target = BitConverter.ToUInt32(payload.Data, 4);
        if (!receivers.Contains(target == 0 ? context : target)) return null;
        // Global_glob_reflex selects material 9 for f2=0 (ordinary alpha fade).
        // Alternate material 8 and script priority arbitration are not emulated.
        if (BitConverter.ToSingle(message, CinematicMessageFields.Float2) != 0) return null;
        float[] durations = [Read(8), Read(12), Read(16), Read(20), Read(24)];
        float total = durations.Sum();
        bool validTiming = float.IsFinite(time) && float.IsFinite(total) && total > 0
            && durations.All(value => float.IsFinite(value) && value >= 0);
        return validTiming ? new(time, durations, Color(40), Color(44)) : null;

        float Read(int offset) => BitConverter.ToSingle(message, offset);
        Vector4 Color(int offset)
        {
            uint packed = BitConverter.ToUInt32(message, offset);
            return new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, packed >> 24) / 255;
        }
    }

    internal Vector4? Evaluate(float time)
    {
        // Each message deletes/replaces the preceding effect in native slot 10.
        var fade = fades.LastOrDefault(item => item.Time <= time);
        if (fade == null) return null;
        float elapsed = time - fade.Time;
        for (int phase = 0; phase < fade.Durations.Length; phase++)
        {
            float duration = fade.Durations[phase];
            if (duration > 0 && elapsed < duration)
            {
                float amount = phase switch { 1 => elapsed / duration, 2 => 1, 3 => 1 - elapsed / duration, _ => 0 };
                return Vector4.Lerp(fade.Start, fade.End, amount);
            }
            elapsed -= duration;
        }
        return null;
    }
}
