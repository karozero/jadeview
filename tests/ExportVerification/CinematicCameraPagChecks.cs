using System.Numerics;
using JadeView;

internal static class CinematicCameraPagChecks
{
    internal static void Verify()
    {
        var ownerObject = new SceneObject { GaoKey = 10, Name = "Script owner" };
        ownerObject.InitialCapabilities = 1;
        var firstEffect = CreateEffect(20, "First PAG");
        var secondEffect = CreateEffect(21, "Second PAG");
        var world = new WorldData { Key = 100, Name = "test_cine" };
        world.Objects.AddRange([ownerObject, firstEffect, secondEffect]);

        var owner = new CinematicScriptOwner(10, 11, 12, world.Key, uint.MaxValue,
            uint.MaxValue, "Script owner", ["SFX_colpag_cam_scp_loop"],
            [new("des_o_pag1", firstEffect.GaoKey, "instance", firstEffect.Name),
             new("des_o_pag2", secondEffect.GaoKey, "instance", secondEffect.Name)],
            [new("f_time", PcVariableFormat.FloatType, "model", "0"),
             new("f_rand", PcVariableFormat.FloatType, "model", "2"),
             new("i_mode", PcVariableFormat.IntegerType, "model", "0")]);
        var calls = new (float Time, CinematicLiteralCall Call)[]
        {
            (2, new(ownerObject.GaoKey, 8133, [0, 1])),
            (4, new(ownerObject.GaoKey, 8133, [1, 0]))
        };

        var controllers = CinematicCameraPagControllerReader.Read([owner], world);
        if (controllers.Length != 1 || controllers[0].FirstEffect != firstEffect
            || controllers[0].SecondEffect != secondEffect)
            throw new InvalidDataException("The callback controller did not resolve its OVA effect carriers.");

        var timeline = new CinematicCameraPagTimeline([owner], world, calls);
        Matrix4x4 cameraPose = Matrix4x4.Identity;
        timeline.Evaluate(1, cameraPose);
        VerifyEmission(firstEffect, 50);
        VerifyEmission(secondEffect, 20);
        VerifyVisible(firstEffect);
        Vector3 firstModePosition = firstEffect.PreviewMatrix!.Value.Translation;
        if (Vector3.Distance(firstModePosition, new(-6, -5, 2)) > .001f)
            throw new InvalidDataException("The PAG callback did not place its first mode from camera sight/horizon/banking axes.");

        timeline.Evaluate(2.5f, cameraPose);
        VerifyEmission(firstEffect, 0);
        VerifyEmission(secondEffect, 0);
        timeline.Evaluate(4.25f, cameraPose);
        VerifyEmission(firstEffect, 50);
        Vector3 reactivatedPosition = firstEffect.PreviewMatrix!.Value.Translation;
        if (Vector3.Distance(reactivatedPosition, new(6, -5, 2)) > .001f)
            throw new InvalidDataException("The PAG callback did not retain its timed mode across capability deactivation.");

        timeline.Evaluate(1, cameraPose);
        Vector3 replayedPosition = firstEffect.PreviewMatrix!.Value.Translation;
        if (Vector3.Distance(replayedPosition, firstModePosition) > .001f)
            throw new InvalidDataException("Seeking backward did not deterministically reset the callback mode.");

        for (int iteration = 0; iteration < 32; iteration++) timeline.Evaluate(1, cameraPose);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1024; iteration++) timeline.Evaluate(1, cameraPose);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0)
            throw new InvalidDataException($"Warmed PAG callback evaluation allocated {allocated} bytes.");

        Matrix4x4 alternatePose = Matrix4x4.CreateTranslation(10, 3, -4);
        timeline.Evaluate(1, alternatePose);
        if (Vector3.Distance(firstEffect.PreviewMatrix!.Value.Translation, new(4, -2, -2)) > .001f)
            throw new InvalidDataException("Camera-following PAG did not use the active preview camera pose.");

        timeline.Evaluate(1, null);
        VerifyEmission(firstEffect, 50);
        Vector3 retainedPosition = firstEffect.PreviewMatrix!.Value.Translation;
        if (Vector3.Distance(retainedPosition, new(4, -2, -2)) > .001f)
            throw new InvalidDataException("Missing active preview camera should not reset an already evaluated PAG pose.");

        Console.WriteLine("PASS: script-driven camera PAG binding, capability gate, deterministic mode timing and emission rates");
    }

    private static SceneObject CreateEffect(uint key, string name)
    {
        var effect = new SceneObject { GaoKey = key, Name = name, ControlFlags = CinematicObjectState.Invisible };
        effect.CinematicParticles = new() { Values = new float[19] };
        return effect;
    }

    private static void VerifyEmission(SceneObject effect, float expectedFrequency)
    {
        if (effect.CinematicParticles?.Values[4] != expectedFrequency)
            throw new InvalidDataException($"PAG generator frequency was {effect.CinematicParticles?.Values[4]}, expected {expectedFrequency}.");
    }

    private static void VerifyVisible(SceneObject effect)
    {
        if (effect.IsControlHidden)
            throw new InvalidDataException("An active camera-following PAG carrier stayed force-invisible.");
    }
}
