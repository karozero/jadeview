using System.Numerics;

namespace JadeView;

public sealed partial record CinematicAction
{
    internal Matrix4x4 Motion(float elapsed, int? frequency, float startFrame)
    {
        if (Items.Length == 0) return Matrix4x4.Identity;
        foreach (var item in Items) item.Clip.EnsureLoaded();
        float rate = Rate(Items[0], frequency);
        float offset = rate > 0 ? startFrame / 60f / rate : 0;
        var initial = Accumulate(offset, frequency);
        return Matrix4x4.Invert(initial, out var inverse)
            ? Accumulate(Math.Max(0, elapsed) + offset, frequency) * inverse : Matrix4x4.Identity;
    }

    private Matrix4x4 Accumulate(float elapsed, int? frequency)
    {
        var result = Matrix4x4.Identity;
        float total = 0;
        foreach (var item in Items) total += Duration(item, frequency);
        if (total <= 0) return result;
        if (elapsed >= total && LoopItem != byte.MaxValue)
        {
            int loopStart = Math.Clamp(LoopItem, 0, Items.Length - 1);
            float prefix = 0;
            for (int index = 0; index < loopStart; index++)
                prefix += Duration(Items[index], frequency);
            float cycle = total - prefix;
            if (cycle > 0 && float.IsFinite(cycle))
            {
                result = RangeMotion(0, loopStart);
                int repetitions = (int)Math.Min((elapsed - prefix) / cycle, int.MaxValue);
                result = Power(RangeMotion(loopStart, Items.Length), repetitions) * result;
                elapsed = (elapsed - prefix) % cycle;
                return PartialMotion(loopStart, elapsed, frequency) * result;
            }
        }
        return PartialMotion(0, Math.Min(elapsed, total), frequency);
    }

    private Matrix4x4 PartialMotion(int start, float elapsed, int? frequency)
    {
        var result = Matrix4x4.Identity;
        for (int index = start; index < Items.Length; index++)
        {
            var item = Items[index];
            float duration = Duration(item, frequency);
            float localTime = Math.Min(elapsed, duration) * Rate(item, frequency);
            if (!float.IsFinite(localTime)) localTime = 0;
            result = ItemMotion(item, localTime) * result;
            if (elapsed <= duration) break;
            elapsed -= duration;
        }
        return result;
    }

    private Matrix4x4 RangeMotion(int start, int end)
    {
        var result = Matrix4x4.Identity;
        for (int index = start; index < end; index++)
            result = ItemMotion(Items[index], Items[index].Clip.Duration * Math.Max(1, Items[index].Repetitions)) * result;
        return result;
    }

    private static Matrix4x4 ItemMotion(CinematicActionItem item, float time)
    {
        float duration = item.Clip.Duration;
        if (duration <= 0) return Matrix4x4.Identity;
        var initial = CinematicTransformSampler.EvaluateRoot(item.Clip, 0);
        if (!Matrix4x4.Invert(initial, out var inverse)) return Matrix4x4.Identity;
        var end = CinematicTransformSampler.EvaluateRoot(item.Clip, duration) * inverse;
        int cycles = (int)Math.Min(time / duration, int.MaxValue);
        float remainder = time % duration;
        var motion = CinematicTransformSampler.EvaluateRoot(item.Clip, remainder) * inverse * Power(end, cycles);
        // ANI_AnimPlayer restores the pre-animation position for SpeedFromDyna.
        // The body still advances and rotates; movement belongs to DYN/scripts.
        if (item.UsesDynamicsTranslation) motion.Translation = Vector3.Zero;
        return motion;
    }

    private static Matrix4x4 Power(Matrix4x4 value, int exponent)
    {
        var result = Matrix4x4.Identity;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) result = value * result;
            value *= value;
            exponent >>= 1;
        }
        return result;
    }
}
