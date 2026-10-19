using System.Security.Cryptography;

namespace JadeView;

internal static class PcPickupFlagProfile
{
    // Audited PC 2003 executable. Its global SF bitset contains 192 words (6144 bits).
    // These tail indices have no literal representation anywhere in its unpacked code.
    // Archive variables are checked separately; this is not a profile for other releases.
    // TODO: Revisit this hardcoded candidate pool before treating pickup persistence as verified.
    // Absence of literal indices does not prove that these bits are unused: native code can
    // compute indices at runtime. Trace SF access and save/load behavior, test scene re-entry
    // and story progression, then replace or substantiate this allocation policy.
    // Any revision must preserve existing .bgemod assignments and their saved-game meaning.
    internal static readonly int[] Candidates =
    [
        6139, 6138, 6137, 6136, 6135, 6133, 6132, 6131, 6127, 6126,
        6125, 6123, 6122, 6121, 6117, 6115, 6114, 6113, 6112, 6111,
        6110, 6108, 6106, 6105, 6099, 6097, 6094, 6093, 6092, 6090,
        6089, 6088, 6081, 6079, 6078, 6077, 6076, 6072, 6071, 6070,
        6067, 6066, 6065, 6063, 6062, 6061, 6060, 6059, 6058, 6057,
        6056, 6055, 6054, 6053, 6052, 6051, 6050, 6048, 6047, 6045,
        6044, 6043, 6042, 6041, 6039, 6038, 6037, 6036, 6035, 6033,
        6031, 6030, 6029, 6027, 6026, 6024, 6023, 6022, 6021, 6020,
        6017, 6015, 6013, 6012, 6011, 6010, 6009, 6008, 6007, 6006
    ];

    internal static void VerifyExecutable(string archivePath)
    {
        string executable = Path.Combine(Path.GetDirectoryName(archivePath)!, "BGE.exe");
        if (!File.Exists(executable))
            throw new InvalidDataException("Place the original PC BGE.exe next to the source BF to verify duplicated pickup persistence.");
        using var stream = File.OpenRead(executable);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        if (hash != "70AE232D58BAFE5CBF5C27712538AF91E8B13A98104AE1BF6AEA6DCF3506F5DB")
            throw new InvalidDataException("This BGE.exe has no audited pickup save-flag profile. Persistent pickups cannot be duplicated safely for this executable yet.");
    }
}
