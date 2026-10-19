namespace JadeView;

public sealed class SceneObjectPresenceCommand : IEditCommand
{
    private readonly WorldData _world;
    private readonly SceneObject _target;
    private readonly int _index;
    private readonly bool _shouldExist;
    private readonly Action? _afterChange;

    public bool ShouldExist => _shouldExist;
    public WorldData World => _world;
    public SceneObject Target => _target;
    public string Description => _shouldExist ? $"Add { _target.Name }" : $"Delete { _target.Name }";

    public SceneObjectPresenceCommand(WorldData world, SceneObject target, bool shouldExist, int? index = null, Action? afterChange = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _shouldExist = shouldExist;
        _index = Math.Clamp(index ?? world.Objects.Count, 0, world.Objects.Count);
        _afterChange = afterChange;
    }

    public void Execute() => SetPresence(_shouldExist);
    public void Undo() => SetPresence(!_shouldExist);

    private void SetPresence(bool exists)
    {
        int current = _world.Objects.IndexOf(_target);
        if (exists)
        {
            if (current < 0) _world.Objects.Insert(Math.Clamp(_index, 0, _world.Objects.Count), _target);
        }
        else if (current >= 0)
        {
            _world.Objects.RemoveAt(current);
        }
        _afterChange?.Invoke();
    }
}
