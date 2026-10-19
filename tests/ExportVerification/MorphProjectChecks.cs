using System.Numerics;
using System.Reflection;
using JadeView;

internal static class MorphProjectChecks
{
    internal static FaceMorphPreview RoundTripSally(SceneActor actor, SceneObject head, WorldData world,
        ResourceResolver resolver, string output)
    {
        var document = new SceneDocument { ContainerName = "FF0057FD" };
        document.Worlds.Add(world);
        var project = new ModProject();
        project.CaptureMorphPreviews(document);
        string projectPath = Path.Combine(output, "sally-morph-project.bgemod");
        project.Save(projectPath);
        var loaded = ModProject.Load(projectPath);
        Check(loaded.BindMorphPreviews(document, resolver).Count == 0, "Real Sally morph project did not restore");
        var restored = head.MorphPreview!;
        Check(restored.Timeline.Keys.Count == 3 && restored.GetWeight(1) == .5f && !restored.Timeline.Playing,
            "Real Sally morph project lost animation keys or current pose");
        var result = SceneExporter.Export(Path.Combine(output, "sally-restored-morph.glb"), world, resolver,
            new() { Animations = true, Textures = true }, actor.Root);
        Check(result.Animations == 2, "Reloaded Sally morph animation was not exported");
        Console.WriteLine("PASS: real Sally .bgemod reload preserves facial keys and re-exports its morph clip");
        return restored;
    }

    internal static void Verify(string output)
    {
        var root = new SceneObject { Id = "actor-instance-1", GaoKey = 1 };
        var head = new SceneObject { Id = "actor-instance-1/head", GaoKey = 2, GeometryKey = 901, Name = "Fixture head" };
        var actor = new SceneActor { Root = root, Members = [root, head] };
        root.Actor = actor; head.Actor = actor;
        var world = new WorldData { Key = 77 };
        world.Actors.Add(actor); world.Objects.AddRange(actor.Members);
        var document = new SceneDocument { ContainerName = "FF_FIXTURE" };
        document.Worlds.Add(world);
        var geometry = new Geometry { Key = 901, Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
            Parts = [new(0, [0, 1, 2], [])] };
        head.Modifiers = [new(4, Payload())];
        var resolver = new ResourceResolver();
        var geometries = (Dictionary<uint, Geometry>)typeof(ResourceResolver).GetField("_geometries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resolver)!;
        geometries.Add(geometry.Key, geometry);
        var preview = FaceMorphPreview.Get(head, geometry)!;
        preview.Timeline.Record(0, [0, 0]); preview.Timeline.Record(.1f, [0, 1]);
        preview.Timeline.Loop = false; preview.Seek(.05f); preview.SetWeight(1, .4f); preview.Timeline.Playing = true;
        var project = new ModProject();
        project.CaptureMorphPreviews(document);
        Check(project.MorphPreviews.Count == 1 && !project.IsEmpty && !project.HasChanges,
            "Morph authoring state was lost or treated as a BF patch");
        string path = Path.Combine(output, "morph-project-roundtrip.bgemod");
        project.Save(path);
        var loaded = ModProject.Load(path);
        var snapshot = loaded.CreateSnapshot();
        Check(loaded.BindMorphPreviews(document, resolver).Count == 0, "Matching morph project was rejected");
        var restored = head.MorphPreview!;
        Check(restored.GetWeight(1) == .4f && restored.Timeline.Time == .05f && !restored.Timeline.Loop &&
            !restored.Timeline.Playing && restored.Timeline.Keys.Count == 2,
            "Project did not restore exact weights, keys, clock and loop with paused playback");
        Check(snapshot.MorphPreviews[0].Keys[1].Weights[1] == 1, "Project snapshots dropped morph keys");
        VerifySourceMismatch(loaded, document, resolver, head, geometry);
        VerifyAppearanceSwitch(loaded, document, resolver, geometries, actor, head, geometry);
        VerifyChannelRoundTrip(document, resolver, actor, head, geometry, output);
        Check(ModProject.FromJson("{\"formatVersion\":1,\"name\":\"legacy\"}").MorphPreviews.Count == 0,
            "Legacy project without morph data no longer loads");
        Reject(() => ModProject.FromJson("{\"morphPreviews\":[null]}"));
        var invalid = snapshot.CreateSnapshot();
        invalid.MorphPreviews[0].Keys = [new(0, [0, 0]), new(0, [0, 1])];
        Reject(() => ModProject.FromJson(invalid.ToJson()));
        invalid = snapshot.CreateSnapshot(); invalid.MorphPreviews[0].Weights = [0];
        Reject(() => ModProject.FromJson(invalid.ToJson()));
        invalid = snapshot.CreateSnapshot(); invalid.MorphPreviews.Add(invalid.MorphPreviews[0]);
        Reject(() => ModProject.FromJson(invalid.ToJson()));
        var history = new CommandHistory();
        history.Execute(new TransformCommand(new SceneObject(), Vector3.UnitX, Quaternion.Identity, Vector3.One));
        history.MarkExternalChange();
        history.Undo();
        Check(history.IsDirty, "Transform undo hid unsaved morph authoring edits");
        history.IsDirty = false;
        Check(!history.IsDirty, "Saving did not clear the external authoring dirty marker");
        Console.WriteLine("PASS: .bgemod morph round-trip, source checks, instance isolation, appearance switching, legacy loading and malformed record rejection");
    }

    private static void VerifySourceMismatch(ModProject project, SceneDocument document, ResourceResolver resolver,
        SceneObject head, Geometry geometry)
    {
        var original = geometry.Positions[0];
        geometry.Positions[0] += Vector3.UnitZ;
        Check(project.BindMorphPreviews(document, resolver).Count == 1 && head.MorphPreview!.GetWeight(1) == 0,
            "A different mesh with the same key/count accepted saved morph keys");
        project.CaptureMorphPreviews(document);
        Check(project.MorphPreviews.Count == 1 && project.MorphPreviews[0].Weights[1] == .4f,
            "Saving after a source mismatch erased retained authoring data");
        geometry.Positions[0] = original;
        var modifier = head.Modifiers[0];
        var changed = (byte[])modifier.Data.Clone(); changed[0] ^= 1;
        head.Modifiers = [new(4, changed)];
        Check(project.BindMorphPreviews(document, resolver).Count == 1 && head.MorphPreview!.Timeline.Keys.Count == 0,
            "A changed modifier accepted saved morph keys");
        head.Modifiers = [modifier];
        string id = head.Id;
        head.Id = "different-instance/head";
        Check(project.BindMorphPreviews(document, resolver).Count == 1 && head.MorphPreview!.GetWeight(1) == 0,
            "Shared GAO key was used instead of the exact object instance");
        head.Id = id;
        Check(project.BindMorphPreviews(document, resolver).Count == 0 && head.MorphPreview!.GetWeight(1) == .4f,
            "Restoring the matching source did not recover retained morph data");
    }

    private static void VerifyAppearanceSwitch(ModProject project, SceneDocument document, ResourceResolver resolver,
        Dictionary<uint, Geometry> geometries, SceneActor actor, SceneObject head, Geometry original)
    {
        var alternate = new Geometry { Key = 902, Positions = (Vector3[])original.Positions.Clone(), Parts = original.Parts };
        geometries.Add(alternate.Key, alternate);
        head.GeometryKey = alternate.Key;
        var preview = FaceMorphPreview.Get(head, alternate)!;
        Check(preview.GetWeight(1) == 0, "Weights leaked into another appearance");
        preview.SetWeight(1, .8f);
        project.CaptureMorphPreview(document, actor, head, preview);
        Check(project.MorphPreviews.Count == 2, "Saving another appearance replaced the first appearance");
        head.GeometryKey = original.Key;
        preview = FaceMorphPreview.Get(head, original)!;
        Check(preview.GetWeight(1) == .4f && preview.Timeline.Keys.Count == 2, "Switching back lost saved appearance keys");
        preview.Reset(); preview.Timeline.Clear();
        project.CaptureMorphPreviews(document);
        Check(project.MorphPreviews.Count == 1 && project.MorphPreviews[0].GeometryKey == alternate.Key,
            "Clearing one appearance removed unrelated saved profiles");
    }

    private static void VerifyChannelRoundTrip(SceneDocument document, ResourceResolver resolver,
        SceneActor actor, SceneObject head, Geometry geometry, string output)
    {
        var preview = FaceMorphPreview.Get(head, geometry)!;
        preview.Reset(); preview.Timeline.Clear();
        preview.SetChannel(0, .75f, .5f);
        var project = new ModProject();
        project.CaptureMorphPreviews(document);
        Check(!project.HasChanges && project.MorphPreviews.Single().ChannelState != null,
            "Channel state was lost or included in BF patches");
        string path = Path.Combine(output, "morph-channel-project.bgemod");
        project.Save(path);
        var loaded = ModProject.Load(path);
        var snapshot = loaded.CreateSnapshot();
        Check(loaded.BindMorphPreviews(document, resolver).Count == 0, "Saved channels did not restore");
        preview = head.MorphPreview!;
        Check(preview.ChannelsActive && preview.GetChannel(0) == new FaceMorphChannelState(.75f, .5f)
            && preview.GetWeight(1) == .375f && !preview.Timeline.Playing, "Channel controls/pose did not survive save/load");
        loaded.MorphPreviews[0].ChannelState![0] = new(1, 1);
        Check(snapshot.MorphPreviews[0].ChannelState![0].Weight == .75f, "Snapshot shares mutable channel state");
        loaded.MorphPreviews[0].ChannelState = [new(1, -1)];
        Reject(loaded.MorphPreviews[0].Validate);
        loaded.MorphPreviews[0].ChannelState = [];
        Check(loaded.BindMorphPreviews(document, resolver).Count == 1 && !head.MorphPreview!.ChannelsActive,
            "Wrong channel count applied to the source modifier");
        preview = FaceMorphPreview.Get(head, geometry)!;
        preview.SetChannel(0, 1, 0);
        project.CaptureMorphPreview(document, actor, head, preview);
        Check(project.MorphPreviews.Count == 1 && project.MorphPreviews[0].ChannelState != null,
            "Neutral channel configuration was discarded");
        preview.Reset();
        project.CaptureMorphPreviews(document);
        Check(project.MorphPreviews.Count == 0, "Reset left stale persisted channel controls");
        Console.WriteLine("PASS: channel .bgemod round-trip, snapshots, source-count validation, neutral profiles and reset");
    }

    private static byte[] Payload()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0u); writer.Write(0u); writer.Write(3);
        writer.Write(2); writer.Write(1); writer.Write(0);
        writer.Write(1); writer.Write(0); writer.Write(.1f); writer.Write(.2f); writer.Write(.3f);
        writer.Write(2); writer.Write(1f); writer.Write(0f); writer.Write(1u); writer.Write(1u);
        writer.Flush(); return stream.ToArray();
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Malformed morph project was accepted");
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}
