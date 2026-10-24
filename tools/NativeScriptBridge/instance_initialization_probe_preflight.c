#include "instance_initialization_probe.h"

static uint32_t read_word(const unsigned char* bytes, unsigned int offset)
{
    uint32_t value;
    memcpy(&value, bytes + offset, sizeof(value));
    return value;
}

static uint16_t read_half(const unsigned char* bytes, unsigned int offset)
{
    uint16_t value;
    memcpy(&value, bytes + offset, sizeof(value));
    return value;
}

static int reject(char* diagnostic, size_t capacity, const char* message)
{
    if (diagnostic && capacity)
        sprintf_s(diagnostic, capacity, "%s", message);
    return 0;
}

int init_probe_validate_ova(const InitProbeResource* resource, int isInstanceOva,
    uint32_t expectedFunctionKey, char* diagnostic, size_t diagnosticCapacity)
{
    unsigned int descriptorByteCount;
    unsigned int variableCount;
    unsigned int namesOffset;
    unsigned int namesByteCount;
    unsigned int storageLengthOffset;
    unsigned int storageLength;
    unsigned int storageOffset;
    unsigned int callbacksOffset;
    unsigned int trailingByteCount;
    unsigned int index;

    if (!resource || !resource->bytes || resource->byteCount < 32)
        return reject(diagnostic, diagnosticCapacity, "OVA payload is too short");

    descriptorByteCount = read_word(resource->bytes, 0);
    if ((descriptorByteCount % sizeof(AIVarElem)) != 0 || descriptorByteCount > 4096u * sizeof(AIVarElem))
        return reject(diagnostic, diagnosticCapacity, "OVA descriptor block is malformed or too large");
    variableCount = descriptorByteCount / sizeof(AIVarElem);
    if (descriptorByteCount > (unsigned int)resource->byteCount - 8)
        return reject(diagnostic, diagnosticCapacity, "OVA descriptors are truncated");

    namesOffset = 4 + descriptorByteCount;
    namesByteCount = read_word(resource->bytes, namesOffset);
    if (namesByteCount != variableCount * sizeof(AIVarNameElem) ||
        namesByteCount > (unsigned int)resource->byteCount - namesOffset - 8)
        return reject(diagnostic, diagnosticCapacity, "OVA names do not match descriptor count");

    storageLengthOffset = namesOffset + 4 + namesByteCount;
    storageLength = read_word(resource->bytes, storageLengthOffset);
    storageOffset = storageLengthOffset + 4;
    if (storageLength > (unsigned int)resource->byteCount - storageOffset ||
        storageLength > 1024u * 1024u)
        return reject(diagnostic, diagnosticCapacity, "OVA defaults are truncated or exceed the bounded limit");
    callbacksOffset = storageOffset + storageLength;
    trailingByteCount = (unsigned int)resource->byteCount - callbacksOffset;
    if (trailingByteCount != 5u * sizeof(uint32_t) && trailingByteCount != 6u * sizeof(uint32_t))
        return reject(diagnostic, diagnosticCapacity, "OVA callback trailer or instance-function data is unsupported");
    if (trailingByteCount == 6u * sizeof(uint32_t) && read_word(resource->bytes, callbacksOffset + 5u * sizeof(uint32_t)) != 0)
        return reject(diagnostic, diagnosticCapacity, "nonempty instance-function list is outside this proof");

    if (!isInstanceOva && variableCount == 0)
        return reject(diagnostic, diagnosticCapacity, "fixture model OVA has no typed defaults");

    for (index = 0; index < variableCount; ++index)
    {
        unsigned int descriptorOffset = 4 + index * (unsigned int)sizeof(AIVarElem);
        int32_t variableOffset;
        uint32_t arrayCount;
        uint16_t typeId;
        uint32_t value;

        memcpy(&variableOffset, resource->bytes + descriptorOffset, sizeof(variableOffset));
        arrayCount = read_word(resource->bytes, descriptorOffset + 4);
        typeId = read_half(resource->bytes, descriptorOffset + 8);
        if ((typeId != 33 && typeId != 40) || arrayCount != 1 || variableOffset < 0 ||
            (unsigned int)variableOffset > storageLength || sizeof(value) > storageLength - (unsigned int)variableOffset)
            return reject(diagnostic, diagnosticCapacity, "fixture OVA contains an unsupported type, array, or offset");

        value = read_word(resource->bytes, storageOffset + (unsigned int)variableOffset);
        if (typeId == 40 && value != 0)
            return reject(diagnostic, diagnosticCapacity, "UNSUPPORTED_NONNULL_GAO_REFERENCE");
    }

    for (index = 0; index < 5; ++index)
    {
        uint32_t callbackKey = read_word(resource->bytes, callbacksOffset + index * sizeof(uint32_t));
        uint32_t expectedKey = !isInstanceOva && index == 2 ? expectedFunctionKey : 0;
        if (callbackKey != expectedKey)
            return reject(diagnostic, diagnosticCapacity, "OVA callback key/slot differs from the bounded fixture contract");
    }

    if (isInstanceOva && (variableCount != 0 || storageLength != 0))
        return reject(diagnostic, diagnosticCapacity, "fixture instance OVA is not the validated empty overlay");

    return 1;
}

int init_probe_validate_links(uint32_t expectedInstanceKey, uint32_t expectedModelKey, uint32_t expectedInstanceOvaKey,
    uint32_t expectedModelOvaKey, uint32_t expectedFunctionKey,
    char* diagnostic, size_t diagnosticCapacity)
{
    InitProbeResource* instance = init_probe_find_key(expectedInstanceKey);
    InitProbeResource* model = init_probe_find_key(expectedModelKey);
    InitProbeResource* instanceOva = init_probe_find_key(expectedInstanceOvaKey);
    InitProbeResource* modelOva = init_probe_find_key(expectedModelOvaKey);
    InitProbeResource* function = init_probe_find_key(expectedFunctionKey);
    unsigned int index;
    unsigned int ovaCount = 0;
    unsigned int ofcCount = 0;
    unsigned int registryMatches = 0;
    uint32_t modelVarsKey = 0;
    uint32_t modelFunctionKey = 0;

    if (!instance || !model || !instanceOva || !modelOva || !function)
        return reject(diagnostic, diagnosticCapacity, "manifest-selected AIC/AIM/OVA/OFC resource key is absent");
    if (instance->byteCount != 8 || read_word(instance->bytes, 0) != expectedModelKey ||
        read_word(instance->bytes, 4) != expectedInstanceOvaKey)
        return reject(diagnostic, diagnosticCapacity, "AIC links differ from the manifest-selected model/instance OVA");
    if (model->byteCount <= 0 || (model->byteCount % 8) != 0)
        return reject(diagnostic, diagnosticCapacity, "AIM resource is not a bounded 8-byte entry table");

    for (index = 0; index < (unsigned int)model->byteCount; index += 8)
    {
        uint32_t resourceKey = read_word(model->bytes, index);
        char typeCode[5];
        memcpy(typeCode, model->bytes + index + 4, 4);
        typeCode[4] = '\0';
        if (_stricmp(typeCode, ".ova") == 0)
        {
            ++ovaCount;
            modelVarsKey = resourceKey;
        }
        else if (_stricmp(typeCode, ".ofc") == 0)
        {
            ++ofcCount;
            modelFunctionKey = resourceKey;
        }
        else
            return reject(diagnostic, diagnosticCapacity, "AIM contains a resource kind outside the fixture proof");
    }

    if (ovaCount != 1 || ofcCount != 1 || modelVarsKey != expectedModelOvaKey ||
        modelFunctionKey != expectedFunctionKey || function->byteCount != 12)
        return reject(diagnostic, diagnosticCapacity, "AIM .ova/.ofc links differ from manifest or are ambiguous");

    for (index = 0; index < AI2C_i_SizeofArray(); ++index)
        if ((uint32_t)AI2C_gat_fctdefs[index].key == expectedFunctionKey)
            ++registryMatches;
    if (registryMatches != 1)
        return reject(diagnostic, diagnosticCapacity, "source-selected OFC registry row is absent or ambiguous");

    return 1;
}
