namespace JadeView;
internal sealed record PcSceneSource(byte[] Bytes, PcSceneLoader.Result Replay)
{
    internal byte[] Frame(PcSceneLoader.Record record) => Bytes.AsSpan(record.FrameRange.Offset, record.FrameRange.Size).ToArray();
}
