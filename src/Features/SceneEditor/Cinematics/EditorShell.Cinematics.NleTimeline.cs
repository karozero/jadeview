using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private CinematicPlayback? _cinematicNlePlayback;
    private CinematicNleTimelineLane[] _cinematicNleLanes = [];

    private void DrawCinematicBottomWorkspace()
    {
        if (!ImGui.BeginTabBar("CinematicBottomTabs")) return;
        if (ImGui.BeginTabItem("Cutscenes"))
        {
            DrawCinematicBrowser();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Event Timeline"))
        {
            DrawCinematicNleTimeline();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawCinematicNleTimeline()
    {
        DrawPanelTitle("EVENT TIMELINE", "Read-only NLE-style overview of decoded source events. Drag the playhead or click a marker to seek; event editing is not supported yet.");
        if (_cinematicPlayback == null || _cinematicWorld == null || Document == null)
        {
            ImGui.TextDisabled("Load a cutscene to see its animation, script, message and audio lanes.");
            return;
        }

        if (!ReferenceEquals(_cinematicNlePlayback, _cinematicPlayback))
        {
            _cinematicNlePlayback = _cinematicPlayback;
            _cinematicNleLanes = CinematicNleTimelineBuilder.Build(_cinematicPlayback,
                _cinematicWorld, Document);
        }

        ImGui.TextDisabled($"{_cinematicPlayback.Name} · {_cinematicPlayback.Time:F2} / {_cinematicPlayback.Duration:F2}s · "
            + $"{_cinematicNleLanes.Length} lanes · drag to seek");
        if (_cinematicNleLanes.Length == 0)
        {
            ImGui.TextDisabled("No decoded animation or event records are available for this clip.");
            return;
        }

        bool tracksVisible = ImGui.BeginChild("CinematicNleTracks", Vector2.Zero, ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);
        if (tracksVisible)
        {
            float timelineWidth = Math.Max(320 * _scale, ImGui.GetContentRegionAvail().X - 150 * _scale);
            float labelWidth = 145 * _scale;
            if (ImGui.BeginTable("CinematicNleTrackTable", 2,
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextDisabled("Time");
                ImGui.TableSetColumnIndex(1);
                DrawNleRuler(timelineWidth);

                for (int laneIndex = 0; laneIndex < _cinematicNleLanes.Length; laneIndex++)
                {
                    CinematicNleTimelineLane lane = _cinematicNleLanes[laneIndex];
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextDisabled($"{LaneIcon(lane.Kind)} {lane.Name}");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{lane.Items.Length} item(s) · {LaneDescription(lane.Kind)}");
                    ImGui.TableSetColumnIndex(1);
                    DrawNleLane(lane, laneIndex, timelineWidth);
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawNleRuler(float width)
    {
        float height = 22 * _scale;
        ImGui.InvisibleButton("CinematicNleRuler", new(width, height));
        Vector2 start = ImGui.GetItemRectMin();
        Vector2 end = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(ImGuiCol.FrameBg));
        float duration = Math.Max(_cinematicPlayback!.Duration, .001f);
        float step = duration switch { > 120 => 15, > 60 => 10, > 25 => 5, > 12 => 2, _ => 1 };
        for (float time = 0; time <= duration; time += step)
        {
            float x = start.X + width * time / duration;
            drawList.AddLine(new(x, end.Y - 7 * _scale), new(x, end.Y),
                ImGui.GetColorU32(ImGuiCol.TextDisabled));
            drawList.AddText(new(x + 2 * _scale, start.Y + 1 * _scale),
                ImGui.GetColorU32(ImGuiCol.TextDisabled), $"{time:0}s");
        }
        DrawNlePlayhead(drawList, start, end, width, duration);
        if (ImGui.IsItemActive())
            SeekFromNlePointer(start.X, width, duration);
    }

    private void DrawNleLane(CinematicNleTimelineLane lane, int laneIndex, float width)
    {
        float height = 21 * _scale;
        ImGui.InvisibleButton($"CinematicNleLane{laneIndex}", new(width, height));
        Vector2 start = ImGui.GetItemRectMin();
        Vector2 end = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(ImGuiCol.FrameBg));
        drawList.AddLine(new(start.X, end.Y), end, ImGui.GetColorU32(ImGuiCol.Border));

        float duration = Math.Max(_cinematicPlayback!.Duration, .001f);
        foreach (CinematicNleTimelineItem item in lane.Items)
        {
            if (!float.IsFinite(item.Start) || item.Start < 0 || item.Start > duration) continue;
            float itemStart = Math.Clamp(item.Start / duration, 0, 1);
            float itemEnd = Math.Clamp(item.End / duration, itemStart, 1);
            float x = start.X + itemStart * width;
            uint color = LaneColor(lane.Kind);
            if (item.End > item.Start)
            {
                float barEnd = start.X + Math.Max(itemEnd * width, itemStart * width + 4 * _scale);
                drawList.AddRectFilled(new(x, start.Y + 4 * _scale),
                    new(Math.Min(barEnd, end.X), end.Y - 4 * _scale), color, 2 * _scale);
            }
            else
            {
                drawList.AddLine(new(x, start.Y + 3 * _scale), new(x, end.Y - 3 * _scale),
                    color, 2 * _scale);
                drawList.AddCircleFilled(new(x, (start.Y + end.Y) * .5f), 3 * _scale, color);
            }
        }
        DrawNlePlayhead(drawList, start, end, width, duration);
        if (ImGui.IsItemActive())
            SeekFromNlePointer(start.X, width, duration);
        else if (ImGui.IsItemHovered())
            DrawNleItemTooltip(lane, start.X, width, start.Y, end.Y, duration);
    }

    private void DrawNleItemTooltip(CinematicNleTimelineLane lane, float originX, float width,
        float top, float bottom, float duration)
    {
        float mouseX = ImGui.GetIO().MousePos.X;
        float mouseY = ImGui.GetIO().MousePos.Y;
        if (mouseY < top || mouseY > bottom) return;
        float hoveredTime = Math.Clamp((mouseX - originX) / width, 0, 1) * duration;
        CinematicNleTimelineItem? hoveredItem = lane.Items.FirstOrDefault(item =>
            hoveredTime >= item.Start && hoveredTime <= Math.Max(item.End, item.Start + .12f));
        if (hoveredItem != null)
            ImGui.SetTooltip($"{hoveredItem.Label} · {hoveredItem.Start:F2}s\n{hoveredItem.Details}");
    }

    private void SeekFromNlePointer(float originX, float width, float duration)
    {
        float normalizedTime = Math.Clamp((ImGui.GetIO().MousePos.X - originX) / width, 0, 1);
        _cinematicPlayback!.Pause();
        _cinematicPlayback.Seek(normalizedTime * duration);
        _cinematicVideoPostRollActive = false;
        _cinematicVideoPostRollCompleted = false;
        _cinematicVideoPostRollSeconds = 0;
    }

    private void DrawNlePlayhead(ImDrawListPtr drawList, Vector2 start, Vector2 end,
        float width, float duration)
    {
        float x = start.X + width * Math.Clamp(_cinematicPlayback!.Time / duration, 0, 1);
        drawList.AddLine(new(x, start.Y), new(x, end.Y), 0xFF5C80FF, 2 * _scale);
        drawList.AddTriangleFilled(new(x - 4 * _scale, start.Y), new(x + 4 * _scale, start.Y),
            new(x, start.Y + 6 * _scale), 0xFF5C80FF);
    }

    private static string LaneIcon(CinematicNleLaneKind kind) => kind switch
    {
        CinematicNleLaneKind.Animation => "A",
        CinematicNleLaneKind.BodyAction => "B",
        CinematicNleLaneKind.Script => "E",
        CinematicNleLaneKind.Message => "M",
        CinematicNleLaneKind.Audio => "S",
        _ => "·"
    };

    private static string LaneDescription(CinematicNleLaneKind kind) => kind switch
    {
        CinematicNleLaneKind.Animation => "decoded animation key ranges",
        CinematicNleLaneKind.BodyAction => "resolved actor body-action ranges",
        CinematicNleLaneKind.Script => "decoded engine calls from clip and nested EVE tracks",
        CinematicNleLaneKind.Message => "decoded typed messages",
        CinematicNleLaneKind.Audio => "resolved source audio cues",
        _ => "decoded cinematic source data"
    };

    private static uint LaneColor(CinematicNleLaneKind kind) => kind switch
    {
        CinematicNleLaneKind.Animation => 0xFF64C98B,
        CinematicNleLaneKind.BodyAction => 0xFF85B5FF,
        CinematicNleLaneKind.Script => 0xFF4BC9F2,
        CinematicNleLaneKind.Message => 0xFFFFBE5C,
        CinematicNleLaneKind.Audio => 0xFFCA7CFF,
        _ => 0xFFFFFFFF
    };
}
