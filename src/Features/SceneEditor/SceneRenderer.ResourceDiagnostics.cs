namespace JadeView;

public sealed partial class SceneRenderer
{
    public float TotalShaderInitializationMilliseconds { get; private set; }
    public float StatDiffuseLightingPlanMilliseconds { get; private set; }
    public float StatDiffuseLightingEvaluationMilliseconds { get; private set; }
    public float StatDiffuseLightingUploadMilliseconds { get; private set; }
    public float StatTextureDecodeMilliseconds { get; private set; }
    public float StatTextureUploadMilliseconds { get; private set; }
    public float StatMaterialPreparationMilliseconds { get; private set; }
    public float StatMaterialSourceResolutionMilliseconds { get; private set; }
    public float StatMaterialAllocationMilliseconds { get; private set; }
    public int StatTextureUploadsThisFrame { get; private set; }
    public int StatMaterialSetsThisFrame { get; private set; }
    public int StatMaterialPassesThisFrame { get; private set; }

    private void ResetResourceDiagnostics()
    {
        StatDiffuseLightingPlanMilliseconds = 0;
        StatDiffuseLightingEvaluationMilliseconds = 0;
        StatDiffuseLightingUploadMilliseconds = 0;
        StatTextureDecodeMilliseconds = 0;
        StatTextureUploadMilliseconds = 0;
        StatMaterialPreparationMilliseconds = 0;
        StatMaterialSourceResolutionMilliseconds = 0;
        StatMaterialAllocationMilliseconds = 0;
        StatTextureUploadsThisFrame = 0;
        StatMaterialSetsThisFrame = 0;
        StatMaterialPassesThisFrame = 0;
    }
}
