namespace JadeView;

public sealed partial class EditorShell
{
    private void RecordMorphProjectEdit(SceneActor actor, FaceMorphPreview preview)
    {
        var member = actor.Members.FirstOrDefault(member => ReferenceEquals(member.MorphPreview, preview));
        if (Document == null || CurrentProject == null || member == null) return;
        CurrentProject.CaptureMorphPreview(Document, actor, member, preview);
        History.MarkExternalChange();
    }
}
