using System.Buffers.Binary;
using System.Text;

namespace JadeView;

// Independent, bounded LZO1X v0 stream reader. New BIG records use the format's RAW-block mode.
static class Lzo
{
    public static byte[] Decode(byte[] input, int length)
    {
        byte[] output = new byte[length];
        int ip = 0, op = 0, state = 0;
        int Byte() { if (ip >= input.Length) throw new InvalidDataException("Truncated LZO."); return input[ip++]; }
        int Extended(int value, int mask)
        { if (value != 0) return value; int n = mask, b; while ((b = Byte()) == 0) n = checked(n + 255); return checked(n + b); }
        void Literals(int n)
        { if (n > input.Length - ip || n > output.Length - op) throw new InvalidDataException("LZO literal outside bounds."); input.AsSpan(ip, n).CopyTo(output.AsSpan(op)); ip += n; op += n; }
        if (input.Length == 0) throw new InvalidDataException("Empty LZO.");
        if (input[0] > 17) { state = Byte() - 17; Literals(state); state = Math.Min(4, state); }
        while (true)
        {
            int code = Byte(), size, distance, trailing;
            if (code < 16 && state == 0)
            { size = Extended(code, 15) + 3; Literals(size); state = 4; continue; }
            if (code < 16)
            { size = state == 4 ? 3 : 2; distance = 1 + (code >> 2) + (Byte() << 2) + (state == 4 ? 2048 : 0); trailing = code & 3; }
            else if (code < 32)
            {
                size = Extended(code & 7, 7) + 2;
                int word = Byte() | Byte() << 8;
                distance = ((code & 8) << 11) + (word >> 2);
                if (distance == 0)
                { if (op != length || ip != input.Length || size != 3) throw new InvalidDataException("Invalid LZO end marker."); return output; }
                distance += 16384; trailing = word & 3;
            }
            else if (code < 64)
            { size = Extended(code & 31, 31) + 2; int word = Byte() | Byte() << 8; distance = 1 + (word >> 2); trailing = word & 3; }
            else
            { size = (code >> 5) + 1; distance = 1 + ((code >> 2) & 7) + (Byte() << 3); trailing = code & 3; }
            if (distance > op || distance <= 0 || size > output.Length - op) throw new InvalidDataException("LZO match outside bounds.");
            for (int i = 0; i < size; i++) { output[op] = output[op - distance]; op++; }
            Literals(trailing); state = trailing;
        }
    }
}
