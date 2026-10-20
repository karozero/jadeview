using System.Numerics;

namespace JadeView;

internal static partial class ActorLoader
{
    private static ActorNode Node(byte[] bytes, SceneObject sceneObject)
    {
        var actorNode = new ActorNode
        {
            Local = AnimationMath.Matrix(bytes, sceneObject.RecordRange.Offset + 4 + 22)
        };
        int recordStart = sceneObject.RecordRange.Offset + 4;
        int actorDataOffset = recordStart + 90 + ((sceneObject.Flags & 0x80000) != 0 ? 48 : 24);

        if ((sceneObject.Flags & 0x1000) == 0)
        {
            return actorNode;
        }

        if ((sceneObject.Flags & 0x4000) != 0)
        {
            int extraDataCount = Preview.I32(bytes, actorDataOffset + 16);
            actorDataOffset += 20 + extraDataCount * 4;
        }

        if ((sceneObject.Flags & 0x400000) != 0)
        {
            actorNode.Parent = Preview.U32(bytes, actorDataOffset);
            actorNode.Local = AnimationMath.Matrix(bytes, actorDataOffset + 4);
            actorDataOffset += 72;
        }

        if ((sceneObject.Flags & 2) != 0)
        {
            actorDataOffset += 16;
        }

        if ((sceneObject.Flags & 0x200000) == 0)
        {
            return actorNode;
        }

        int paletteCount = Preview.I32(bytes, actorDataOffset);
        actorDataOffset += 4;
        if (paletteCount < 0 || paletteCount > 512)
        {
            throw new InvalidDataException("Invalid actor bone palette.");
        }

        if ((sceneObject.Flags & 0x1000000) != 0)
        {
            actorNode.Links = ReadBoneLinks(bytes, actorDataOffset, paletteCount);
        }
        else
        {
            actorNode.Matrices = ReadBoneMatrices(bytes, actorDataOffset, paletteCount);
        }

        return actorNode;
    }

    private static ActorBoneLink[] ReadBoneLinks(byte[] bytes, int offset, int count)
    {
        var links = new ActorBoneLink[count];
        for (int linkIndex = 0; linkIndex < count; linkIndex++)
        {
            int linkOffset = offset + linkIndex * 8;
            links[linkIndex] = new(Preview.U32(bytes, linkOffset), Preview.I32(bytes, linkOffset + 4),
                Matrix4x4.Identity);
        }

        return links;
    }

    private static Matrix4x4[] ReadBoneMatrices(byte[] bytes, int offset, int count)
    {
        var matrices = new Matrix4x4[count];
        for (int matrixIndex = 0; matrixIndex < count; matrixIndex++)
        {
            int matrixOffset = offset + matrixIndex * 68;
            matrices[matrixIndex] = AnimationMath.Matrix(bytes, matrixOffset);
        }

        return matrices;
    }
}
