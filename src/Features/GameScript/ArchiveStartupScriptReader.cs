using System.Buffers.Binary;
using System.Text;

namespace JadeView;

/// <summary>
/// Follows the serialized BIG start-key through the global AI instance and model.
/// This inventories resource links; it does not execute the AI or predict world state.
/// </summary>
internal static class ArchiveStartupScriptReader
{
    private const int InstanceReferenceBytes = sizeof(uint) * 2;
    private const int ModelReferenceBytes = sizeof(uint) + 4;
    private const int MaximumModelResources = 65_536;
    private const int MaximumStartupResourceBytes = 16 * 1024 * 1024;

    internal static ArchiveStartupScriptInventory Read(Archive archive)
    {
        Entry? instance = ResolveUnique(archive, archive.StartKey, out string instanceStatus);
        if (instance == null)
            return Empty(archive.StartKey, instanceStatus,
                $"BIG startKey {archive.StartKey:X8} does not resolve to a unique archive entry.");

        byte[] instanceBytes;
        try
        {
            instanceBytes = archive.Read(instance, MaximumStartupResourceBytes);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return Empty(archive.StartKey, "InvalidInstancePayload", exception.Message, instance.Name);
        }

        if (instanceBytes.Length < InstanceReferenceBytes)
            return Empty(archive.StartKey, "InvalidInstancePayload",
                "Startup AI instance does not contain the model and instance-variable key pair.", instance.Name);

        uint modelKey = BinaryPrimitives.ReadUInt32LittleEndian(instanceBytes);
        uint instanceVariablesKey = BinaryPrimitives.ReadUInt32LittleEndian(instanceBytes.AsSpan(sizeof(uint)));
        Entry? model = ResolveUnique(archive, modelKey, out string modelStatus);
        Entry? instanceVariables = ResolveUnique(archive, instanceVariablesKey, out string variablesStatus);
        if (model == null)
        {
            return new(archive.StartKey, "ResolvedInstance", instance.Name, modelKey, null,
                instanceVariablesKey, instanceVariables?.Name, [],
                $"Global AI model {modelKey:X8} is {modelStatus}; instance variables are {variablesStatus}.");
        }

        byte[] modelBytes;
        try
        {
            modelBytes = archive.Read(model, MaximumStartupResourceBytes);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return new(archive.StartKey, "ResolvedInstance", instance.Name, modelKey, model.Name,
                instanceVariablesKey, instanceVariables?.Name, [],
                $"Could not read global AI model: {exception.Message}");
        }

        int referenceCount = modelBytes.Length / ModelReferenceBytes;
        if (referenceCount > MaximumModelResources)
        {
            return new(archive.StartKey, "ResolvedInstance", instance.Name, modelKey, model.Name,
                instanceVariablesKey, instanceVariables?.Name, [],
                $"Global AI model declares {referenceCount} resources, above the supported bound.");
        }

        var resources = new ArchiveScriptResourceReference[referenceCount];
        for (int index = 0; index < referenceCount; index++)
        {
            int offset = index * ModelReferenceBytes;
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(modelBytes.AsSpan(offset));
            string typeTag = Encoding.ASCII.GetString(modelBytes, offset + sizeof(uint), 4);
            Entry? resource = ResolveUnique(archive, key, out string resourceStatus);
            resources[index] = new(key, typeTag, resourceStatus, resource?.Name);
        }

        int trailingBytes = modelBytes.Length % ModelReferenceBytes;
        string staticLinkNote = trailingBytes == 0
            ? "Resource links are static and callbacks are not executed."
            : $"Parsed {referenceCount} complete 8-byte model references; retained but did not interpret {trailingBytes} trailing byte(s), matching Iris's size >> 3 iteration. Callbacks are not executed.";
        string? diagnostic = instanceVariables == null
            ? $"Global AI instance variables {instanceVariablesKey:X8} are {variablesStatus}. {staticLinkNote}"
            : staticLinkNote;
        return new(archive.StartKey, "Resolved", instance.Name, modelKey, model.Name,
            instanceVariablesKey, instanceVariables?.Name, resources, diagnostic);
    }

    private static Entry? ResolveUnique(Archive archive, uint key, out string status)
    {
        if (key is 0 or uint.MaxValue)
        {
            status = "NullReference";
            return null;
        }

        Entry[] matches = archive.Entries.Where(entry => entry.Key == key).Take(2).ToArray();
        if (matches.Length == 1)
        {
            status = "Resolved";
            return matches[0];
        }

        status = matches.Length == 0 ? "MissingResource" : "AmbiguousResource";
        return null;
    }

    private static ArchiveStartupScriptInventory Empty(uint startKey, string status, string diagnostic,
        string? instanceName = null) => new(startKey, status, instanceName, null, null, null, null, [], diagnostic);
}
