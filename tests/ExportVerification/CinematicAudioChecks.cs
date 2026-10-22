using JadeView;

internal static class CinematicAudioChecks
{
    internal static void Verify()
    {
        var modifier = new byte[132];
        Put(0, 273); Put(4, 0x800); Put(8, 0); Put(12, 77); Put(120, 2);
        BitConverter.GetBytes(1f).CopyTo(modifier, 16);
        var owner = new SceneObject { GaoKey = 44, Modifiers = [new(8, modifier)] };
        var world = new WorldData { Key = 99 };
        world.Objects.Add(owner);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicSoundBanks.Add(77, [0, uint.MaxValue, 55]);
        var scriptOwner = new CinematicScriptOwner(44, 0, 0, world.Key, uint.MaxValue,
            uint.MaxValue, "audio owner", [], [],
            [new("des_i_SndMdf_Musique", PcVariableFormat.IntegerType, "instance", "0"),
             new("des_i_SndMdf_Ambiance", PcVariableFormat.IntegerType, "model", "-1"),
             new("des_i_SndMdf_Voix", PcVariableFormat.IntegerType, "model", "-10")]);
        var bindings = CinematicScriptAudioBindingResolver.Resolve(scriptOwner, document);
        Require(bindings.Length == 3 && bindings[0].Modifiers.Single().SoundKey == 55
            && bindings[0].Status == "resolved"
            && bindings[1].ModifierId == -1 && bindings[1].Modifiers.Single().SoundKey == 55
            && bindings[1].Status.Contains("aggregates all 1 attached type-8 modifiers", StringComparison.Ordinal)
            && bindings[2].Status == "disabled sentinel",
            "Cine_Events OVA audio IDs and negative status selectors were not joined to owner modifiers in the same world");
        var playBinding = CinematicSoundCommandResolver.Resolve(
            new(44, 0x2F1A, [0]), world.Key, document, [scriptOwner]);
        Require(playBinding is { Status: "resolved target modifier" }
            && playBinding.SoundKeys.SequenceEqual(["00000037"])
            && playBinding.MatchingOvaSlots.SequenceEqual(["music"]),
            "SNDPlayModifier did not link its target, modifier ID, decoded sound key, and matching OVA slot");
        var playAllBinding = CinematicSoundCommandResolver.Resolve(
            new(44, 0x2F1A, [uint.MaxValue]), world.Key, document, [scriptOwner]);
        Require(playAllBinding is { ModifierId: -1 }
            && playAllBinding.SoundKeys.SequenceEqual(["00000037"]),
            "SNDPlayModifier(-1) did not enumerate the target's attached sound modifiers");
        var ignoredBinding = CinematicSoundCommandResolver.Resolve(
            new(44, 0x2F1A, [unchecked((uint)-10)]), world.Key, document, [scriptOwner]);
        Require(ignoredBinding is { Status: "ignored -10 sentinel", SoundKeys.Length: 0 },
            "SNDPlayModifier(-10) should remain an explicit no-op");
        owner.Modifiers.Add(new(8, (byte[])modifier.Clone()));
        var ambiguousBindings = CinematicScriptAudioBindingResolver.Resolve(scriptOwner, document);
        Require(ambiguousBindings[0].Modifiers.Length == 0 && ambiguousBindings[0].Status.StartsWith("ambiguous:",
            StringComparison.Ordinal), "Ambiguous owner modifier IDs must not choose an arbitrary sound asset");
        var ambiguousCommandBinding = CinematicSoundCommandResolver.Resolve(
            new(44, 0x2F1A, [0]), world.Key, document, [scriptOwner]);
        Require(ambiguousCommandBinding is { SoundKeys.Length: 0 }
            && ambiguousCommandBinding.Status.StartsWith("ambiguous:", StringComparison.Ordinal),
            "A sound command with duplicate target modifier IDs must not resolve an arbitrary audio key");
        owner.Modifiers.RemoveAt(1);
        var source = new AnimationClip
        {
            Duration = 2,
            CinematicOwnerKey = 44,
            Tracks = [new() { Bone = -1, Target = 0, Keys =
            [
                CinematicTestEvents.Call(.5f, 0, 12058, 0),
                CinematicTestEvents.Call(1.5f, 0, 12060, 0)
            ] }]
        };
        var cues = CinematicAudioTimeline.Build(source, world, document);
        Require(cues.Length == 1 && cues[0].SoundKey == 55 && cues[0].Start == .5f && cues[0].End == 1.5f,
            "Owner context, preserved bank slot or start/stop timing was lost");
        var cueChoices = new[] { cues[0], cues[0] with { SoundKey = 56, Start = .75f } };
        var selectedCues = CinematicAudioCueSelection.Select(cueChoices, new HashSet<int> { 1 });
        Require(selectedCues.Length == 1 && selectedCues[0].SoundKey == 56,
            "The cinematic audio cue selector did not isolate the selected source cue.");

        var localizedCue = new CinematicAudioCue(0, 10, 20, 0, 100, 1, false);
        Entry[] localizedEntries =
        [
            new(0, 100, 0, 0, "014_apres_m-disk_FR.wad", "Localisations/France/Cines"),
            new(1, 101, 0, 0, "014_apres_m-disk_DE.wad", "Localisations/Germany/Cines"),
            new(2, 102, 0, 0, "014_apres_m-disk_US.wad", "Localisations/US/Cines"),
            new(3, 103, 0, 0, "079_apres_flammes_FR.wad", "Localisations/France/Cines")
        ];
        var localizedCues = CinematicAudioLocalization.Expand([localizedCue], localizedEntries);
        Require(localizedCues.Length == 3 && localizedCues[0].SoundKey == 100
            && localizedCues.Select(cue => cue.ChoiceGroup).Distinct().Count() == 1
            && localizedCues.Select(cue => cue.ChoiceIndex).SequenceEqual([0, 1, 2]),
            "Localized audio WAD variants were not grouped with the current language first.");
        var selectedLocales = new HashSet<int> { 0 };
        CinematicAudioCueSelection.SetSelected(localizedCues, selectedLocales, 2, true);
        Require(selectedLocales.SetEquals([2]),
            "Selecting a localized audio bank did not replace the other language variant.");

        var voiceSpeaker = new SceneObject { GaoKey = 70, Name = "Speaker", SoundBankKey = 500 };
        world.Objects.Add(voiceSpeaker);
        document.CinematicSoundBanks[500] = [101, 102, 103];
        var dialogueReceiver = new CinematicScriptOwner(80, 0, 0, world.Key, uint.MaxValue,
            uint.MaxValue, "Dialogue receiver", [], [], [],
            [new(3, 0, 0, SoundId: 1, SoundVariantMax: 1, Volume: 0),
             new(4, 0, 0, SoundId: 0)]);
        document.CinematicScriptOwners.Add(dialogueReceiver.Id, dialogueReceiver);
        var dialogueMessage = new CinematicTimelineMessage(0.75f, dialogueReceiver.OwnerKey,
            voiceSpeaker.GaoKey, 0, default, default, 0, 0, 3, 0, 30);
        var dialogueCues = CinematicAudioTimeline.BuildDialogueCues(4,
            [(dialogueMessage.Time, dialogueMessage)], world, document);
        Require(dialogueCues.Cues.Length == 0 && dialogueCues.Diagnostics.Length == 1
            && dialogueCues.Diagnostics[0].Contains("global sound ID 1", StringComparison.Ordinal),
            "Dialogue audio should not be guessed from a character-local sound bank.");
        var zeroSlotCue = CinematicAudioTimeline.BuildDialogueCues(4,
            [(1.25f, dialogueMessage with { Time = 1.25f, Int1 = 4 })], world, document);
        Require(zeroSlotCue.Cues.Length == 0 && zeroSlotCue.Diagnostics.Length == 0,
            "Global sound ID zero should defer to the source cinematic audio bank.");

        var audio = AudioAsset.FromSamples(Enumerable.Repeat((short)1000, 24000).ToArray(), 1, 24000);
        var mix = CinematicAudioMixer.Mix(cues, 2, key => key == 55 ? audio : null);
        Require(Sample(.25f) == 0 && Sample(.75f) == 1000 && Sample(1.75f) == 0,
            "Mixed audio did not honor source cue boundaries");
        Require(mix.Audio.Samples[36000 * 2] == mix.Audio.Samples[36000 * 2 + 1], "Mono source was not mapped to stereo");
        var overlapping = CinematicAudioMixer.Mix([cues[0], cues[0] with { Volume = .5f }], 2, _ => audio);
        Require(overlapping.Audio.Samples[36000 * 2] == 1500, "Simultaneous audio cues were not mixed");
        var ramp = AudioAsset.FromSamples([0, 1000, 2000, 3000], 1, 24000);
        var resampled = CinematicAudioMixer.Mix([cues[0]], 2, _ => ramp);
        Require(resampled.Audio.Samples[24001 * 2] == 500, "PCM sample-rate conversion lost interpolation");
        Console.WriteLine("PASS: source sound-bank slots, owner context, timed audio start/stop, mixing and PCM resampling");

        short Sample(float time) => mix.Audio.Samples[(int)(time * CinematicAudioMixer.SampleRate) * 2];
        void Put(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(modifier, offset);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
