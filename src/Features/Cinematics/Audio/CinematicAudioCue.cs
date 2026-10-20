namespace JadeView;

public sealed record CinematicAudioCue(float Start, float End, uint OwnerKey, int ModifierId,
    uint SoundKey, float Volume, bool Loop);

internal sealed record CinematicSoundModifier(int Id, uint SoundKey, float Volume, float Delay, bool Loop)
{
    internal static CinematicSoundModifier? Read(SceneModifier modifier, SceneDocument document)
    {
        const int SerializedSize = 132;
        byte[] bytes = modifier.Data;
        if (modifier.Type != 8 || bytes.Length != SerializedSize || Preview.U32(bytes, 0) != 273) return null;
        uint key = Preview.U32(bytes, 12);
        uint flags = Preview.U32(bytes, 4);
        if ((flags & 0x800) != 0)
        {
            int slot = Preview.I32(bytes, 120);
            if (!document.CinematicSoundBanks.TryGetValue(key, out var sounds)
                || slot < 0 || slot >= sounds.Length) return null;
            key = sounds[slot];
        }
        if (key is 0 or uint.MaxValue) return null;
        float volume = BitConverter.ToSingle(bytes, 16), delay = BitConverter.ToSingle(bytes, 128);
        if (!float.IsFinite(volume) || !float.IsFinite(delay) || delay < 0) return null;
        return new(Preview.I32(bytes, 8), key, Math.Clamp(volume, 0, 1), delay,
            (Preview.U32(bytes, 56) & 4) != 0);
    }
}
