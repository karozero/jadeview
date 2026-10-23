using static SkinExportChecks;
using static GameExportChecks;
using System.Numerics;
using System.Reflection;
using JadeView;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
SceneRendererChecks.Verify();
OutlinerRowChecks.Verify();
string output = Path.GetFullPath(Environment.GetEnvironmentVariable("JADEVIEW_EXPORT_VERIFICATION_OUTPUT")
    ?? "artifacts/export-verification");
Directory.CreateDirectory(output);
if (VerificationCommands.TryRun(args, output)) return;
GameWorldUniverseReducerChecks.Verify();
GameWorldListDescriptorChecks.Verify();
GameScriptWorldListResolverChecks.Verify();
GameScriptObjectFlagsChecks.Verify();
PcCollisionResourceChecks.Verify();
CinematicActionLibraryChecks.Verify();
CinematicEngineFunctionCatalogChecks.Verify();
CinematicAfterEffectChecks.Verify();
CinematicAfterEffectRenderChecks.Verify();
CinematicScriptInspectorChecks.Verify();
CinematicNestedEventTrackChecks.Verify();
CinematicCompanionActionRequestChecks.Verify();
CinematicNestedEventTransformChecks.Verify(output);
CinematicOrientToChecks.Verify(output);
CinematicLocalRotationChecks.Verify();
CinematicEventTrackPreviewChecks.Verify();
CinematicClipIncomingLinkChecks.Verify();
PcProceduralTextureChecks.Verify();
CinematicPlaybackChecks.Verify(output);
CinematicCameraChecks.Verify(output);
CinematicCameraLensChecks.Verify(output);
CinematicCameraRoutingChecks.Verify(output);
CinematicSceneScopeChecks.Verify();
CinematicTimelineClipChecks.Verify();
CinematicBodyChecks.Verify();
CinematicMotionChecks.Verify();
CinematicTransitionChecks.Verify();
CinematicBlendChecks.Verify();
CinematicRootBlendChecks.Verify();
CinematicDynamicsMotionChecks.Verify();
CinematicFrequencyChecks.Verify();
CinematicScaleChecks.Verify(output);
CinematicShutterChecks.Verify();
CinematicFlashChecks.Verify();
CinematicCameraPagChecks.Verify();
CinematicLightningChecks.Verify();
CinematicScreenFadeChecks.Verify();
CinematicFacialSignalChecks.Verify();
CinematicStateChecks.Verify(output);
CinematicGroupStateChecks.Verify();
CinematicPhaseTransitionChecks.Verify();
AnimationBezierChecks.Verify();
CinematicEnvironmentChecks.Verify();
CinematicMaterialChecks.Verify();
CinematicParticleStateChecks.Verify();
CinematicAudioChecks.Verify();
CinematicVideoPlaybackChecks.Verify();
CinematicShockwaveChecks.Verify();
MorphReaderChecks.Verify();
MorphChannelChecks.Verify();
MorphTransitionChecks.Verify();
MorphMouthCycleChecks.Verify();
MorphCompletionChecks.Verify();
MorphSourceInitializationChecks.Verify(output);
ActorModuleChecks.Verify();
PcScalarVariableChecks.Verify();
MorphBlinkChecks.Verify();
SallyExpressionChecks.Verify();
MorphDiscoveryChecks.Verify();
AnimationPayloadChecks.Verify();
AnimationPackedRotationChecks.Verify();
AnimationTimeCurveChecks.Verify();
AnimationFrameTimingChecks.Verify();
MorphTimelineChecks.Verify();
MorphProjectChecks.Verify(output);
MaterialExportChecks.VerifyMaterials(output);
LayerBakeChecks.Verify(output);
GeometryCarrierChecks.Verify(output);
ParticlePreviewChecks.Verify();
var resolver = new ResourceResolver();
var geometry = new Geometry
{
    Key = 101,
    Positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
    Uvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(.25f, .75f)],
    Parts = [new(0, [0, 1, 2, 0, 2, 3], [0, 1, 2, 4, 2, 3])]
};
Register(resolver, "_geometries", geometry.Key, geometry);
var sceneObject = new SceneObject { Id = "fixture", Name = "UV seam fixture", GaoKey = 11, GeometryKey = geometry.Key };
var world = new WorldData { Name = "Export verification" };
world.Objects.Add(sceneObject);
string staticPath = Path.Combine(output, "uv-seam.glb");
SceneExporter.Export(staticPath, world, resolver, new() { Textures = false });
using (var glb = new GlbReader(staticPath))
{
    var primitive = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var attributes = primitive.GetProperty("attributes");
    var positions = glb.Accessor(attributes.GetProperty("POSITION").GetInt32());
    var uvs = glb.Accessor(attributes.GetProperty("TEXCOORD_0").GetInt32());
    var indices = glb.Accessor(primitive.GetProperty("indices").GetInt32());
    Check(positions.Length == 5 && indices.Length == 6, "Indexed mesh must share vertices except at the UV seam");
    int[] expectedSources = [0, 1, 2, 0, 2, 3];
    int[] expectedUvs = [0, 1, 2, 4, 2, 3];
    for (int corner = 0; corner < 6; corner++)
    {
        int index = (int)indices[corner][0];
        Near(GlbReader.Vector(positions[index]), Geometry.ToRender(geometry.Positions[expectedSources[corner]]), "Triangle corner position");
        var uv = geometry.Uvs[expectedUvs[corner]];
        Near(new(uvs[index][0], uvs[index][1], 0), new(uv.X, 1 - uv.Y, 0), "UV seam and V orientation");
    }
}
Console.WriteLine("PASS: indexed geometry, winding/corner order, UV seams and UVs without embedded textures");

var materialBytes = new byte[56];
BitConverter.GetBytes(5).CopyTo(materialBytes, 0);
BitConverter.GetBytes(7u).CopyTo(materialBytes, 40);
BitConverter.GetBytes(2f).CopyTo(materialBytes, 44);
BitConverter.GetBytes(3f).CopyTo(materialBytes, 48);
BitConverter.GetBytes(900u).CopyTo(materialBytes, 52);
Register(resolver, "_materials", 501, new ResolvedMaterial { Type = 5, SourceBytes = materialBytes });
sceneObject.MaterialKey = 501;
string uvPath = Path.Combine(output, "transformed-uv.glb");
SceneExporter.Export(uvPath, world, resolver, new() { Textures = false });
using (var glb = new GlbReader(uvPath))
{
    var primitive = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var uvs = glb.Accessor(primitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32());
    var indices = glb.Accessor(primitive.GetProperty("indices").GetInt32());
    int seam = (int)indices[3][0];
    Near(new(uvs[seam][0], uvs[seam][1], 0), new(.5f, -1.25f, 0), "UV transform must precede V conversion and preserve tiling");
}
sceneObject.MaterialKey = 0;
VerifyLargeMesh(output);
Console.WriteLine("PASS: material UV transform order, tiling outside [0,1] and 32-bit mesh indices");

var root = new SceneObject
{
    Id = "root",
    Name = "Actor root",
    GaoKey = 20,
    Transform = new() { Translation = new(8, -2, 1) }
};
var firstBone = new SceneObject
{
    Id = "bone-a",
    Name = "First bone",
    GaoKey = 21,
    ActorNode = new() { Parent = root.GaoKey, Local = Matrix4x4.CreateTranslation(3, 0, 0) }
};
var secondBone = new SceneObject
{
    Id = "bone-b",
    Name = "Second bone",
    GaoKey = 22,
    ActorNode = new() { Parent = root.GaoKey, Local = Matrix4x4.CreateTranslation(7, 0, 0) }
};
sceneObject.ActorNode = new()
{
    Parent = root.GaoKey,
    Links =
    [new(firstBone.GaoKey, 0, Matrix4x4.Identity), new(secondBone.GaoKey, 1, Matrix4x4.Identity)]
};
sceneObject.Flags = 0x1200000;
sceneObject.DrawFlags = 0x8008;
geometry.Skin = [new(0, Matrix4x4.CreateTranslation(-1, 0, 0), [Weight(0, .5f), Weight(1, 1)]),
    new(1, Matrix4x4.CreateTranslation(-4, 0, 0), [Weight(0, .5f), Weight(2, 1)])];
var clip = new AnimationClip { Loaded = true, Name = "Off-grid keys", Duration = .09f, Unsupported = 1, SceneMotion = true };
clip.Tracks.Add(new()
{
    Bone = -1,
    Target = firstBone.GaoKey,
    Keys =
    [new() { Time = 0, Duration = .013f, Position = new(3, 0, 0), Rotation = Quaternion.Identity, Flags = 2048 },
     new() { Time = .013f, Duration = .024f, Position = new(3, 2, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f) },
     new() { Time = .037f, Duration = .053f, Position = new(3, 4, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2) }]
});
clip.Tracks[0].ChannelFlags = 0x4013;
clip.Tracks[0].HeaderWord = 0x128;
clip.Tracks[0].Keys[1].OpaquePayloads.Add(new("word-pair-list", 123,
    [1, 0, 0, 0, 0x35, 0, 0, 0, 0x21, 0, 0, 1]));
var actor = new SceneActor
{
    Root = root,
    Members = [root, firstBone, secondBone, sceneObject],
    Bones = [firstBone, secondBone],
    Clips = [clip],
    Clip = clip,
    Loop = true,
    Playing = true,
    Time = .005f
};
foreach (var member in actor.Members) member.Actor = actor;
actor.Evaluate(0);
world.Objects.Clear(); world.Objects.AddRange(actor.Members); world.Actors.Add(actor);
string animationPath = Path.Combine(output, "weighted-animation.glb");
var result = SceneExporter.Export(animationPath, world, resolver, new() { Textures = false, Animations = true }, root);
Check(actor.Loop && actor.Playing && !actor.RootMotion && actor.Time == .005f && actor.Clip == clip,
    "Export changed preview playback state");
Check(result.Warnings.Any(warning => warning.Contains("unsupported records")), "Partial animation was not reported");
using (var glb = new GlbReader(animationPath))
{
    var samplers = glb.Root.GetProperty("animations")[0].GetProperty("samplers");
    var sourceRecords = glb.Root.GetProperty("animations")[0].GetProperty("extras").GetProperty("jadeViewSourceRecords");
    Check(!sourceRecords.GetProperty("interpreted").GetBoolean(), "Opaque events must not claim interpreted playback");
    var retainedRecord = sourceRecords.GetProperty("records")[0];
    Check(retainedRecord.GetProperty("bytesHex").GetString() == "010000003500000021000001"
        && retainedRecord.GetProperty("sourceOffset").GetInt32() == 123
        && retainedRecord.GetProperty("time").GetSingle() == .013f
        && retainedRecord.GetProperty("headerWord").GetString() == "00000128",
        "GLB lost original event bytes, timing or source identity");
    var times = glb.Accessor(samplers[0].GetProperty("input").GetInt32()).Select(value => value[0]).ToArray();
    Check(times.Contains(.013f) && times.Contains(.037f) && times.Contains(.09f), "Source key boundaries were lost");
    Check(times.Contains(MathF.BitDecrement(.013f)), "STEP transition lacks its pre-jump sample");
    Check(times.Zip(times.Skip(1)).All(pair => pair.First < pair.Second), "Animation times are not strictly increasing");
    foreach (var sampler in samplers.EnumerateArray())
        Check(glb.Accessor(sampler.GetProperty("output").GetInt32()).Length == times.Length, "Animation input/output counts differ");
    VerifySkin(glb, actor, sceneObject, geometry, [0, .013f, .037f, .09f]);
    sceneObject.DrawFlags &= ~0x8000u;
    actor.Clip = clip; actor.Loop = false; actor.Time = .09f; actor.Playing = false; actor.Evaluate(0);
    Near(firstBone.PreviewMatrix!.Value.Translation, new(11, 2, 1), "Non-looping endpoint wrapped to the first key");
}
string mirroredPath = Path.Combine(output, "mirrored-at-origin.glb");
SceneExporter.Export(mirroredPath, world, resolver, new() { Textures = false, Animations = true, Placement = 2 }, root);
using (var glb = new GlbReader(mirroredPath))
    VerifySkin(glb, actor, sceneObject, geometry, [0, .013f, .037f, .09f],
        Matrix4x4.CreateTranslation(-root.RenderMatrix.Translation));
Console.WriteLine("PASS: weighted GLB skin matches evaluated source positions, including unweighted fallback");
Console.WriteLine("PASS: mirrored geometry and actor placement at origin match evaluated source skinning");
Console.WriteLine("PASS: off-grid keys, STEP boundaries, final pose, unsupported warnings and preview-state restoration");

var alternateClip = new AnimationClip { Loaded = true, Name = "Alternate motion", Duration = .09f };
alternateClip.Tracks.Add(new()
{
    Bone = -1,
    Target = firstBone.GaoKey,
    Keys =
    [new() { Time = 0, Duration = .09f, Position = new(3, -2, 0), Rotation = Quaternion.Identity }]
});
actor.Clips.Add(alternateClip);
actor.SelectClip(clip);
actor.Time = 0; actor.Evaluate(0);
string allClipsPath = Path.Combine(output, "multiple-clips.glb");
result = SceneExporter.Export(allClipsPath, world, resolver,
    new() { Textures = false, Animations = true, AllClips = true }, root);
Check(result.Animations == 2 && actor.Clip == clip, "All-clips export lost a clip or changed the active preview clip");
using (var glb = new GlbReader(allClipsPath))
{
    Check(!glb.Root.GetProperty("animations")[1].TryGetProperty("extras", out _),
        "Source records leaked into an unrelated clip");
    for (int animationIndex = 0; animationIndex < actor.Clips.Count; animationIndex++)
    {
        actor.SelectClip(actor.Clips[animationIndex]);
        VerifySkin(glb, actor, sceneObject, geometry, [0, .09f], animationIndex: animationIndex);
    }
}
Console.WriteLine("PASS: multiple clips retain independent motion and restore the selected preview clip");
MorphPreviewChecks.Verify(actor, sceneObject, geometry, world, resolver, output);

if (args.Length > 0)
{
    VerifyGame(args[0], output);
    MorphDiscoveryChecks.VerifyPeyj(args[0], output);
}

static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
    ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resolver)!).Add(key, value);
static uint Weight(int vertex, float weight) => (BitConverter.SingleToUInt32Bits(weight) & 0xFFFF0000) | checked((ushort)vertex);
static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < .001f, $"{message}: {actual} != {expected}");

static void VerifyLargeMesh(string output)
{
    var resolver = new ResourceResolver();
    var geometry = new Geometry { Key = 300, Positions = new Vector3[65536] };
    int[] corners = Enumerable.Range(1, 65534).SelectMany(index => new[] { 0, index, index + 1 }).ToArray();
    geometry.Parts.Add(new(0, corners, []));
    Register(resolver, "_geometries", geometry.Key, geometry);
    var world = new WorldData();
    world.Objects.Add(new() { Name = "Large mesh", GeometryKey = geometry.Key });
    string path = Path.Combine(output, "large-mesh.glb");
    SceneExporter.Export(path, world, resolver, new() { Textures = false });
    using var glb = new GlbReader(path);
    var primitive = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var accessor = glb.Root.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()];
    Check(accessor.GetProperty("componentType").GetInt32() == 5125, "Large mesh must use uint indices");
    Check(glb.Accessor(primitive.GetProperty("indices").GetInt32())[^1][0] == 65535, "Large mesh index overflow");
}

