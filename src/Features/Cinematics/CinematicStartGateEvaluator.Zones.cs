namespace JadeView;

internal static partial class CinematicStartGateEvaluator
{
    private static CinematicStartGateResult EvaluateZoneGate(CinematicScriptOwner owner,
        SceneObject[] worldObjects,
        CinematicStartGateDefinition gate)
    {
        return gate.Mask == CinematicStartGateCatalog.Zone1
            ? EvaluateZone1Gate(owner, worldObjects, gate)
            : EvaluateZone2Gate(owner, worldObjects, gate);
    }

    private static CinematicStartGateResult EvaluateZone1Gate(CinematicScriptOwner owner,
        SceneObject[] worldObjects,
        CinematicStartGateDefinition gate)
    {
        int? actorType = ReadInteger(owner, "des_i_actor_type1");
        CinematicScriptObjectReference? zoneReference = owner.EffectiveObjectReference(gate.Variable);
        bool usesActorFilter = actorType is not null and not 0;
        uint? serializedZoneKey = ReadUnsigned(owner, gate.Variable);
        uint? zoneTargetKey = zoneReference?.TargetKey ?? serializedZoneKey;
        bool usesImplicitOwnerZone = usesActorFilter && zoneTargetKey == 0;
        bool hasDynamicZoneTarget = usesActorFilter
            && zoneTargetKey is 1 or 2;
        bool mayUseOwnerFallback = usesActorFilter
            && !usesImplicitOwnerZone
            && (zoneReference is null || hasDynamicZoneTarget || !HasUniqueObject(zoneTargetKey ?? 0, worldObjects));
        uint? zoneKey = usesImplicitOwnerZone ? owner.OwnerKey : zoneTargetKey;
        CinematicScriptObjectReference? detectorReference = owner.EffectiveObjectReference("des_o_actor_detec1");
        string zoneDescription = usesImplicitOwnerZone
            ? DescribeResolvedObject("implicit current-owner zone", owner.OwnerKey, worldObjects)
            : DescribeZone1Reference(gate.Variable, zoneTargetKey, worldObjects, hasDynamicZoneTarget,
                mayUseOwnerFallback, owner.OwnerKey);
        string detectorDescription = DescribeReference("des_o_actor_detec1", detectorReference, worldObjects);

        string collisionDescription = usesActorFilter
            ? DescribeZone1ModelFilter(actorType!.Value)
            : DescribeZone1CollisionMode();
        string evidence = $"Iris zone 1 uses {zoneDescription} and {detectorDescription}. "
            + "During cine_init, Iris assigns the current owner as zone 1 when actor type is nonzero and the resolved zone reference is null. "
            + collisionDescription
            + " JadeView does not simulate the live overlap list, so this gate remains unknown.";
        var missingInputs = new List<string>
        {
            "runtime collision detection-list contents on zone 1",
            usesActorFilter
                ? "runtime model identity for overlapped objects"
                : "runtime identity flag 0x400 on the zone"
        };
        AddObjectRequirement(missingInputs, "zone 1", zoneKey, worldObjects);
        if (mayUseOwnerFallback)
        {
            missingInputs.Add("runtime resolution of des_o_actor_zone1 to determine whether Cine_Events falls back to its owner");
            AddObjectRequirement(missingInputs, "current Cine_Events owner fallback", owner.OwnerKey, worldObjects);
        }
        AddObjectRequirement(missingInputs, "des_o_actor_detec1", detectorReference?.TargetKey, worldObjects);

        return Unknown(gate, evidence, [.. missingInputs]);
    }

    private static CinematicStartGateResult EvaluateZone2Gate(CinematicScriptOwner owner,
        SceneObject[] worldObjects,
        CinematicStartGateDefinition gate)
    {
        CinematicScriptObjectReference? zoneReference = owner.EffectiveObjectReference(gate.Variable);
        CinematicScriptObjectReference? detectorReference = owner.EffectiveObjectReference("des_o_actor_detec2");
        int? collisionType = ReadInteger(owner, "des_i_collision_type2");
        string zoneDescription = DescribeReference(gate.Variable, zoneReference, worldObjects);
        string detectorDescription = DescribeReference("des_o_actor_detec2", detectorReference, worldObjects);
        string collisionMode = collisionType switch
        {
            null => "des_i_collision_type2 is unavailable, so the native positive/inverse branch is unknown.",
            0 => "des_i_collision_type2 is zero, so Iris inverts the selected zone collision result.",
            _ => "des_i_collision_type2 is nonzero, so Iris requires the selected zone collision result."
        };
        string evidence = $"Iris zone 2 uses {zoneDescription} and {detectorDescription}. "
            + "The zone's runtime identity flag 0x400 selects COLZDEPivotCollide when set, "
            + "or COLBVObjectObject when clear. " + collisionMode
            + " JadeView does not observe that runtime flag or collision result, so this gate remains unknown.";
        var missingInputs = new List<string>
        {
            "runtime identity flag 0x400 on the zone",
            "runtime collision query result"
        };
        if (collisionType is null)
            missingInputs.Add("des_i_collision_type2");
        AddObjectRequirement(missingInputs, "zone 2", zoneReference?.TargetKey, worldObjects);
        AddObjectRequirement(missingInputs, "des_o_actor_detec2", detectorReference?.TargetKey, worldObjects);

        return Unknown(gate, evidence, [.. missingInputs]);
    }

    private static string DescribeZone1ModelFilter(int actorType) => actorType switch
    {
        1 => "Iris reads zone 1's collision detection list, filters entries by object mask 0x20000, "
            + "and accepts objects matching its first configured model filter.",
        2 => "Iris reads zone 1's collision detection list, filters entries by object mask 0x20000, "
            + "and accepts objects matching its second configured model filter.",
        _ => $"Iris reads zone 1's collision detection list and filters entries by object mask 0x20000, "
            + $"but actor type {actorType} has no model-filter branch in this callback."
    };

    private static string DescribeZone1CollisionMode()
    {
        return "With no actor-model filter, Iris chooses between COLZDEPivotCollide and COLBVObjectObject "
            + "using runtime identity flag 0x400 on zone 1; the live flag and collision result are unknown.";
    }

    private static string DescribeReference(string variableName,
        CinematicScriptObjectReference? reference,
        SceneObject[] worldObjects)
    {
        if (reference is null)
            return $"{variableName} is unresolved";
        return DescribeResolvedObject(variableName, reference.TargetKey, worldObjects);
    }

    private static string DescribeZone1Reference(string variableName,
        uint? targetKey,
        SceneObject[] worldObjects,
        bool hasDynamicTarget,
        bool mayUseOwnerFallback,
        uint ownerKey)
    {
        if (hasDynamicTarget && CinematicObjectKey.SpecialActorSlotName(targetKey!.Value) is { } actorSlotName)
        {
            string fallback = mayUseOwnerFallback
                ? $"; if that runtime slot is empty, Iris assigns current owner 0x{ownerKey:X8} as zone 1"
                : string.Empty;
            return $"{variableName} selects {actorSlotName}{fallback}";
        }

        if (targetKey is null or uint.MaxValue)
        {
            string unresolved = targetKey is null
                ? $"{variableName} is unresolved in the preview"
                : $"{variableName} has serialized key 0xFFFFFFFF, which is not a resolvable archive object here";
            return mayUseOwnerFallback
                ? unresolved + $"; Iris assigns current owner 0x{ownerKey:X8} if the runtime zone reference resolves null"
                : unresolved;
        }

        return DescribeResolvedObject(variableName, targetKey.Value, worldObjects);
    }

    private static string DescribeResolvedObject(string label, uint targetKey, SceneObject[] worldObjects)
    {
        if (targetKey == 0)
            return $"{label} has a null target (0x00000000)";
        if (targetKey == uint.MaxValue)
            return $"{label} has serialized key 0xFFFFFFFF, which is not a resolvable archive object here";
        if (CinematicObjectKey.SpecialActorSlotName(targetKey) is { } actorSlotName)
            return $"{label} targets {actorSlotName}; its runtime binding is unavailable in the preview";

        SceneObject[] matches = worldObjects.Where(sceneObject => sceneObject.GaoKey == targetKey).Take(2).ToArray();
        if (matches.Length == 1)
            return $"{label} {matches[0].Name} (0x{targetKey:X8})";
        string state = matches.Length == 0 ? "is not loaded in the preview world" : "is ambiguous in the preview world";
        return $"{label} target 0x{targetKey:X8} {state}";
    }

    private static bool HasUniqueObject(uint targetKey, SceneObject[] worldObjects) =>
        CinematicObjectKey.IsArchiveObject(targetKey)
        && worldObjects.Count(sceneObject => sceneObject.GaoKey == targetKey) == 1;

    private static void AddObjectRequirement(List<string> missingInputs, string label,
        uint? targetKey, SceneObject[] worldObjects)
    {
        if (targetKey is null)
        {
            missingInputs.Add($"resolved {label} object reference");
            return;
        }

        if (targetKey is 0 or uint.MaxValue)
        {
            string unresolvedReference = targetKey == 0
                ? $"non-null runtime {label} target"
                : $"field-specific interpretation of 0xFFFFFFFF {label} reference";
            missingInputs.Add(unresolvedReference);
            return;
        }

        if (CinematicObjectKey.SpecialActorSlotName(targetKey.Value) is { } actorSlotName)
        {
            missingInputs.Add($"runtime AI_MainActor[{targetKey.Value - 1}] binding ({actorSlotName}) for {label}");
            return;
        }

        if (!HasUniqueObject(targetKey.Value, worldObjects))
            missingInputs.Add($"unique loaded {label} object {targetKey.Value:X8}");
    }
}
