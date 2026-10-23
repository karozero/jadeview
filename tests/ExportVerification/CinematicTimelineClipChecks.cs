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

        var sceneTracks = CinematicTimelineClipDiscovery.FindSceneTracks(
            [sceneSequenceOwner, actorAnimationOwner, otherWorldOwner, invalidLinkOwner],
            new HashSet<uint> { cinematicWorld });
        if (!sceneTracks.SetEquals([(cinematicWorld, 0x200u)]))
            throw new InvalidDataException("The cutscene picker included ordinary Events or a track from another world.");

        var actorTracks = CinematicActorClipDiscovery.FindActorEventClips(
            [sceneSequenceOwner, actorAnimationOwner, otherWorldOwner, invalidLinkOwner,
             new PcSceneLoader.Record(0x202, 'i', 0, 0) { LoadWorldKey = cinematicWorld },
             new PcSceneLoader.Record(0x203, 'i', 0, 0) { LoadWorldKey = cinematicWorld },
             new PcSceneLoader.Record(0x204, 'i', 0, 0) { LoadWorldKey = cinematicWorld }],
            new HashSet<uint> { cinematicWorld }, new HashSet<uint> { actorAnimationOwner.Key });
        if (actorTracks is not [{ Key: 0x202 }])
            throw new InvalidDataException("Actor clip discovery included unlinked cinematic i-records or a scene-level SceneEvents clip.");

        Console.WriteLine("PASS: cinematic and actor clip discovery follows SceneEvents and owner Events links");
    }
}
