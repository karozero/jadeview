using System.Numerics;
using System.Globalization;
using System.Text;

namespace JadeView;

public sealed record GeometryBillboard(int Vertex, Vector3 Center, float HalfWidth, float HalfHeight, bool Wind);
