namespace JadeView;

public sealed partial class FaceMorphPreview
{
    // Samples a copy starting at the current transient pose, without authoring keys.
    public FaceMorphKeyframe[] SampleMouthCycle(float duration)
    {
        if (mouth == null) throw new InvalidOperationException("Start the experimental mouth preview before sampling it.");
        return FaceMorphMouthCycleSampler.Sample(mouth, weights, duration);
    }
}
