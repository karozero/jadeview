using System.Reflection;
using System.Text.Json;

namespace JadeView;

internal static class CinematicScriptGlobalQueueCatalog
{
    private const string ResourceName = "JadeView.CinematicScriptGlobalMessageQueue";
    private static readonly Lazy<CinematicScriptGlobalQueueRegistration> Startup = new(ReadStartup);

    internal static CinematicScriptGlobalQueueRegistration StartupRegistration => Startup.Value;

    private static CinematicScriptGlobalQueueRegistration ReadStartup()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded global message-queue catalog '{ResourceName}' is missing.");
        var registration = JsonSerializer.Deserialize<CinematicScriptGlobalQueueRegistration>(stream)
            ?? throw new InvalidDataException("Embedded global message-queue catalog is empty.");
        if (!uint.TryParse(registration.StartupOwnerKeyHex,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("Global message-queue catalog has an invalid startup owner key.");
        return registration;
    }
}

internal sealed record CinematicScriptGlobalQueueRegistration(
    string QueueSymbol,
    string RegistrationApi,
    string StartupOwnerName,
    string StartupOwnerKeyHex,
    string StartupSource,
    int StartupLine,
    string WorldTeardownSource,
    int WorldTeardownLine)
{
    internal uint StartupOwnerKey => uint.Parse(StartupOwnerKeyHex,
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);
}
