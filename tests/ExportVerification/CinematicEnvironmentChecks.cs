using System.Text;
using JadeView;

internal static class CinematicEnvironmentChecks
{
    internal static void Verify()
    {
        var model = Variable("des_o_sky", 7);
        Check(Variable("other", 99), 7);
        Check(Variable("des_o_sky", 9), 9);
        Check(Variable("des_o_sky", 0), 0); // Explicit null must not inherit the model.
        Reject(Variable("des_o_sky", 9, 34));
        var outside = Variable("des_o_sky", 9);
        BitConverter.GetBytes(4u).CopyTo(outside, 4);
        Reject(outside);
        Reject(Variable("des_o_sky", 9)[..^1]);
        try
        {
            PcObjectVariableReader.ReadOverlay(Variable("des_o_sky", 7, 34), Variable("des_o_sky", 9), "des_o_sky");
            throw new InvalidOperationException("An instance reference hid incompatible model storage.");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: typed environment object references, model/instance overlay, explicit null and malformed storage rejection");

        void Check(byte[] instance, uint expected)
        {
            if (PcObjectVariableReader.ReadOverlay(model, instance, "des_o_sky") != expected)
                throw new InvalidDataException("Environment object overlay lost source reference precedence.");
        }
        void Reject(byte[] instance)
        {
            try { PcObjectVariableReader.ReadOverlay(model, instance, "des_o_sky"); }
            catch (InvalidDataException) { return; }
            throw new InvalidDataException("Invalid environment object variable was accepted.");
        }
    }

    private static byte[] Variable(string name, uint key, ushort type = 40)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(12); writer.Write(0u); writer.Write(1u); writer.Write(type); writer.Write((ushort)0);
        writer.Write(30);
        byte[] encoded = new byte[30]; Encoding.Latin1.GetBytes(name).CopyTo(encoded, 0); writer.Write(encoded);
        writer.Write(4); writer.Write(key);
        return stream.ToArray();
    }
}
