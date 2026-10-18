using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

/// <summary>
/// Флаги типа матрицы трансформации Jade (MATH_tdst_Matrix.lType).
/// Согласованы с reference-исходниками Jaded (SDK/Sources/MATHs/MATHstruct.h).
/// </summary>
[Flags]
public enum JadeMatrixType : int
{
    Identity = 0,
    Translation = 2,      // MATH_Ci_Translation: матрица содержит перенос
    Rotation = 4,         // MATH_Ci_Rotation: матрица содержит поворот (не единичная 3x3)
    Scale = 8,            // MATH_Ci_Scale: матрица содержит масштабирование по локальным осям
    OScale = 16,          // MATH_Ci_OScale: ориентированное масштабирование
    AnyScale = 32,        // MATH_Ci_AnyScale: произвольное масштабирование
    Complex = Scale | Rotation,
    AnyFormOfScale = Scale | OScale | AnyScale,
    AnyFlag = AnyFormOfScale | Rotation | Translation
}

