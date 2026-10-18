using System.Numerics;
using System.Text;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
    #region 3. Outliner (Дерево objects справа сверху)

    private void DrawOutliner()
    {
        ImGui.TextColored(Green, "OUTLINER");
        ImGui.SameLine();
        ImGui.TextDisabled(ActiveWorld != null ? $"{ActiveWorld.Name} ({ActiveWorld.Objects.Count})" : "No world");

        if (Archive != null && _archiveScenes.Count > 0)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.BeginDisabled(_activeJob != null);
            var activeArchiveScene = _archiveScenes.FirstOrDefault(s =>
                string.Equals(Document?.ContainerName, s.Entry.Name, StringComparison.OrdinalIgnoreCase));
            string scenePreview = activeArchiveScene != null
                ? $"{activeArchiveScene.Label} · {activeArchiveScene.Entry.Id}"
                : "Archive scenes";
            if (ImGui.BeginCombo("##ArchiveScenePicker", scenePreview))
            {
                foreach (var scene in _archiveScenes)
                {
                    string label = $"{scene.Label} · {scene.Entry.Id}";
                    bool selected=Document?.ContainerName.Equals(scene.Entry.Name, StringComparison.OrdinalIgnoreCase)==true;
                    if (ImGui.Selectable(label, selected))
                        OpenSceneContainer(scene.Entry);
                    if (selected) ImGui.SetItemDefaultFocus();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(label + (scene.Diagnostic != null ? "\n" + scene.Diagnostic : ""));
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
        }

        // Выбор активного мира, если их несколько в FF0
        if (Document != null && Document.Worlds.Count > 1)
        {
            if (ImGui.BeginCombo("##WorldPicker", ActiveWorld?.Name ?? "Select world"))
            {
                foreach (var w in Document.Worlds)
                {
                    bool selected=w==ActiveWorld;
                    if (ImGui.Selectable($"{w.Name} ({w.Objects.Count} objects)", selected))
                    {
                        ActiveWorld = w;
                        Renderer.ActiveWorld = w;
                        SelectedObject = w.Objects.FirstOrDefault();
                        Renderer.FrameAll();
                    }
                    if(selected)ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchOutliner", "Search name or GAO key...", ref _searchOutliner, 128);

        // Фильтры категорий
        if (ImGui.RadioButton("All", ref _outlinerFilterType, 0)) { }
        ImGui.SameLine();
        if (ImGui.RadioButton("Static", ref _outlinerFilterType, 1)) { }
        ImGui.SameLine();
        if (ImGui.RadioButton("Restricted", ref _outlinerFilterType, 2)) { }

        bool showSelectionWireframe = Renderer.ShowSelectionWireframe;
        if (ImGui.Checkbox("Selection wireframe", ref showSelectionWireframe))
            Renderer.ShowSelectionWireframe = showSelectionWireframe;

        ImGui.Separator();

        if (ActiveWorld == null || ActiveWorld.Objects.Count == 0)
        {
            ImGui.TextDisabled("No objects in the active world.");
            return;
        }

        var filtered = ActiveWorld.Objects.Where(o =>
        {
            if (!string.IsNullOrEmpty(_searchOutliner))
            {
                bool matches = o.Name.Contains(_searchOutliner, StringComparison.OrdinalIgnoreCase) ||
                               o.GaoKey.ToString("X8").Contains(_searchOutliner, StringComparison.OrdinalIgnoreCase);
                if (!matches) return false;
            }
            if (_outlinerFilterType == 1 && !o.Capabilities.CanTranslate) return false;
            if (_outlinerFilterType == 2 && o.Capabilities.CanTranslate) return false;
            return true;
        }).ToList();

        ImGui.BeginChild("OutlinerList", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawAnimationControls();
        var shown = filtered.ToHashSet();
        foreach (var actor in ActiveWorld.Actors)
        {
            if(!actor.Members.Any(shown.Contains))continue;
            ImGui.PushID(actor.Root.Id);
            var flags=ImGuiTreeNodeFlags.OpenOnArrow|ImGuiTreeNodeFlags.SpanAvailWidth;
            if(SelectedObject==actor.Root)flags|=ImGuiTreeNodeFlags.Selected;
            if (_revealOutliner?.Actor==actor && _revealOutliner!=actor.Root) ImGui.SetNextItemOpen(true);
            bool open=ImGui.TreeNodeEx($"[Actor] {actor.Root.Name} ({actor.Members.Count-1})",flags);
            DrawObjectContextMenu(actor.Root);
            if(_revealOutliner==actor.Root){ImGui.SetScrollHereY(.5f);_revealOutliner=null;}
            if(ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())SelectedObject=actor.Root;
            if(ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)){SelectedObject=actor.Root;Renderer.FrameSelected();}
            if(open)
            {
                foreach(var component in actor.Members.Where(o=>o!=actor.Root))
                {
                    if(ImGui.Selectable($"{component.Name}##{component.Id}",SelectedObject==component))SelectedObject=component;
                    DrawObjectContextMenu(component);
                    if(_revealOutliner==component){ImGui.SetScrollHereY(.5f);_revealOutliner=null;}
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        foreach(var obj in filtered.Where(o=>o.Actor==null))
        {
            ImGui.PushID(obj.Id);
            if(ImGui.Selectable($"{(Renderer.IsHidden(obj)?"[hidden] ":"")}{obj.Name} ({obj.GaoKey:X8})",SelectedObject==obj))SelectedObject=obj;
            DrawObjectContextMenu(obj);
            if(_revealOutliner==obj){ImGui.SetScrollHereY(.5f);_revealOutliner=null;}
            if(ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)){SelectedObject=obj;Renderer.FrameSelected();}
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private void DrawObjectContextMenu(SceneObject obj)
    {
        if(!ImGui.BeginPopupContextItem("ObjectActions##"+obj.Id))return;
        SelectedObject=obj;
        if(ImGui.MenuItem("Export...")){_exportTarget=obj;_openExport=true;}
        ImGui.EndPopup();
    }

    private void DrawExportDialog()
    {
        if(_openExport){ImGui.OpenPopup("Export GLB");_openExport=false;}
        ImGui.SetNextWindowSize(new Vector2(460*_scale,0),ImGuiCond.Appearing);
        if(!ImGui.BeginPopupModal("Export GLB",ImGuiWindowFlags.AlwaysAutoResize))return;
        ImGui.TextWrapped(_exportTarget==null?$"Level: {ActiveWorld?.Name}":$"Object: {_exportTarget.Name}");
        if(_exportTarget?.Actor is {} actor && _exportTarget==actor.Root)ImGui.TextDisabled($"Actor with {actor.Members.Count-1} components");
        ImGui.Separator();
        ImGui.Checkbox("Embed textures",ref _exportOptions.Textures);
        if(_exportTarget==null)ImGui.Checkbox("Include actors",ref _exportOptions.Actors);
        ImGui.Checkbox("Include collision meshes",ref _exportOptions.Collisions);
        ImGui.Checkbox("Include unknown GAOs",ref _exportOptions.UnknownObjects);
        if(_exportOptions.UnknownObjects)ImGui.TextWrapped("GAOs without decoded geometry become named empty objects with their source keys.");
        ImGui.Checkbox("Include game-hidden objects",ref _exportOptions.GameHidden);
        bool hasAnimations=_exportTarget?.Actor!=null || _exportTarget==null && ActiveWorld?.Actors.Count>0;
        ImGui.BeginDisabled(!hasAnimations);
        ImGui.Checkbox("Animations and skinning",ref _exportOptions.Animations);
        if(_exportOptions.Animations)ImGui.Checkbox("All clips (otherwise current / first)",ref _exportOptions.AllClips);
        ImGui.EndDisabled();
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##ExportPlacement",ref _exportOptions.Placement,new[]{"Current placement","Original game placement","At origin"},3);
        ImGui.TextWrapped("One .glb file for Blender. Jade multi-pass effects are simplified; animations use the current mesh appearance.");
        ImGui.Separator();
        if(ImGui.Button("Export...",new Vector2(120*_scale,0)) && ActiveWorld!=null)
        {
            using var dialog=new Forms.SaveFileDialog{Title="Export Blender-compatible GLB",Filter="Binary glTF (*.glb)|*.glb",DefaultExt="glb",AddExtension=true,FileName=SceneExporter.SafeFileName(_exportTarget?.Name??ActiveWorld.Name)+".glb"};
            if(dialog.ShowDialog()==Forms.DialogResult.OK)
            {
                try
                {
                    Forms.Cursor.Current=Forms.Cursors.WaitCursor;
                    var result=SceneExporter.Export(dialog.FileName,ActiveWorld,Resolver,_exportOptions,_exportTarget);
                    _statusMessage=$"Exported {result.Meshes} meshes, {result.Textures} textures, {result.Animations} animations, {result.EmptyObjects} empty GAOs: {dialog.FileName}";
                    if(result.Warnings.Length>0)_statusMessage+=$" ({result.Warnings.Length} format limitations; see GLB extras)";
                    ImGui.CloseCurrentPopup();
                }
                catch(Exception ex){SetError("Export failed: "+ex.Message);}
                finally{Forms.Cursor.Current=Forms.Cursors.Default;}
            }
        }
        ImGui.SameLine();if(ImGui.Button("Cancel"))ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawAnimationControls()
    {
        var actor=SelectedObject?.Actor;if(actor==null)return;
        if(!ImGui.CollapsingHeader($"Animation · {actor.Root.Name}##ActorAnimation",ImGuiTreeNodeFlags.DefaultOpen))return;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(5,2));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(6,3));
        ImGui.SetNextItemWidth(-1);
        if(ImGui.BeginCombo("##ActorClip",actor.Clip?.Name??"Default pose"))
        {
            bool defaultSelected=actor.Clip==null;
            if(ImGui.Selectable("Default pose",defaultSelected))actor.SelectClip(null);
            if(defaultSelected)ImGui.SetItemDefaultFocus();
            for(int i=0;i<actor.Clips.Count;i++)
            {
                var clip=actor.Clips[i];
                bool selected=actor.Clip==clip;
                if(ImGui.Selectable($"{i:D3} · {clip.Name}",selected))actor.SelectClip(clip);
                if(selected)ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        if(actor.Diagnostic!=null)ImGui.TextWrapped(actor.Diagnostic);
        else if(actor.Clips.Count==0)ImGui.TextDisabled("No animations found in loaded packages.");
        if(actor.Clip is {} c)
        {
            if(ImGui.Button(actor.Playing?"Pause":"Play")){if(!actor.Playing && actor.Time>=c.Duration)actor.Time=0;actor.Playing=!actor.Playing;}
            ImGui.SameLine();if(ImGui.Button("Restart")){actor.Time=0;actor.Evaluate(0);}
            ImGui.SameLine();ImGui.Checkbox("Loop",ref actor.Loop);
            ImGui.Checkbox("Root motion",ref actor.RootMotion);
            float time=actor.Time;ImGui.SetNextItemWidth(-1);
            if(ImGui.SliderFloat("##ClipTime",ref time,0,Math.Max(c.Duration,.001f),"%.2f s")){actor.Time=time;actor.Playing=false;actor.Evaluate(0);}
            ImGui.SetNextItemWidth(100);ImGui.SliderFloat("Speed",ref actor.Speed,.1f,3,"%.2fx");
            if(c.Error!=null)ImGui.TextColored(Yellow,c.Error);
            else if(c.Unsupported>0)ImGui.TextColored(Yellow,$"Unsupported events: {c.Unsupported}");
        }
        ImGui.Separator();
        ImGui.PopStyleVar(2);
    }

    #endregion
}
