using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class CinematicRliCallbackAudit
{
    private sealed record Sample(float Time, bool HasOverride, float? Factor,
        Vector4? DestinationColor, bool? FactorIsUsed, uint? OwnerCapabilities,
        long? CinematicMode, long? ScriptTime, long? ShieldDuration, string? SourceFunction,
        uint? TargetControlFlags, bool TargetIsControlHidden, uint TargetEffectiveDrawFlags);

    internal static void Write(string archivePath, string containerId, uint clipKey,
        uint ownerKey, uint targetKey, float start, float end, float step,
        string outputDirectory, IReadOnlyDictionary<int, bool?>? savedFlagAssumptions = null)
    {
        if (!float.IsFinite(start) || !float.IsFinite(end) || !float.IsFinite(step)
            || start < 0 || end < start || step <= 0 || (end - start) / step > 10000)
            throw new ArgumentOutOfRangeException(nameof(step), "Use a finite positive sample interval and range.");
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == containerId);
        SceneDocument document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        if (!document.CinematicClips.TryGetValue(clipKey, out AnimationClip? clip))
            throw new InvalidDataException($"Clip {clipKey:X8} was not found in {containerId}.");
        WorldData sourceWorld = document.Worlds.Single(world => world.Key == clip.CinematicWorldKey);
        WorldData world = CinematicWorldAssembler.Assemble(document, sourceWorld);
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);

        SceneObject owner = FindUnique(world, ownerKey);
        SceneObject target = FindUnique(world, targetKey);
        CinematicScriptOwner[] scriptOwners = document.CinematicScriptOwners.Values
            .Where(candidate => candidate.OwnerKey == ownerKey).ToArray();
        var sourceInspection = scriptOwners.Select(scriptOwner =>
        {
            CinematicScriptReachableEngineCall[] calls =
                CinematicScriptReader.ReachableEngineCalls(scriptOwner);
            CinematicScriptRliDisplayCallbackCandidate[] callbacks =
                CinematicScriptRliDisplayCallbackAnalyzer.Analyze(scriptOwner, calls);
            CinematicScriptRliValueFlowAssignment[] assignments =
                CinematicScriptReader.ReachableRliValueFlowAssignments(scriptOwner);
            string[] transparencyWriterFunctions = assignments
                .Where(assignment => assignment.Variable.Equals("f_transparency_coef",
                    StringComparison.OrdinalIgnoreCase))
                .Select(assignment => assignment.FunctionName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new
            {
                scriptOwner.ModelFunctionNames,
                InitialCapabilities = owner.InitialCapabilities.ToString("X8"),
                SerializedDomeReference = scriptOwner.EffectiveObjectReference("des_o_dome"),
                SerializedSaveFlags = scriptOwner.Variables.Where(variable =>
                        variable.Name.Contains("Flag", StringComparison.OrdinalIgnoreCase)
                        || variable.Name.Contains("sauve", StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                RelevantRuntimeVariables = scriptOwner.Variables.Where(variable =>
                        variable.Name.Equals("i_cine", StringComparison.OrdinalIgnoreCase)
                        || variable.Name.Equals("f_time", StringComparison.OrdinalIgnoreCase)
                        || variable.Name.Equals("des_f_time3", StringComparison.OrdinalIgnoreCase)
                        || variable.Name.Equals("f_transparency_coef", StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                RegistrationCalls = calls.Where(call => call.Api is "AICBAdd" or "AICBDel")
                    .Select(call => new
                    {
                        call.Api, call.FunctionName, call.Arguments, call.Source, call.Line,
                        call.BranchConditions
                    }).ToArray(),
                DisplayCallbacks = callbacks.Select(callback => new
                {
                    callback.ContextKey, callback.TargetKey, callback.CallbackType,
                    callback.FunctionName, callback.FactorExpression,
                    callback.DestinationColorExpression, callback.FactorIsUsed,
                    callback.BranchConditions, callback.EffectBranchConditions,
                    callback.CallbackPath, callback.HasMatchingRemovalSite
                }).ToArray(),
                TransparencyAssignments = assignments.Where(assignment =>
                        assignment.Variable.Equals("f_transparency_coef",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(assignment => new
                    {
                        assignment.FunctionName, assignment.Expression,
                        assignment.BranchConditions, assignment.CallbackPath
                    }).ToArray(),
                WriterFunctionAssignments = assignments.Where(assignment =>
                        transparencyWriterFunctions.Contains(assignment.FunctionName,
                            StringComparer.Ordinal))
                    .Select(assignment => new
                    {
                        assignment.FunctionName, assignment.Variable, assignment.Expression,
                        assignment.Line, assignment.BranchConditions
                    }).ToArray(),
                WriterFunctionCalls = calls.Where(call => transparencyWriterFunctions.Contains(
                        call.FunctionName, StringComparer.Ordinal))
                    .Select(call => new
                    {
                        call.FunctionName, call.Api, call.Arguments, call.ResultVariable,
                        call.Line, call.BranchConditions
                    }).ToArray(),
                CallbackPaths = CinematicScriptReader.NestedCallbackPaths(scriptOwner)
                    .Select(path => new { path.FunctionName, path.Steps }).ToArray()
            };
        }).ToArray();
        var playback = CinematicPlayback.Discover(document, world, resolver, archive.Entries)
            .Single(candidate => candidate.Key == clipKey);
        (float Time, CinematicLiteralCall Call)[] ownerTimelineCalls =
            CinematicLiteralCall.ReadTimeline(clip)
                .Where(item => item.Call.Target == ownerKey)
                .ToArray();
        IReadOnlyDictionary<int, bool?> assumptions = savedFlagAssumptions
            ?? new Dictionary<int, bool?>();
        playback.BindSavedFlagAssumptions(assumptions);

        var samples = new List<Sample>();
        playback.Activate();
        try
        {
            int sampleCount = (int)MathF.Floor((end - start) / step) + 1;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float time = Math.Min(end, start + sampleIndex * step);
                playback.Seek(time);
                CinematicRliDrawState? state = target.PreviewRliDrawState;
                IReadOnlyDictionary<string, long?> fields =
                    CinematicScriptRuntimeBranchEvaluator.ResolveFieldValues(
                        scriptOwners.Single(), owner, time, ownerTimelineCalls);
                samples.Add(new(time, state.HasValue, state?.Factor,
                    state?.DestinationColor, state?.FactorIsUsed, owner.PreviewCapabilities,
                    fields.GetValueOrDefault("i_cine"), fields.GetValueOrDefault("f_time"),
                    fields.GetValueOrDefault("des_f_time3"), state?.SourceFunction,
                    target.PreviewControlFlags, target.IsControlHidden, target.EffectiveDrawFlags));
            }
        }
        finally
        {
            playback.Deactivate();
        }

        var report = new
        {
            Container = containerId,
            Clip = clipKey.ToString("X8"),
            Owner = owner.Name,
            OwnerKey = ownerKey.ToString("X8"),
            Target = target.Name,
            TargetKey = targetKey.ToString("X8"),
            SavedFlagAssumptions = assumptions,
            SourceInspection = sourceInspection,
            Samples = samples,
            Limits = "Samples are JadeView preview draw-state. They do not prove retail alpha blending or that the same callbacks ran in a retail game session."
        };

        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory,
            $"cinematic-rli-callback-{containerId}-{clipKey:X8}-{ownerKey:X8}-{targetKey:X8}.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"CINEMATIC RLI CALLBACK AUDIT {owner.Name} {ownerKey:X8} → {target.Name} {targetKey:X8}");
        foreach (var inspection in sourceInspection)
        {
            Console.WriteLine($"  initialCapabilities=0x{inspection.InitialCapabilities}; domeReference={inspection.SerializedDomeReference}; source callbacks={inspection.DisplayCallbacks.Length}; registrations={inspection.RegistrationCalls.Length}; transparency writers={inspection.TransparencyAssignments.Length}; callback paths={inspection.CallbackPaths.Length}");
            foreach (var callback in inspection.DisplayCallbacks)
                Console.WriteLine($"    hook={callback.CallbackType} target={callback.TargetKey:X8} fn={callback.FunctionName} factor={callback.FactorExpression} reg-branches={string.Join(" | ", callback.BranchConditions)} effect-branches={string.Join(" | ", callback.EffectBranchConditions)} path={callback.CallbackPath}");
            foreach (var registration in inspection.RegistrationCalls)
                Console.WriteLine($"    register fn={registration.FunctionName} args={string.Join(",", registration.Arguments)} branches={string.Join(" | ", registration.BranchConditions)} at {registration.Source}:{registration.Line}");
        }
        foreach (Sample sample in samples)
            Console.WriteLine(sample.HasOverride
                ? $"  t={sample.Time:F3} factor={sample.Factor:F4} source={sample.SourceFunction} factorUsed={sample.FactorIsUsed} destination={sample.DestinationColor} capabilities=0x{sample.OwnerCapabilities:X8} targetHidden={sample.TargetIsControlHidden} targetFlags=0x{sample.TargetControlFlags:X8} drawFlags=0x{sample.TargetEffectiveDrawFlags:X8} i_cine={sample.CinematicMode} f_time={sample.ScriptTime} duration3={sample.ShieldDuration}"
                : $"  t={sample.Time:F3} no resolved draw override; capabilities=0x{sample.OwnerCapabilities:X8} targetHidden={sample.TargetIsControlHidden} targetFlags=0x{sample.TargetControlFlags:X8} drawFlags=0x{sample.TargetEffectiveDrawFlags:X8} i_cine={sample.CinematicMode} f_time={sample.ScriptTime} duration3={sample.ShieldDuration}");
        Console.WriteLine($"WROTE {outputPath}");
    }

    private static SceneObject FindUnique(WorldData world, uint key)
    {
        SceneObject[] matches = world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Where(sceneObject => sceneObject.GaoKey == key).Distinct().ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidDataException($"Expected one scene object {key:X8}; found {matches.Length}.");
    }
}
