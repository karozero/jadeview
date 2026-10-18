using System.Numerics;
using System.Globalization;
using System.Text;

namespace JadeView;

public sealed record JadeMaterial(uint Key, int Offset, uint Diffuse, uint[] Textures, uint[] Children, int Type = 0, byte[]? Bytes = null);
