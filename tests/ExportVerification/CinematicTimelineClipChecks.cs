using JadeView;

internal static class CinematicTimelineClipChecks
{
    internal static void Verify()
    {
        const uint cinematicWorld = 0x1000;
        var sceneSequenceOwner = new PcSceneLoader.Record(1, 'l', 0, 0)
        {
            LoadWorldKey = cinematicWorld,
            SceneEvents = 0x200,
            Events = 0x201
        };
        var actorAnimationOwner = new PcSceneLoader.Record(2, 'l', 0, 0)
        {
            LoadWorldKey = cinematicWorld,
            Events = 0x202
        };
        var otherWorldOwner = new PcSceneLoader.Record(3, 'l', 0, 0)
        {
            LoadWorldKey = cinematicWorld + 1,
            SceneEvents = 0x203
        };
        var invalidLinkOwner = new PcSceneLoader.Record(4, 'l', 0, 0)
        {
            LoadWorldKey = cinematicWorld,
            SceneEvents = uint.MaxValue
        };
        var pairedWorldActorOwner = new PcSceneLoader.Record(actorAnimationOwner.Key, 'l', 0, 0)
        {
            LoadWorldKey = cinematicWorld + 1,
            Events = 0x205
        };

        var sceneTracks = CinematicTimelineClipDiscovery.FindSceneTracks(
            [sceneSequenceOwner, actorAnimationOwner, otherWorldOwner, invalidLinkOwner],
            new HashSet<uint> { cinematicWorld });
        if (!sceneTracks.SetEquals([(cinematicWorld, 0x200u)]))
            throw new InvalidDataException("The cutscene picker included ordinary Events or a track from another world.");

        var actorTracks = CinematicActorClipDiscovery.FindActorClips(
            [sceneSequenceOwner, actorAnimationOwner, otherWorldOwner, invalidLinkOwner,
             new PcSceneLoader.Record(0x200, 'i', 0, 0) { LoadWorldKey = cinematicWorld },
             new PcSceneLoader.Record(0x202, 'i', 0, 0) { LoadWorldKey = cinematicWorld },
             new PcSceneLoader.Record(0x203, 'i', 0, 0) { LoadWorldKey = cinematicWorld },
             new PcSceneLoader.Record(0x204, 'i', 0, 0) { LoadWorldKey = cinematicWorld },
             pairedWorldActorOwner,
             new PcSceneLoader.Record(0x205, 'i', 0, 0) { LoadWorldKey = cinematicWorld + 1 }],
            new HashSet<uint> { cinematicWorld }, new HashSet<uint> { actorAnimationOwner.Key });
        if (!actorTracks.Select(record => record.Key).ToHashSet().SetEquals([0x200u, 0x202u, 0x205u]))
            throw new InvalidDataException("Actor clip discovery must combine owner Events and root SceneEvents while excluding unlinked cinematic i-records.");

        Console.WriteLine("PASS: cinematic and actor clip discovery follows SceneEvents and owner Events links");
    }
}
