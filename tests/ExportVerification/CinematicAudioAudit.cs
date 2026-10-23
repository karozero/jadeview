using System.Text.Json;
using JadeView;

internal static class CinematicAudioAudit
{
    private readonly record struct SampleDeltaPeak(double Time, long Delta, int Channel,
        short Previous, short Current);

    internal static void Write(string archivePath, string container, uint clipKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var source = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(world => world.Key == source.CinematicWorldKey));
        var playback = CinematicPlayback.Discover(document, world, new(), archive.Entries)
            .Single(player => player.Key == clipKey);
        var cues = playback.AudioCues;
        var audioDiagnostics = playback.AudioDiagnostics;
        playback.Activate();
        var initiallyHidden = world.Objects.Where(sceneObject => sceneObject.IsControlHidden)
            .Select(sceneObject => sceneObject.Name).ToArray();
        var changes = source.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads.SelectMany(payload =>
                CinematicOwnerContext.ForTrack(source, track).Select(context => (
                    key.Time, Call: CinematicLiteralCall.Decode(payload, track.Target, context))))))
            .Where(item => item.Call?.Selector is 8115 or 8152 or 8153).ToArray();
        var changedKeys = changes.Select(change => change.Call!.Target).ToHashSet();
        var times = changes.Select(change => Math.Min(MathF.BitIncrement(change.Time), source.Duration))
            .Append(0).Distinct().Order().ToArray();
        var stateSamples = new List<object>();
        foreach (float time in times)
        {
            playback.Seek(time);
            stateSamples.Add(new
            {
                Time = time,
                Objects = world.Objects.Where(sceneObject => changedKeys.Contains(sceneObject.GaoKey)).Select(sceneObject => new
                {
                    sceneObject.Name,
                    Key = sceneObject.GaoKey.ToString("X8"),
                    sceneObject.IsControlHidden,
                    sceneObject.ControlFlags,
                    sceneObject.PreviewControlFlags
                }).ToArray(),
                ExcludedActors = world.Actors.Where(actor => actor.Root.CinematicExcluded).Select(actor => actor.Root.Name).ToArray()
            });
        }
        playback.Deactivate();
        var defaultCues = cues.Where(cue => cue.ChoiceGroup == null || cue.ChoiceIndex == 0).ToArray();
        var mix = CinematicAudioMixer.Load(archive, defaultCues, source.Duration, CancellationToken.None);
        if (mix.ResolvedCues == 0 || !mix.Audio.Samples.Any(sample => Math.Abs((int)sample) > 100))
            throw new InvalidDataException("No non-silent source audio was resolved.");
        var bankChecks = cues.Select(cue =>
        {
            var bankMix = CinematicAudioMixer.Load(archive, [cue], source.Duration, CancellationToken.None);
            return new
            {
                cue.SoundKey,
                cue.Label,
                cue.ChoiceIndex,
                cue.ChoiceCount,
                Resolved = bankMix.ResolvedCues == 1,
                NonSilent = bankMix.Audio.Samples.Any(sample => Math.Abs((int)sample) > 100),
                Diagnostics = bankMix.Diagnostics
            };
        }).ToArray();
        var loopSources = cues.Where(cue => cue.Loop).Select(cue => cue.SoundKey).Distinct()
            .Select(soundKey =>
            {
                Entry soundEntry = archive.Entries.First(entry => entry.Key == soundKey);
                AudioAsset audio = Preview.DecodeWave(archive.Read(soundEntry));
                int frameCount = audio.Samples.Length / audio.Channels;
                int lastFrame = Math.Max(0, frameCount - 1);
                int loopSeamDelta = Enumerable.Range(0, audio.Channels)
                    .Select(channel => Math.Abs((int)audio.Samples[lastFrame * audio.Channels + channel]
                        - audio.Samples[channel]))
                    .DefaultIfEmpty(0).Max();
                return new
                {
                    Key = soundKey.ToString("X8"),
                    soundEntry.ExportName,
                    audio.LoopStartFrame,
                    audio.LoopEndFrame,
                    audio.Channels,
                    audio.Rate,
                    audio.Seconds,
                    frameCount,
                    loopSeamDelta
                };
            }).ToArray();
        var dialogueLines = document.CinematicScriptOwners.Values
            .Where(owner => owner.WorldKey == source.CinematicWorldKey)
            .SelectMany(owner => owner.DialogueLines ?? [])
            .Where(line => line.TextBankKey is not (0 or uint.MaxValue))
            .DistinctBy(line => (line.TextBankKey, line.TextId)).ToArray();
        var matchingTextGroups = FindTextGroups(archive, dialogueLines);
        var localizedBankCue = defaultCues.FirstOrDefault(cue =>
            archive.Entries.FirstOrDefault(entry => entry.Key == cue.SoundKey)?.Name
                .EndsWith(".wad", StringComparison.OrdinalIgnoreCase) == true);
        object? waveformComparison = null;
        if (localizedBankCue is { } bankCue)
        {
            Entry? bankEntry = archive.Entries.FirstOrDefault(entry => entry.Key == bankCue.SoundKey);
            if (bankEntry != null)
            {
                byte[] bankBytes = archive.Read(bankEntry);
                AudioAsset bankAudio = Preview.DecodeWave(bankBytes);
                var mixedPeak = FindLargestAdjacentSampleDelta(mix.Audio.Samples,
                    mix.Audio.Channels, mix.Audio.Rate);
                double sourceTime = Math.Max(0, mixedPeak.Time - bankCue.Start);
                var correspondingSourcePeak = FindLargestAdjacentSampleDelta(bankAudio.Samples,
                    bankAudio.Channels, bankAudio.Rate, sourceTime, .05);
                waveformComparison = new
                {
                    Bank = bankEntry.ExportName,
                    WaveStructure = DescribeWaveStructure(bankBytes),
                    AdpcmDeltaHeaders = DescribeAdpcmDeltaHeaders(bankBytes),
                    MixedPeak = mixedPeak,
                    MixedPeakNeighborhood = DescribeSampleNeighborhood(mix.Audio.Samples,
                        mix.Audio.Channels, mix.Audio.Rate, mixedPeak),
                    SourceWindowAroundMixedPeak = correspondingSourcePeak,
                    SourcePeakNeighborhood = DescribeSampleNeighborhood(bankAudio.Samples,
                        bankAudio.Channels, bankAudio.Rate, correspondingSourcePeak),
                    SourcePeak = FindLargestAdjacentSampleDelta(bankAudio.Samples,
                        bankAudio.Channels, bankAudio.Rate)
                };
            }
        }
        if (bankChecks.Any(check => !check.Resolved || !check.NonSilent))
            throw new InvalidDataException("At least one discovered localized audio bank could not be decoded as non-silent audio.");
        string prefix = Path.Combine(output, $"{container}-{clipKey:X8}");
        var renderedWave = AudioAsset.FromSamples(mix.Audio.Samples, mix.Audio.Channels, mix.Audio.Rate);
        File.WriteAllBytes(prefix + "-cinematic-audio.wav", renderedWave.Wav);
        File.WriteAllText(prefix + "-cinematic-audio.json", JsonSerializer.Serialize(new
        {
            Clip = clipKey.ToString("X8"),
            Owner = source.CinematicOwnerKey.ToString("X8"),
            source.Duration,
            mix.ResolvedCues,
            mix.Diagnostics,
            AudioDiagnostics = audioDiagnostics,
            AudioBanks = bankChecks,
            LoopSourceMetadata = loopSources,
            WaveformComparison = waveformComparison,
            SceneSoundBanks = document.CinematicSoundBanks.Select(bank => new
            {
                Key = bank.Key.ToString("X8"),
                Sounds = bank.Value.Select(soundKey => new
                {
                    Key = soundKey.ToString("X8"),
                    Name = archive.Entries.FirstOrDefault(entry => entry.Key == soundKey)?.ExportName
                })
            }),
            SoundBankResources = world.Objects.Where(sceneObject => sceneObject.SoundBankKey is not (0 or uint.MaxValue))
                .Select(sceneObject => sceneObject.SoundBankKey).Distinct()
                .Select(bankKey => new
                {
                    Key = bankKey.ToString("X8"),
                    Decoded = document.CinematicSoundBanks.ContainsKey(bankKey),
                    Entries = archive.Entries.Where(entry => entry.Key == bankKey).Select(entry =>
                    {
                        byte[] bytes = archive.Read(entry);
                        return new
                        {
                            entry.Name,
                            entry.Folder,
                            entry.StoredSize,
                            Prefix = Convert.ToHexString(bytes.AsSpan(0, Math.Min(32, bytes.Length)))
                        };
                    })
                }),
            ObjectSoundBanks = world.Objects.Where(sceneObject => sceneObject.SoundBankKey != uint.MaxValue)
                .Select(sceneObject => new
                {
                    Object = sceneObject.Name,
                    ObjectKey = sceneObject.GaoKey.ToString("X8"),
                    BankKey = sceneObject.SoundBankKey.ToString("X8"),
                    HasDecodedBank = document.CinematicSoundBanks.ContainsKey(sceneObject.SoundBankKey)
                }),
            SoundModifiers = world.Objects.SelectMany(sceneObject => sceneObject.Modifiers
                .Where(modifier => modifier.Type == 8 && modifier.Data.Length >= 16)
                .Select(modifier => new
                {
                    Object = sceneObject.Name,
                    ObjectKey = sceneObject.GaoKey.ToString("X8"),
                    modifier.Data.Length,
                    Id = Preview.I32(modifier.Data, 8),
                    Flags = Preview.U32(modifier.Data, 4).ToString("X8"),
                    ResourceOrBankKey = Preview.U32(modifier.Data, 12).ToString("X8"),
                    BankSlot = modifier.Data.Length >= 124 ? Preview.I32(modifier.Data, 120) : (int?)null,
                    Resolved = CinematicSoundModifier.Read(modifier, document) is not null,
                    BankArchiveEntries = archive.Entries.Where(entry => entry.Key == Preview.U32(modifier.Data, 12))
                        .Select(entry => new
                        {
                            entry.Name,
                            entry.Folder,
                            entry.StoredSize,
                            Prefix = Convert.ToHexString(archive.Read(entry).AsSpan(0, Math.Min(16, entry.StoredSize)))
                        })
                })),
            DocumentDiagnostics = document.Diagnostics.ToArray(),
            Cues = cues.Select(cue => new
            {
                cue.Start,
                cue.End,
                cue.OwnerKey,
                cue.ModifierId,
                cue.SoundKey,
                Asset = archive.Entries.FirstOrDefault(entry => entry.Key == cue.SoundKey)?.ExportName,
                cue.Volume,
                cue.Loop,
                cue.Label,
                cue.ChoiceGroup,
                cue.ChoiceIndex,
                cue.ChoiceCount
            }),
            DialogueTextReferences = dialogueLines,
            MatchingTextGroups = matchingTextGroups,
            InvisibleAtStart = initiallyHidden,
            StateSamples = stateSamples,
            Limits = "Source-time PCM mix, loaded bank language, without native spatialization, reverb or phoneme reconstruction."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AUDIO AUDIT: {container}/{clipKey:X8}: owner {source.CinematicOwnerKey:X8}, {mix.ResolvedCues} resolved cues -> {prefix}-cinematic-audio.wav");
    }

    private static SampleDeltaPeak FindLargestAdjacentSampleDelta(short[] samples, int channels,
        int sampleRate, double? centerTime = null, double radiusSeconds = 0)
    {
        int frameCount = samples.Length / channels;
        int firstFrame = centerTime is { } center
            ? Math.Max(1, (int)((center - radiusSeconds) * sampleRate))
            : 1;
        int lastFrame = centerTime is { } rangeCenter
            ? Math.Min(frameCount, (int)((rangeCenter + radiusSeconds) * sampleRate))
            : frameCount;
        long largestDelta = -1;
        int largestFrame = firstFrame;
        int largestChannel = 0;
        short previousSample = 0;
        short currentSample = 0;
        for (int frame = firstFrame; frame < lastFrame; frame++)
        for (int channel = 0; channel < channels; channel++)
        {
            short previous = samples[(frame - 1) * channels + channel];
            short current = samples[frame * channels + channel];
            long delta = Math.Abs((int)current - previous);
            if (delta <= largestDelta) continue;
            largestDelta = delta;
            largestFrame = frame;
            largestChannel = channel;
            previousSample = previous;
            currentSample = current;
        }

        return new(largestFrame / (double)sampleRate, largestDelta, largestChannel,
            previousSample, currentSample);
    }

    private static object[] DescribeSampleNeighborhood(short[] samples, int channels,
        int sampleRate, SampleDeltaPeak peak)
    {
        int centerFrame = (int)Math.Round(peak.Time * sampleRate);
        const int radiusFrames = 8;
        return Enumerable.Range(Math.Max(0, centerFrame - radiusFrames), radiusFrames * 2 + 1)
            .Where(frame => frame < samples.Length / channels)
            .Select(frame => (object)new
            {
                OffsetFrames = frame - centerFrame,
                Left = samples[frame * channels],
                Right = channels > 1 ? samples[frame * channels + 1] : (short?)null
            })
            .ToArray();
    }

    private static string[] DescribeWaveStructure(byte[] bytes)
    {
        var markers = new Dictionary<uint, (uint Position, string? Label, uint Length)>();
        var descriptions = new List<string>();
        for (int chunkOffset = 12; chunkOffset + 8 <= bytes.Length;)
        {
            string chunkName = System.Text.Encoding.ASCII.GetString(bytes, chunkOffset, 4);
            uint chunkSize = Preview.U32(bytes, chunkOffset + 4);
            int contentOffset = chunkOffset + 8;
            if (chunkSize > bytes.Length - contentOffset) break;
            int contentEnd = contentOffset + (int)chunkSize;
            switch (chunkName)
            {
                case "fmt " when chunkSize >= 16:
                    descriptions.Add($"fmt={Preview.U16(bytes, contentOffset)} "
                        + $"channels={Preview.U16(bytes, contentOffset + 2)} "
                        + $"rate={Preview.I32(bytes, contentOffset + 4)} "
                        + $"blockAlign={Preview.U16(bytes, contentOffset + 12)} "
                        + $"bits={Preview.U16(bytes, contentOffset + 14)} size={chunkSize}");
                    break;
                case "data":
                    descriptions.Add($"data bytes={chunkSize}");
                    break;
                case "cue " when chunkSize >= sizeof(uint):
                {
                    uint cueCount = Preview.U32(bytes, contentOffset);
                    if (cueCount > (chunkSize - sizeof(uint)) / 24) break;
                    for (int cueIndex = 0; cueIndex < cueCount; cueIndex++)
                    {
                        int cueOffset = contentOffset + sizeof(uint) + cueIndex * 24;
                        uint identifier = Preview.U32(bytes, cueOffset);
                        uint position = Preview.U32(bytes, cueOffset + 4);
                        markers[identifier] = (position, null, 0);
                    }
                    descriptions.Add($"cue markers={cueCount}");
                    break;
                }
                case "LIST" when chunkSize >= 4
                    && bytes.AsSpan(contentOffset, 4).SequenceEqual("adtl"u8):
                {
                    int associatedOffset = contentOffset + 4;
                    while (associatedOffset + 8 <= contentEnd)
                    {
                        string associatedName = System.Text.Encoding.ASCII
                            .GetString(bytes, associatedOffset, 4);
                        uint associatedSize = Preview.U32(bytes, associatedOffset + 4);
                        int associatedContent = associatedOffset + 8;
                        if (associatedSize < 4 || associatedSize > contentEnd - associatedContent) break;
                        uint identifier = Preview.U32(bytes, associatedContent);
                        var marker = markers.GetValueOrDefault(identifier);
                        if (associatedName == "labl")
                        {
                            int labelStart = associatedContent + 4;
                            int labelEnd = Array.IndexOf(bytes, (byte)0, labelStart,
                                (int)associatedSize - 4);
                            if (labelEnd < 0) labelEnd = associatedContent + (int)associatedSize;
                            markers[identifier] = (marker.Position,
                                System.Text.Encoding.ASCII.GetString(bytes, labelStart,
                                    labelEnd - labelStart), marker.Length);
                        }
                        else if (associatedName == "ltxt" && associatedSize >= 8)
                        {
                            markers[identifier] = (marker.Position, marker.Label,
                                Preview.U32(bytes, associatedContent + 4));
                        }
                        associatedOffset = associatedContent + (int)associatedSize
                            + ((int)associatedSize & 1);
                    }
                    break;
                }
                case "plst" when chunkSize >= sizeof(uint):
                {
                    uint segmentCount = Preview.U32(bytes, contentOffset);
                    descriptions.Add($"playlist segments={segmentCount}");
                    break;
                }
            }

            chunkOffset = contentEnd + ((int)chunkSize & 1);
        }

        foreach (var (identifier, marker) in markers.OrderBy(item => item.Value.Position))
            descriptions.Add($"cue {identifier}: sample={marker.Position} "
                + $"length={marker.Length} label={marker.Label ?? "(none)"}");
        return descriptions.ToArray();
    }

    private static object DescribeAdpcmDeltaHeaders(byte[] bytes)
    {
        int format = 0;
        int channels = 0;
        int blockAlign = 0;
        int dataOffset = 0;
        int dataSize = 0;
        for (int chunkOffset = 12; chunkOffset + 8 <= bytes.Length;)
        {
            int chunkSize = Preview.I32(bytes, chunkOffset + 4);
            int contentOffset = chunkOffset + 8;
            if (chunkSize < 0 || chunkSize > bytes.Length - contentOffset) break;
            if (bytes.AsSpan(chunkOffset, 4).SequenceEqual("fmt "u8) && chunkSize >= 16)
            {
                format = Preview.U16(bytes, contentOffset);
                channels = Preview.U16(bytes, contentOffset + 2);
                blockAlign = Preview.U16(bytes, contentOffset + 12);
            }
            else if (bytes.AsSpan(chunkOffset, 4).SequenceEqual("data"u8))
            {
                dataOffset = contentOffset;
                dataSize = chunkSize;
            }
            chunkOffset = contentOffset + chunkSize + (chunkSize & 1);
        }

        int headerBytes = 7 * channels;
        if (format != 2 || channels is < 1 or > 2 || blockAlign < headerBytes
            || dataOffset == 0 || dataSize < headerBytes)
            return new { Supported = false, Format = format, Channels = channels, BlockAlign = blockAlign };

        int blockCount = 0;
        int highBitDeltaCount = 0;
        int minimumSignedDelta = short.MaxValue;
        int maximumSignedDelta = short.MinValue;
        int minimumUnsignedDelta = ushort.MaxValue;
        int maximumUnsignedDelta = ushort.MinValue;
        int dataEnd = dataOffset + dataSize;
        for (int blockOffset = dataOffset; blockOffset + headerBytes <= dataEnd;
            blockOffset += blockAlign)
        {
            blockCount++;
            for (int channel = 0; channel < channels; channel++)
            {
                int deltaOffset = blockOffset + channels + channel * sizeof(ushort);
                int unsignedDelta = Preview.U16(bytes, deltaOffset);
                int signedDelta = Preview.I16(bytes, deltaOffset);
                if ((unsignedDelta & 0x8000) != 0) highBitDeltaCount++;
                minimumSignedDelta = Math.Min(minimumSignedDelta, signedDelta);
                maximumSignedDelta = Math.Max(maximumSignedDelta, signedDelta);
                minimumUnsignedDelta = Math.Min(minimumUnsignedDelta, unsignedDelta);
                maximumUnsignedDelta = Math.Max(maximumUnsignedDelta, unsignedDelta);
            }
        }

        return new
        {
            Supported = true,
            Format = format,
            Channels = channels,
            BlockAlign = blockAlign,
            BlockCount = blockCount,
            HighBitDeltaCount = highBitDeltaCount,
            MinimumSignedDelta = minimumSignedDelta,
            MaximumSignedDelta = maximumSignedDelta,
            MinimumUnsignedDelta = minimumUnsignedDelta,
            MaximumUnsignedDelta = maximumUnsignedDelta
        };
    }

    private static object[] FindTextGroups(Archive archive, CinematicDialogueLine[] dialogueLines)
    {
        var bankKeys = dialogueLines.Select(line => line.TextBankKey).ToHashSet();
        return archive.Entries.Where(entry => entry.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry =>
            {
                TextDocument? document = null;
                try { document = TextDocument.Read(archive.Read(entry)); }
                catch (InvalidDataException) { }
                return document?.Batches.SelectMany(batch => batch)
                    .Where(group => bankKeys.Contains(group.Id))
                    .Select(group => (object)new
                    {
                        entry.Name,
                        entry.Folder,
                        GroupKey = group.Id.ToString("X8"),
                        group.Flags,
                        StringCount = group.Strings.Count,
                        ReferenceBytes = group.References.Length,
                        ReferenceLinks = Enumerable.Range(0, group.References.Length / 16)
                            .Select(index =>
                            {
                                int offset = index * 16;
                                uint indexKey = Preview.U32(group.References, offset);
                                uint stringKey = Preview.U32(group.References, offset + 8);
                                return new
                                {
                                    IndexKey = indexKey.ToString("X8"),
                                    IndexType = System.Text.Encoding.ASCII.GetString(group.References,
                                        offset + 4, 4),
                                    IndexResource = archive.Entries.FirstOrDefault(entry =>
                                        entry.Key == indexKey)?.Name,
                                    StringKey = stringKey.ToString("X8"),
                                    StringType = System.Text.Encoding.ASCII.GetString(group.References,
                                        offset + 12, 4),
                                    StringResource = archive.Entries.FirstOrDefault(entry =>
                                        entry.Key == stringKey)?.Name
                                };
                            }).ToArray(),
                        Lines = dialogueLines.Where(line => line.TextBankKey == group.Id)
                            .Select(line => new
                            {
                                line.TextId,
                                line.SoundId,
                                Text = line.TextId >= 0 && line.TextId < group.Strings.Count
                                    ? group.Strings[line.TextId]
                                    : null
                            }).ToArray()
                    }) ?? [];
            }).ToArray();
    }

}
