using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Forms = System.Windows.Forms;

namespace JadeView;
public sealed partial class EditorShell
{
    private void DrawAddSceneObjectPopup()
    {
        if (!ImGui.BeginPopup("AddSceneObjectFromAsset"))
            return;
        DrawPanelTitle("ADD OBJECT", "Create an object from already loaded scene/FIX geometry in the active world.");
        DrawCategoryTitle("Placement");
        ImGui.TextDisabled("The object starts near the selection; use the existing gizmo to place it.");
        DrawCategoryTitle("Search");
        ImGui.SetNextItemWidth(360 * _scale);
        ImGui.InputTextWithHint("##AddGeometrySearch", "Filter by name or key...", ref _addGeometrySearch, 128);
        DrawCategoryTitle("Available geometry");
        ImGui.BeginChild("AddGeometryList", new Vector2(420 * _scale, 300 * _scale), ImGuiChildFlags.Borders);
        var geometries = Resolver.Geometries.Values
            .Where(CanAddGeometry)
            .Where(MatchesGeometrySearch)
            .OrderBy(geometry => geometry.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var geometry in geometries)
        {
            string label = $"{geometry.Name} · {geometry.Key:X8} · {geometry.Positions.Length:N0} vertices";
            if (ImGui.Selectable(label))
            {
                AddGeometryToScene(geometry);
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    private void AddGeometryToScene(Geometry geometry)
    {
        if (ActiveWorld == null)
            return;
        var transform = new TransformData
        {
            Translation = SelectedObject != null ? SelectedObject.Transform.Translation + new Vector3(2, 0, 0) : Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        };
        var obj = new SceneObject
        {
            Id = "editor-" + Guid.NewGuid().ToString("N"),
            GaoKey = NewEditorGaoKey(),
            Name = geometry.Name,
            GeometryKey = geometry.Key,
            MaterialKey = geometry.MaterialKey,
            Transform = transform,
            InitialTransform = transform.Clone(),
            Capabilities = new ObjectEditCapabilities
            {
                Flags = EditCapabilities.AllTransform
            },
            IsUserCreated = true
        };
        History.Execute(new SceneObjectPresenceCommand(ActiveWorld, obj, shouldExist: true));
    }

    private void DuplicateSceneObject(SceneObject source)
    {
        if (ActiveWorld == null || !CanDuplicateSceneObject(source))
            return;
        var transform = source.Transform.Clone();
        transform.Translation += new Vector3(1, 0, 0);
        var duplicate = new SceneObject
        {
            Id = "editor-" + Guid.NewGuid().ToString("N"),
            GaoKey = NewEditorGaoKey(),
            Name = source.Name + " Copy",
            SourceObjectId = source.IsUserCreated ? source.SourceObjectId : source.Id,
            Transform = transform,
            InitialTransform = transform.Clone(),
            RliColors = source.RliColors.ToArray(),
            DrawFlags = source.DrawFlags,
            GeometryKey = source.GeometryKey,
            MaterialKey = source.MaterialKey,
            Flags = source.Flags,
            Modifiers = source.Modifiers.Select(modifier => new SceneModifier(modifier.Type, modifier.Data.ToArray())).ToList(),
            Capabilities = new ObjectEditCapabilities
            {
                Flags = EditCapabilities.AllTransform
            },
            IsUserCreated = true
        };
        History.Execute(new SceneObjectPresenceCommand(ActiveWorld, duplicate, shouldExist: true));
    }

    private static bool CanDuplicateSceneObject(SceneObject sceneObject)
    {
        const uint unsupportedFlags = SceneLoader.IdentityFlagHierarchy |
            SceneLoader.IdentityFlagAnims | SceneLoader.IdentityFlagBone;
        bool hasUnsupportedStructure = (sceneObject.Flags & unsupportedFlags) != 0;
        bool canCopyStructure = sceneObject.IsUserCreated || !hasUnsupportedStructure;
        return sceneObject.Actor == null && canCopyStructure;
    }
    private void DeleteSceneObject(SceneObject obj)
    {
        if (ActiveWorld == null || obj.Actor != null || !ActiveWorld.Objects.Contains(obj))
            return;
        int index = ActiveWorld.Objects.IndexOf(obj);
        History.Execute(new SceneObjectPresenceCommand(ActiveWorld, obj, shouldExist: false, index));
    }

    private uint NewEditorGaoKey()
    {
        return Document?.AllocateObjectKey() ?? throw new InvalidOperationException("Open a scene before adding an object.");
    }

    private bool CanAddGeometry(Geometry geometry)
    {
        if (geometry.Positions.Length == 0)
            return false;
        if (!Resolver.ResourceItems.TryGetValue(geometry.Key, out var resource))
            return false;
        string scope = resource.Id.Scope;
        string container = Document?.ContainerName ?? "";
        bool isFixedResource = scope.Equals("FIX", StringComparison.OrdinalIgnoreCase);
        bool isSceneResource = scope.Equals(Path.GetFileNameWithoutExtension(container), StringComparison.OrdinalIgnoreCase) ||
            scope.Equals(Document?.ContainerName, StringComparison.OrdinalIgnoreCase);
        return isFixedResource || isSceneResource;
    }

    private bool MatchesGeometrySearch(Geometry geometry)
    {
        bool hasNoFilter = string.IsNullOrWhiteSpace(_addGeometrySearch);
        bool matchesName = geometry.Name.Contains(_addGeometrySearch, StringComparison.OrdinalIgnoreCase);
        bool matchesKey = geometry.Key.ToString("X8").Contains(_addGeometrySearch, StringComparison.OrdinalIgnoreCase);
        return hasNoFilter || matchesName || matchesKey;
    }
}
