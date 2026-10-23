namespace JadeView;

internal static partial class PcCollisionResourceReader
{
    private static bool IsSupportedDialect(PcCollisionWireDialect dialect) =>
        dialect is PcCollisionWireDialect.JadedPc or PcCollisionWireDialect.IrisCompact;

    private static bool IsSupportedMode(PcCollisionReadMode mode) =>
        mode is PcCollisionReadMode.EditorRich or PcCollisionReadMode.BinaryCompact;

    private static PcCollisionDecodeResult<T> Success<T>(T value, string[]? warnings = null) where T : class =>
        new(PcCollisionDecodeStatus.Success, value, "", warnings ?? []);

    private static PcCollisionDecodeResult<T> Truncated<T>(string message) where T : class =>
        new(PcCollisionDecodeStatus.Truncated, null, message, []);

    private static PcCollisionDecodeResult<T> Invalid<T>(string message) where T : class =>
        new(PcCollisionDecodeStatus.InvalidData, null, message, []);

    private static PcCollisionDecodeResult<T> UnsupportedVersion<T>(string message) where T : class =>
        new(PcCollisionDecodeStatus.UnsupportedVersion, null, message, []);

    private static PcCollisionDecodeResult<T> DecodeFailure<T>(Cursor cursor, string message)
        where T : class
    {
        if (cursor.Truncated)
            return Truncated<T>(message);
        return Invalid<T>(message);
    }

}
