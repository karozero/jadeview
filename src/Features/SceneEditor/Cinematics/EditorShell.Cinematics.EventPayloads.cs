using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private const int MaximumVisibleRawEventPayloads = 24;
    private const int MaximumVisibleRawEventRows = 32;

    private static void DrawCinematicRawEventPayloads(CinematicEmbeddedEventTrackList eventList,
        CinematicEmbeddedEventTrack track)
    {
        if (track.SourceTrack is not { } sourceTrack) return;

        var payloads = sourceTrack.Keys.SelectMany((key, keyIndex) => key.OpaquePayloads
            .Select(payload => (keyIndex, key, payload))).ToArray();
        if (payloads.Length == 0
            || !ImGui.TreeNode($"Raw EVE key payloads ({payloads.Length})##rawEve"
                + $"{eventList.WorldKey:X8}{eventList.OwnerKey:X8}{eventList.EventKey:X8}{track.Index}"))
            return;

        ImGui.TextDisabled("Serialized key data for format research. Row categories, metadata and type IDs are raw fields unless a decoded command or message above identifies them.");
        foreach (var (keyIndex, key, payload) in payloads.Take(MaximumVisibleRawEventPayloads))
        {
            ImGui.BulletText($"Key {keyIndex} · {key.Time:F3}s · flags 0x{key.Flags:X4} · {payload.Kind} · {payload.Data.Length} bytes");
            if (CinematicWordPairList.TryRead(payload, out var rows))
            {
                foreach (var (row, rowIndex) in rows.Take(MaximumVisibleRawEventRows).Select((row, rowIndex) => (row, rowIndex)))
                {
                    uint category = row.Tag >> 24;
                    uint metadata = (row.Tag >> 16) & 0xFF;
                    uint type = row.Tag & 0xFFFF;
                    ImGui.TextDisabled($"  [{rowIndex:D2}] value 0x{row.Value:X8} · category {category} · metadata 0x{metadata:X2} · type 0x{type:X4}");
                }
                if (rows.Length > MaximumVisibleRawEventRows)
                    ImGui.TextDisabled($"  Showing {MaximumVisibleRawEventRows} of {rows.Length} serialized rows.");
            }
            else
            {
                string bytes = Convert.ToHexString(payload.Data.AsSpan(0,
                    Math.Min(payload.Data.Length, 32)));
                ImGui.TextDisabled($"  Raw bytes: {bytes}{(payload.Data.Length > 32 ? "…" : "")}");
            }
        }
        if (payloads.Length > MaximumVisibleRawEventPayloads)
            ImGui.TextDisabled($"Showing {MaximumVisibleRawEventPayloads} of {payloads.Length} serialized payloads.");

        ImGui.TreePop();
    }

    private void DrawCinematicCommandObjectArguments(CinematicLiteralCall call, uint worldKey,
        string commandScope)
    {
        foreach (int argumentIndex in CinematicEngineFunctionCatalog.GameObjectArgumentIndices(call))
        {
            uint objectKey = call.Arguments[argumentIndex];
            string objectName = ResolveCinematicObjectName(objectKey, worldKey) ?? $"{objectKey:X8} (unresolved)";
            string label = $"  Object argument {argumentIndex}: {objectName}";
            var sceneObject = _cinematicWorld?.Objects.FirstOrDefault(candidate =>
                candidate.GaoKey == objectKey);
            if (sceneObject != null && _cinematicWorld?.Key == worldKey
                && ImGui.Selectable($"{label}##command-object{worldKey:X8}{commandScope}{argumentIndex}"))
            {
                SelectedObject = sceneObject;
                Renderer.FrameSelected();
            }
            else
            {
                ImGui.TextWrapped(label);
            }
        }
    }
}
