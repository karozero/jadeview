using JadeView;

internal static class CinematicAudioChecks
{
    internal static void Verify()
    {
        short[] stereoSamples = [10, 11, 20, 21, 30, 31];
        short[] audioBuffer = new short[8];
        int copiedFrames = CinematicAudioPreview.CopyAudioFrames(audioBuffer, stereoSamples, 1);
        Require(copiedFrames == 2 && audioBuffer.SequenceEqual<short>([20, 21, 30, 31, 0, 0, 0, 0]),
            "The audio queue should copy available stereo frames and silence-pad after end of source.");
        Require(Math.Abs(CinematicAudioPreview.MixerClockTime(3.25, 60000, 48000) - 4.5) < .0001,
            "The independent audio mixer frame counter should map to the source timeline from stream start.");
        float clockAheadDelta = CinematicAudioPlaybackClock.Delta(4.4, 4.5, true, .016f);
        float clockBehindDelta = CinematicAudioPlaybackClock.Delta(4.4, 4.3, true, .016f);
        float clockAlignedDelta = CinematicAudioPlaybackClock.Delta(4.4, 4.4, true, .016f);
        float clockJumpDelta = CinematicAudioPlaybackClock.Delta(4.4, 5.4, true, .016f);
        Require(clockAheadDelta is > .016f and <= .0169f
            && clockBehindDelta is >= .0151f and < .016f
            && Math.Abs(clockAlignedDelta - .016f) < .0001f
            && clockJumpDelta is > .016f and <= .0169f
            && Math.Abs(CinematicAudioPlaybackClock.Delta(4.4, 0, false, .016f) - .016f) < .0001f,
            "Audio-clock corrections must stay smooth and bounded, preserving frame cadence through packetized clock updates.");
        var wave = AudioAsset.FromSamples(stereoSamples, 2, 48000);
        Require(wave.Wav.Length == 56 && wave.Wav.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && BitConverter.ToInt32(wave.Wav, 40) == stereoSamples.Length * sizeof(short),
            "PCM audit export must produce a valid WAV header and preserve all samples.");
        Require(!CinematicAudioPreview.IsQueueOutOfSync(.22, 0, .5, .17)
            && CinematicAudioPreview.IsQueueOutOfSync(1.1, 0, .5, .17)
            && CinematicAudioPreview.IsQueueOutOfSync(0, .6, .5, .17),
            "PCM queue synchronization should tolerate its expected lead and only restart after a large seek or underrun.");
        double queuedAudioLead = CinematicAudioPreview.AudioBufferFrames * 2d / CinematicAudioMixer.SampleRate;
        Require(queuedAudioLead >= .66 && queuedAudioLead < .69,
            "The playback queue should buffer roughly two thirds of a second to absorb longer render-frame spikes.");
        VerifyStereoMsAdpcmDecode();
        VerifySoundFadeEnvelope();
        VerifyWaveLoopRegions();
        VerifyPossibleClickDiagnostics();

        var modifier = new byte[132];
        Put(0, 273); Put(4, 0x800); Put(8, 0); Put(12, 77); Put(72, 91); Put(120, 2);
        BitConverter.GetBytes(1f).CopyTo(modifier, 16);
        var owner = new SceneObject { GaoKey = 44, SoundBankKey = 77, Modifiers = [new(8, modifier)] };
        var modifierOnlyBankBytes = new byte[132];
        PutTo(modifierOnlyBankBytes, 0, 273);
        PutTo(modifierOnlyBankBytes, 4, 0x800);
        PutTo(modifierOnlyBankBytes, 8, 0);
        PutTo(modifierOnlyBankBytes, 12, 80);
        PutTo(modifierOnlyBankBytes, 72, 91);
        PutTo(modifierOnlyBankBytes, 120, 2);
        BitConverter.GetBytes(1f).CopyTo(modifierOnlyBankBytes, 16);
        var modifierOnlyOwner = new SceneObject
        {
            GaoKey = 48,
            SoundBankKey = uint.MaxValue,
            Modifiers = [new(8, modifierOnlyBankBytes)]
        };
        var languageBankModifierBytes = new byte[132];
        PutTo(languageBankModifierBytes, 0, 273);
        PutTo(languageBankModifierBytes, 4, 0x800);
        PutTo(languageBankModifierBytes, 8, 0);
        PutTo(languageBankModifierBytes, 12, 81);
        PutTo(languageBankModifierBytes, 120, 0);
        BitConverter.GetBytes(1f).CopyTo(languageBankModifierBytes, 16);
        var languageBankOwner = new SceneObject
        {
            GaoKey = 49,
            SoundBankKey = uint.MaxValue,
            Modifiers = [new(8, languageBankModifierBytes)]
        };
        var world = new WorldData { Key = 99 };
        world.Objects.Add(owner);
        world.Objects.Add(modifierOnlyOwner);
        world.Objects.Add(languageBankOwner);
        world.Objects.Add(new SceneObject { GaoKey = 46, SoundBankKey = 78 });
        world.Objects.Add(new SceneObject { GaoKey = 47, SoundBankKey = 79 });
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicSoundBanks.Add(77, [0, uint.MaxValue, 55]);
        var soundBankRecord = new PcSceneLoader.Record(78, 'u', 8, 24);
        var modifierOnlyBankRecord = new PcSceneLoader.Record(80, 'u', 8, 24);
        var languageMetaBankRecord = new PcSceneLoader.Record(81, 'u', 8, 16);
        byte[] soundBankBytes = new byte[32];
        ".smd"u8.CopyTo(soundBankBytes);
        BitConverter.GetBytes(24).CopyTo(soundBankBytes, 4);
        BitConverter.GetBytes(101u).CopyTo(soundBankBytes, 8);
        BitConverter.GetBytes(0x6461772Eu).CopyTo(soundBankBytes, 12);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(soundBankBytes, 16);
        BitConverter.GetBytes(0x6461772Eu).CopyTo(soundBankBytes, 20);
        BitConverter.GetBytes(55u).CopyTo(soundBankBytes, 24);
        BitConverter.GetBytes(0x6461772Eu).CopyTo(soundBankBytes, 28);
        byte[] languageMetaBankBytes = new byte[24];
        ".snk"u8.CopyTo(languageMetaBankBytes);
        PutTo(languageMetaBankBytes, 4, 0);
        PutTo(languageMetaBankBytes, 8, 12);
        PutTo(languageMetaBankBytes, 12, 1);
        PutTo(languageMetaBankBytes, 16, 55);
        PutTo(languageMetaBankBytes, 20, 0x6461772E);
        CinematicSoundBankReader.Load([soundBankRecord, modifierOnlyBankRecord, languageMetaBankRecord], new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [soundBankRecord] = soundBankBytes,
            [modifierOnlyBankRecord] = soundBankBytes,
            [languageMetaBankRecord] = languageMetaBankBytes
        }, document);
        Require(document.CinematicSoundBanks.TryGetValue(78, out uint[]? decodedSoundBank)
            && decodedSoundBank.SequenceEqual([101u, uint.MaxValue, 55u]),
            "A target GAO sound bank must decode raw 8-byte key/type entries.");
        Require(document.CinematicSoundBanks.TryGetValue(80, out uint[]? modifierOnlySoundBank)
            && modifierOnlySoundBank.SequenceEqual([101u, uint.MaxValue, 55u])
            && CinematicSoundModifier.Read(modifierOnlyOwner.Modifiers[0], document) is
                { SoundKey: 55 },
            "A modifier-only bank must retain its authored sound key for playback.");
        Require(document.CinematicSoundBanks.TryGetValue(81, out uint[]? languageChildKeys)
            && languageChildKeys.SequenceEqual([55u])
            && CinematicSoundModifier.Read(languageBankOwner.Modifiers[0], document)?.SoundKey == 55,
            "A referenced .snk metabank must retain its language child keys in source order for cue resolution.");
        var malformedBankRecord = new PcSceneLoader.Record(79, 'u', 8, 8);
        byte[] malformedBankBytes = new byte[16];
        ".smd"u8.CopyTo(malformedBankBytes);
        BitConverter.GetBytes(8).CopyTo(malformedBankBytes, 4);
        BitConverter.GetBytes(1u).CopyTo(malformedBankBytes, 8);
        BitConverter.GetBytes(0x9E005E9Cu).CopyTo(malformedBankBytes, 12);
        CinematicSoundBankReader.Load([malformedBankRecord], new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [malformedBankRecord] = malformedBankBytes
        }, document);
        Require(!document.CinematicSoundBanks.ContainsKey(79)
            && document.Diagnostics.Any(message => message.Contains("unsupported group type 9E005E9C",
                StringComparison.Ordinal)),
            "An .smd payload with non-sound group bytes must not be accepted as a sound-slot bank.");
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
        var modifierOnlyPlayBinding = CinematicSoundCommandResolver.Resolve(
            new(48, 0x2F1A, [0]), world.Key, document);
        Require(modifierOnlyPlayBinding is { Status: "resolved target modifier", SoundKeys.Length: 1 }
            && modifierOnlyPlayBinding.SoundKeys[0] == "00000037",
            "A sound modifier's bank-only resource reference must resolve through SNDPlayModifier.");
        var languageBankClip = new AnimationClip
        {
            Duration = 3,
            CinematicOwnerKey = languageBankOwner.GaoKey,
            Tracks = [new() { Bone = -1, Keys =
                [CinematicTestEvents.Call(.25f, 0, 12058, 0)] }]
        };
        var languageBankCues = CinematicAudioTimeline.Build(languageBankClip, world, document,
            out var languageBankDiagnostics);
        Require(languageBankCues is [{ Start: .25f, SoundKey: 55 }]
            && languageBankDiagnostics.Length == 0,
            "SNDPlayModifier on a localized .snk-backed modifier must produce its source-time audio cue.");
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

        Entry[] audioEntries = [new(0, 55, 0, 0, "sound.wad", "sounds")];
        var localSoundRequest = CinematicSoundCommandResolver.Resolve(
            new(44, 0x2EF6, [2, BitConverter.SingleToUInt32Bits(-1)]), world.Key, document,
            archiveEntries: audioEntries);
        Require(localSoundRequest is { ModifierId: 2, Status: "resolved local sound-bank slot", SoundKeys.Length: 1 }
            && localSoundRequest.SoundKeys[0] == "00000037",
            "SND_RequestPlay must resolve a local index through its target GAO's sound bank.");
        var missingArchiveSound = CinematicSoundCommandResolver.Resolve(
            new(44, 0x2EF6, [2, BitConverter.SingleToUInt32Bits(-1)]), world.Key, document,
            archiveEntries: []);
        Require(missingArchiveSound is { SoundKeys.Length: 0 }
            && missingArchiveSound.Status.Contains("missing archive resource 00000037", StringComparison.Ordinal),
            "A bank slot that is not present in the opened archive must not become a playable cue.");
        var randomFrequencyRequest = CinematicSoundCommandResolver.Resolve(new(44, 0x2F47,
            [2, BitConverter.SingleToUInt32Bits(.75f), BitConverter.SingleToUInt32Bits(.9f),
             BitConverter.SingleToUInt32Bits(1.1f)]), world.Key, document, archiveEntries: audioEntries);
        Require(randomFrequencyRequest is
            { Status: "resolved local sound-bank slot; random frequency range is not reconstructed", SoundKeys.Length: 1 }
            && randomFrequencyRequest.SoundKeys[0] == "00000037",
            "SND_RequestPlayRandomFreq must resolve its bank slot while preserving the pitch limitation.");
        var eventWorld = new WorldData { Key = 100 };
        eventWorld.Objects.Add(new SceneObject { GaoKey = 45, SoundBankKey = 77 });
        document.Worlds.Add(eventWorld);
        var crossWorldRequest = CinematicSoundCommandResolver.Resolve(
            new(45, 0x2EF6, [2, BitConverter.SingleToUInt32Bits(-1)]), world.Key, document,
            archiveEntries: audioEntries);
        Require(crossWorldRequest is
            { Status: "resolved local sound-bank slot via matching GAO in another loaded world", SoundKeys.Length: 1 }
            && crossWorldRequest.SoundKeys[0] == "00000037",
            "A linked EVE track must resolve the same uniquely loaded GAO when its owner world differs.");
        var localSoundClip = new AnimationClip
        {
            Duration = 3,
            CinematicOwnerKey = 44,
            Tracks = [new() { Bone = -1, Keys =
                [CinematicTestEvents.Call(.75f, 0, 12022, 2, BitConverter.SingleToUInt32Bits(-1))] }]
        };
        var localSoundCues = CinematicAudioTimeline.Build(localSoundClip, world, document, out var localSoundDiagnostics);
        Require(localSoundCues.Length == 1 && localSoundCues[0] is
            { Start: .75f, End: 3, OwnerKey: 44, ModifierId: 2, SoundKey: 55, Volume: 1, Loop: false }
            && localSoundCues[0].Label?.Contains("sound-bank slot 2", StringComparison.Ordinal) == true
            && localSoundDiagnostics.Length == 0,
            "Timed SND_RequestPlay should become a selectable one-shot cue with source default volume.");
        var nestedSoundClip = new AnimationClip { Duration = 3 };
        var nestedSoundCues = CinematicAudioTimeline.Build(nestedSoundClip, world, document,
            audioEntries, out var nestedSoundDiagnostics,
            [(.75f, new(44, 12022, [2, BitConverter.SingleToUInt32Bits(-1)])),
             (1.5f, new(44, 12060, [2]))]);
        Require(nestedSoundCues is [{ Start: .75f, End: 1.5f, StopTime: 1.5f,
                OwnerKey: 44, ModifierId: 2, SoundKey: 55 }]
            && nestedSoundDiagnostics.Length == 0,
            "Nested SceneEvents sound requests and stops should join the cinematic clock and resolve the same owner bank slot.");
        var randomSoundClip = new AnimationClip
        {
            Duration = 3,
            CinematicOwnerKey = 44,
            Tracks = [new() { Bone = -1, Keys =
                [CinematicTestEvents.Call(.5f, 0, 12103, 2, BitConverter.SingleToUInt32Bits(.75f),
                    BitConverter.SingleToUInt32Bits(.9f), BitConverter.SingleToUInt32Bits(1.1f))] }]
        };
        var randomSoundCues = CinematicAudioTimeline.Build(randomSoundClip, world, document,
            out var randomSoundDiagnostics);
        Require(randomSoundCues.Length == 1 && randomSoundCues[0].Volume == .75f
            && randomSoundCues[0].Label?.Contains("random pitch not reconstructed", StringComparison.Ordinal) == true
            && randomSoundDiagnostics.Length == 0,
            "Random-frequency sound requests should remain audible at nominal pitch with an explicit limitation label.");
        var invalidSoundClip = new AnimationClip
        {
            Duration = 3,
            CinematicOwnerKey = 44,
            Tracks = [new() { Bone = -1, Keys =
                [CinematicTestEvents.Call(.5f, 0, 12022, 9, BitConverter.SingleToUInt32Bits(-1))] }]
        };
        var invalidSoundCues = CinematicAudioTimeline.Build(invalidSoundClip, world, document, out var invalidSoundDiagnostics);
        Require(invalidSoundCues.Length == 0 && invalidSoundDiagnostics.Length == 1
            && invalidSoundDiagnostics[0].Contains("outside the decoded bank", StringComparison.Ordinal),
            "An out-of-range local sound-bank slot must report a diagnostic instead of selecting another asset.");

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
        Require(cues.Length == 1 && cues[0].SoundKey == 55 && cues[0].Start == .5f
            && cues[0].End == 1.5f && cues[0].StopTime == 1.5f,
            "Owner context, preserved bank slot or stop timing was lost");
        var cueChoices = new[] { cues[0], cues[0] with { SoundKey = 56, Start = .75f } };
        var selectedCues = CinematicAudioCueSelection.Select(cueChoices, new HashSet<int> { 1 });
        Require(selectedCues.Length == 1 && selectedCues[0].SoundKey == 56,
            "The cinematic audio cue selector did not isolate the selected source cue.");
        var delayedIntroCue = new CinematicAudioCue(1.2f, 10, 20, 0, 100, 1, false);
        string formattedCueTime = 1.2f.ToString("F2", System.Globalization.CultureInfo.CurrentCulture);
        Require(CinematicAudioPlaybackStatus.Describe([delayedIntroCue], 1, 0, true, true)
                .Contains($"silence until next cue in {formattedCueTime}s", StringComparison.Ordinal)
            && CinematicAudioPlaybackStatus.Describe([delayedIntroCue], 1, 2, true, true)
                .Contains("1 timeline cue(s) active", StringComparison.Ordinal)
            && CinematicAudioPlaybackStatus.Describe([delayedIntroCue], 1, 0, true, false)
                .Contains($"next cue at {formattedCueTime}s", StringComparison.Ordinal)
            && CinematicAudioPlaybackStatus.Describe([delayedIntroCue], 1, 0, false, true)
                .Contains("Source audio disabled", StringComparison.Ordinal),
            "Cinematic audio status must distinguish intentional pre-cue silence, active timeline audio and disabled/paused states.");

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
        var defaultLocalizedSelection = CinematicAudioCueSelection.SelectDefault(localizedCues);
        Require(defaultLocalizedSelection.Length == 1 && defaultLocalizedSelection[0].SoundKey == 100,
            "Default playback and realtime diagnostics should select one localized WAD, not mix every language.");
        var selectedLocales = new HashSet<int> { 0 };
        CinematicAudioCueSelection.SetSelected(localizedCues, selectedLocales, 2, true);
        Require(selectedLocales.SetEquals([2]),
            "Selecting a localized audio bank did not replace the other language variant.");

        var voiceSpeaker = new SceneObject { GaoKey = 70, Name = "Speaker", SoundBankKey = 500 };
        world.Objects.Add(voiceSpeaker);
        document.CinematicSoundBanks[500] = [101, 102, 103];
        var dialogueReceiver = new CinematicScriptOwner(80, 0, 0, world.Key, uint.MaxValue,
            uint.MaxValue, "Dialogue receiver", [], [], [],
            [new(3, 0x12345678, 3, SoundId: 1, SoundVariantMax: 1, Volume: 0),
             new(4, 0x12345678, 4, SoundId: 0)]);
        document.CinematicScriptOwners.Add(dialogueReceiver.Id, dialogueReceiver);
        var dialogueMessage = new CinematicTimelineMessage(0.75f, dialogueReceiver.OwnerKey,
            voiceSpeaker.GaoKey, 0, default, default, 0, 0, 3, 0, 30);
        var dialogueCues = CinematicAudioTimeline.BuildDialogueCues(4,
            [(dialogueMessage.Time, dialogueMessage)], world, document);
        Require(dialogueCues.Cues.Length == 0 && dialogueCues.Diagnostics.Length == 1
            && dialogueCues.Diagnostics[0].Contains("des_ai_son_associe=1", StringComparison.Ordinal)
            && dialogueCues.Diagnostics[0].Contains("not a resolved WAVE key", StringComparison.Ordinal),
            "Dialogue OVA metadata must not be mislabeled as a WAVE key or guessed from a character-local bank.");
        var zeroSlotCue = CinematicAudioTimeline.BuildDialogueCues(4,
            [(1.25f, dialogueMessage with { Time = 1.25f, Int1 = 4 })], world, document);
        Require(zeroSlotCue.Cues.Length == 0 && zeroSlotCue.Diagnostics.Length == 1
            && zeroSlotCue.Diagnostics[0].Contains("des_ai_son_associe=0", StringComparison.Ordinal),
            "A zero OVA associated-sound value must not be treated as proof that text has no voice.");

        var audio = AudioAsset.FromSamples(Enumerable.Repeat((short)1000, 24000).ToArray(), 1, 24000);
        var mix = CinematicAudioMixer.Mix(cues, 2, key => key == 55 ? audio : null);
        Require(mix.Audio.Wav.Length == 0,
            "The cinematic PCM preview should not allocate a redundant RIFF copy of its mixed samples.");
        Require(Sample(.25f) == 0 && Sample(.75f) == 1000 && Sample(1.75f) == 0,
            "Mixed audio did not honor source cue boundaries");
        Require(mix.Audio.Samples[36000 * 2] == mix.Audio.Samples[36000 * 2 + 1], "Mono source was not mapped to stereo");
        var overlapping = CinematicAudioMixer.Mix([cues[0], cues[0] with { Volume = .5f }], 2, _ => audio);
        Require(overlapping.Audio.Samples[36000 * 2] == 1500, "Simultaneous audio cues were not mixed");
        var rampSamples = Enumerable.Range(0, 1_000).Select(sample => (short)(sample * 10)).ToArray();
        var ramp = AudioAsset.FromSamples(rampSamples, 1, 24000);
        var resampled = CinematicAudioMixer.Mix([cues[0]], 2, _ => ramp);
        Require(resampled.Audio.Samples[(24_000 + 241) * 2] == 1_205,
            "PCM sample-rate conversion lost interpolation after the short edge ramp");
        var edgeCue = new CinematicAudioCue(.1f, .2f, 44, 2, 55, 1, false);
        var edgeMix = CinematicAudioMixer.Mix([edgeCue], .3f, _ => audio);
        short EdgeSample(int frame) => edgeMix.Audio.Samples[frame * 2];
        Require(Math.Abs(EdgeSample(4_800)) < 10 && EdgeSample(4_920) is > 450 and < 550
            && Math.Abs(EdgeSample(9_599)) < 10 && EdgeSample(9_600) == 0,
            "Audio cue boundaries should use a short de-click ramp instead of abrupt waveform cuts.");
        Console.WriteLine("PASS: source sound-bank slots, owner context, timed audio start/stop, mixing, resampling and de-click edges");

        short Sample(float time) => mix.Audio.Samples[(int)(time * CinematicAudioMixer.SampleRate) * 2];
        void Put(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(modifier, offset);
    }

    private static void VerifyStereoMsAdpcmDecode()
    {
        AudioAsset decoded = Preview.DecodeWave(BuildStereoMsAdpcmWave());
        Require(decoded.Rate == 32_000 && decoded.Channels == 2 && decoded.Samples.Length == 240,
            "Stereo MS ADPCM decoding must preserve its declared rate, channel count and 60-frame blocks.");
        Require(decoded.Samples.AsSpan(0, 8).SequenceEqual<short>(
                [500, -500, 1000, -1000, 1100, -1100, 1278, -1100])
            && decoded.Samples[120] == 500 && decoded.Samples[121] == -500,
            "Stereo MS ADPCM samples must stay paired by frame, use high/low nibbles per channel and restart at block boundaries.");
        Console.WriteLine("PASS: stereo PC MS ADPCM channel pairing, nibble order, predictors and block boundaries");
    }

    private static void VerifyPossibleClickDiagnostics()
    {
        short[] sourceSamples = new short[9_600];
        Array.Fill(sourceSamples, (short)-15_000, 0, 4_800);
        Array.Fill(sourceSamples, (short)15_000, 4_800, 4_800);
        var source = AudioAsset.FromSamples(sourceSamples, 1, CinematicAudioMixer.SampleRate);
        var diagnostics = new List<string>();
        _ = CinematicAudioMixer.Mix(
            [new(0, .2f, 1, 0, 0, 1, false)], .2f, _ => source, diagnostics);

        Require(diagnostics.Count == 1
            && diagnostics[0].Contains("Possible click-like PCM jump at 0.100s", StringComparison.Ordinal)
            && diagnostics[0].Contains("source audio is left unchanged", StringComparison.Ordinal),
            "The audio mixer should report a large sample discontinuity without modifying the authored signal.");

        var smoothSource = AudioAsset.FromSamples(Enumerable.Repeat((short)1_000, 9_600).ToArray(),
            1, CinematicAudioMixer.SampleRate);
        var smoothDiagnostics = new List<string>();
        _ = CinematicAudioMixer.Mix(
            [new(0, .2f, 1, 0, 0, 1, false)], .2f, _ => smoothSource, smoothDiagnostics);
        Require(smoothDiagnostics.Count == 0,
            "Steady source audio should not receive a click-like discontinuity diagnostic.");
        Console.WriteLine("PASS: click-like mixed PCM diagnostics identify source jumps without altering audio");
    }

    private static byte[] BuildStereoMsAdpcmWave()
    {
        using var waveStream = new MemoryStream();
        using (var writer = new BinaryWriter(waveStream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(0u);
            writer.Write("WAVE"u8);
            WriteWaveChunk(writer, "fmt "u8, formatWriter =>
            {
                formatWriter.Write((ushort)2);
                formatWriter.Write((ushort)2);
                formatWriter.Write(32_000);
                formatWriter.Write(38_400);
                formatWriter.Write((ushort)72);
                formatWriter.Write((ushort)4);
                formatWriter.Write((ushort)32);
                formatWriter.Write((ushort)60);
                formatWriter.Write((ushort)7);
                int[] coefficientA = [256, 512, 0, 192, 240, 460, 392];
                int[] coefficientB = [0, -256, 0, 64, 0, -208, -232];
                for (int index = 0; index < coefficientA.Length; index++)
                {
                    formatWriter.Write((short)coefficientA[index]);
                    formatWriter.Write((short)coefficientB[index]);
                }
            });
            WriteWaveChunk(writer, "data"u8, dataWriter =>
            {
                for (int blockIndex = 0; blockIndex < 2; blockIndex++)
                {
                    byte[] block = new byte[72];
                    block[0] = 0;
                    block[1] = 0;
                    BitConverter.GetBytes((ushort)100).CopyTo(block, 2);
                    BitConverter.GetBytes((ushort)100).CopyTo(block, 4);
                    BitConverter.GetBytes((short)1000).CopyTo(block, 6);
                    BitConverter.GetBytes((short)-1000).CopyTo(block, 8);
                    BitConverter.GetBytes((short)500).CopyTo(block, 10);
                    BitConverter.GetBytes((short)-500).CopyTo(block, 12);
                    block[14] = 0x1F;
                    block[15] = 0x20;
                    dataWriter.Write(block);
                }
            });
        }
        byte[] wave = waveStream.ToArray();
        BitConverter.GetBytes(checked((uint)wave.Length - 8)).CopyTo(wave, 4);
        return wave;
    }

    private static void WriteWaveChunk(BinaryWriter writer, ReadOnlySpan<byte> identifier,
        Action<BinaryWriter> writeContent)
    {
        using var contentStream = new MemoryStream();
        using (var contentWriter = new BinaryWriter(contentStream,
            System.Text.Encoding.ASCII, leaveOpen: true)) writeContent(contentWriter);
        writer.Write(identifier);
        writer.Write(checked((uint)contentStream.Length));
        writer.Write(contentStream.GetBuffer(), 0, checked((int)contentStream.Length));
        if ((contentStream.Length & 1) != 0) writer.Write((byte)0);
    }

    private static void VerifySoundFadeEnvelope()
    {
        byte[] fadeBytes = BuildSoundFadeInsert();
        CinematicSoundFadeEnvelope? fade = CinematicSoundFadeEnvelope.Read(fadeBytes);
        Require(fade != null && Math.Abs(fade.Duration - .5f) < .0001f
            && Math.Abs(fade.Evaluate(0) - 1) < .0001f
            && Math.Abs(fade.Evaluate(.25f) - .5f) < .0001f
            && fade.Evaluate(.5f) == 0,
            "A Jade .fad insert should evaluate its time/volume key curve and endpoint values.");

        var source = AudioAsset.FromSamples(Enumerable.Repeat((short)1000, 48_000).ToArray(), 1, 48_000);
        var modifier = CinematicSoundModifierFile.Read(BuildSoundModifierFile(55, 91));
        Require(modifier is { SoundKey: 55, FadeOutKey: 91, HasPlaylist: false },
            "A Jade .smd must expose its sound resource and linked fade resource.");
        var cue = new CinematicAudioCue(0, 1, 44, 2, 90, 1, false, StopTime: .25f);
        CinematicAudioMix mix = CinematicAudioMixer.Mix([cue], 1, _ => source,
            resolveFade: key => key == 91 ? fade : null,
            resolveModifier: key => key == 90 ? modifier : null);
        short At(float seconds) => mix.Audio.Samples[(int)(seconds * CinematicAudioMixer.SampleRate) * 2];
        Require(At(.24f) == 1000 && At(.25f) == 1000 && At(.5f) == 500 && At(.75f) == 0,
            "Stopping a sound modifier should preserve its source volume then apply the linked fade until the endpoint.");

        BitConverter.GetBytes(29u).CopyTo(fadeBytes, 56);
        Require(CinematicSoundFadeEnvelope.Read(fadeBytes) == null,
            "A malformed .fad chunk must be rejected without reading past its declared size.");
        byte[] malformedModifier = BuildSoundModifierFile(55, 91);
        BitConverter.GetBytes(223u).CopyTo(malformedModifier, 12);
        Require(CinematicSoundModifierFile.Read(malformedModifier) == null,
            "A malformed .smd chunk must be rejected without reading past its declared size.");
        Console.WriteLine("PASS: Jade .fad stop envelopes, curve sampling, malformed bounds and mixer fade-out");
    }

    private static byte[] BuildSoundModifierFile(uint soundKey, uint fadeOutKey)
    {
        byte[] bytes = new byte[16 + 224 + 8];
        BitConverter.GetBytes(3u).CopyTo(bytes, 0);
        BitConverter.GetBytes(224u).CopyTo(bytes, 12);
        BitConverter.GetBytes(soundKey).CopyTo(bytes, 16);
        BitConverter.GetBytes(fadeOutKey).CopyTo(bytes, 72);
        BitConverter.GetBytes((ushort)0).CopyTo(bytes, 232);
        BitConverter.GetBytes((ushort)0).CopyTo(bytes, 236);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 240);
        return bytes;
    }

    private static byte[] BuildSoundFadeInsert()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(0x696e7374u);
            writer.Write(1u);
            writer.Write(68u);
            writer.Write(1u);
            writer.Write(1u); writer.Write(16u); writer.Write(0f); writer.Write(.5f);
            writer.Write(3u); writer.Write(20u); writer.Write(100f); writer.Write(0f); writer.Write(0u);
            writer.Write(6u); writer.Write(28u); writer.Write(2u);
            writer.Write(1f); writer.Write(0f);
            writer.Write(0f); writer.Write(0f);
        }
        return stream.ToArray();
    }

    private static void VerifyWaveLoopRegions()
    {
        AudioAsset loopedSource = Preview.DecodeWave(BuildWaveWithLoopRegion(10, 70));
        Require(loopedSource.LoopStartFrame == 10 && loopedSource.LoopEndFrame == 80,
            "WAVE cue/labl/ltxt metadata should recover the Jade loop start and end samples.");

        short[] sourceSamples = Enumerable.Range(0, 1_000).Select(sample => (short)(sample * 100)).ToArray();
        var source = AudioAsset.FromSamples(sourceSamples, 1, 48_000, loopedSource.LoopStartFrame,
            loopedSource.LoopEndFrame);
        float duration = sourceSamples.Length / (float)CinematicAudioMixer.SampleRate;
        var cue = new CinematicAudioCue(0, duration, 1, 0, 2, 1, true);
        CinematicAudioMix mix = CinematicAudioMixer.Mix([cue], duration, _ => source);
        Require(mix.Audio.Samples[359 * 2] == 7_900 && mix.Audio.Samples[360 * 2] == 1_000
            && mix.Audio.Samples[361 * 2] == 1_100,
            "A looping cinematic cue should wrap at the source loop end and continue from its authored start.");

        static byte[] BuildWaveWithLoopRegion(uint loopStartFrame, uint loopLengthFrames)
        {
            using var waveStream = new MemoryStream();
            using var writer = new BinaryWriter(waveStream, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write("RIFF"u8);
            writer.Write(0);
            writer.Write("WAVE"u8);
            WriteChunk(writer, "fmt "u8, chunkWriter =>
            {
                chunkWriter.Write((ushort)1);
                chunkWriter.Write((ushort)1);
                chunkWriter.Write(48_000);
                chunkWriter.Write(96_000);
                chunkWriter.Write((ushort)2);
                chunkWriter.Write((ushort)16);
            });
            WriteChunk(writer, "cue "u8, chunkWriter =>
            {
                chunkWriter.Write(1u);
                chunkWriter.Write(1u);
                chunkWriter.Write(loopStartFrame);
                chunkWriter.Write("data"u8);
                chunkWriter.Write(0u);
                chunkWriter.Write(0u);
                chunkWriter.Write(loopStartFrame);
            });
            using var associatedStream = new MemoryStream();
            using (var associatedWriter = new BinaryWriter(associatedStream,
                System.Text.Encoding.ASCII, leaveOpen: true))
            {
                associatedWriter.Write("adtl"u8);
                WriteChunk(associatedWriter, "labl"u8, chunkWriter =>
                {
                    chunkWriter.Write(1u);
                    chunkWriter.Write("loop\0"u8);
                });
                WriteChunk(associatedWriter, "ltxt"u8, chunkWriter =>
                {
                    chunkWriter.Write(1u);
                    chunkWriter.Write(loopLengthFrames);
                    chunkWriter.Write("mark"u8);
                    for (int fieldIndex = 0; fieldIndex < 4; fieldIndex++)
                        chunkWriter.Write((ushort)0);
                });
            }
            WriteRawChunk(writer, "LIST"u8, associatedStream.ToArray());
            WriteChunk(writer, "data"u8, chunkWriter =>
            {
                for (short sample = 0; sample < 100; sample++)
                    chunkWriter.Write((short)(sample * 100));
            });

            byte[] bytes = waveStream.ToArray();
            BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
            return bytes;

            static void WriteChunk(BinaryWriter target, ReadOnlySpan<byte> identifier,
                Action<BinaryWriter> writeData)
            {
                using var chunkStream = new MemoryStream();
                using (var chunkWriter = new BinaryWriter(chunkStream,
                    System.Text.Encoding.ASCII, leaveOpen: true))
                    writeData(chunkWriter);
                WriteRawChunk(target, identifier, chunkStream.ToArray());
            }

            static void WriteRawChunk(BinaryWriter target, ReadOnlySpan<byte> identifier, byte[] data)
            {
                target.Write(identifier);
                target.Write(data.Length);
                target.Write(data);
                if ((data.Length & 1) != 0) target.Write((byte)0);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void PutTo(byte[] target, int offset, uint value) =>
        BitConverter.GetBytes(value).CopyTo(target, offset);
}
