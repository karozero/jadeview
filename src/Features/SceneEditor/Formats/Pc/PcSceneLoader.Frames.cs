namespace JadeView;

internal sealed partial class PcSceneLoader
{
    private void AppendSourceFrame(Request request)
    {
        if (frameSource == null)
            return;
        if (cursor != dataLength)
            throw new InvalidDataException("Rebuild source supplied an incorrectly framed record.");
        byte[] frame = frameSource(request, loadingWorldKey);
        int required = checked(dataLength + frame.Length);
        if (required > dataBytes.Length)
            Array.Resize(ref dataBytes, Math.Max(required, checked(dataBytes.Length * 2)));
        frame.CopyTo(dataBytes, dataLength);
        dataLength = required;
    }

    private void ReadShapeRecord(Request request)
    {
        int start = cursor;
        string tag = ReadText(start, 4);
        int shapeSize = ReadInt32(start + 4);
        cursor = checked(start + 8 + shapeSize);
        if (tag == ".snk")
        {
            int listSize = ReadInt32(cursor);
            cursor = checked(cursor + 4 + listSize);
        }

        if (cursor > dataLength)
            throw new InvalidDataException("Shape outside stream.");
        var shape = new Record(request.Key, 'u', start + 8, cursor - start - 8)
        {
            FrameRange = new(start, cursor - start),
            LoadWorldKey = loadingWorldKey
        };
        result.Records.Add(shape);
        cache[request.Key] = shape;
        result.Resources[request.Key] = shape;
    }

    private void ReadFramedRecord(Request request)
    {
        int start = cursor;
        int size = ReadInt32(start), offset = start + 4;
        if (size < 0 || size > dataLength - offset)
            throw new InvalidDataException($"Bad {request.Kind} size at {start:X8}, key {request.Key:X8}.");
        var record = new Record(request.Key, request.Kind, offset, size)
        {
            FrameRange = new(start, size + 4),
            LoadWorldKey = loadingWorldKey
        };
        currentRecord = record;
        result.Records.Add(record);
        ReadResource(record);
        cursor = checked(offset + record.Size);
        currentRecord = null;
        if (request.Cache)
            cache[request.Key] = record;
        result.Resources[request.Key] = record;
    }
}
