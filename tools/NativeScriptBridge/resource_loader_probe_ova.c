#include "resource_loader_probe_host.h"

static int initialize_source_type_enum_links(void)
{
    unsigned int typeIndex;
    unsigned int functionId;

    memset(AI_gaw_EnumLink, 0xff, sizeof(AI_gaw_EnumLink));
    for (typeIndex = 0; typeIndex < 23; ++typeIndex)
    {
        functionId = (unsigned short)AI_gast_Types[typeIndex].funcid;
        if (functionId >= sizeof(AI_gaw_EnumLink) / sizeof(AI_gaw_EnumLink[0]))
            return 0;
        AI_gaw_EnumLink[functionId] = (int16_t)typeIndex;
    }

    return 1;
}

int validate_compact_ova(const InputResource* resource)
{
    const unsigned char* cursor;
    const unsigned char* end;
    unsigned int descriptorByteCount;
    unsigned int variableCount;
    unsigned int nameByteCount;
    unsigned int defaultStorageByteCount;
    unsigned int index;
    unsigned int arrayCount;
    unsigned int typeId;
    unsigned int typeIndex;
    unsigned int arrayDimensions;
    unsigned int elementCount;
    unsigned int expectedDefaultBytes;
    unsigned int variableStorageBytes;
    unsigned int trailingByteCount;
    uint32_t instanceFunctionCount;

    if (!resource || resource->byteCount < 8)
        return 0;

    cursor = resource->bytes;
    end = resource->bytes + resource->byteCount;
    descriptorByteCount = *(const uint32_t*)cursor;
    cursor += sizeof(uint32_t);
    if ((descriptorByteCount % sizeof(AIVarElem)) != 0 || descriptorByteCount > MAX_HOST_ALLOCATION)
        return 0;
    variableCount = descriptorByteCount / sizeof(AIVarElem);
    if (variableCount > MAX_SCRIPT_VARIABLES || (size_t)(end - cursor) < descriptorByteCount + sizeof(uint32_t))
        return 0;

    if (!initialize_source_type_enum_links())
        return 0;
    cursor += descriptorByteCount;
    nameByteCount = *(const uint32_t*)cursor;
    cursor += sizeof(uint32_t);
    if (nameByteCount != variableCount * sizeof(AIVarNameElem) || (size_t)(end - cursor) < nameByteCount + sizeof(uint32_t))
        return 0;
    cursor += nameByteCount;

    /* In the source speed/read-mode-2 path, both mode-2 words are omitted. */
    defaultStorageByteCount = *(const uint32_t*)cursor;
    cursor += sizeof(uint32_t);
    if (defaultStorageByteCount > MAX_HOST_ALLOCATION || (size_t)(end - cursor) < defaultStorageByteCount + (NATIVE_CALLBACK_SLOT_COUNT * sizeof(uint32_t)))
        return 0;

    expectedDefaultBytes = 0;
    for (index = 0; index < variableCount; ++index)
    {
        const AIVarElem* variable;
        unsigned int descriptorOffset;
        unsigned int typeSize;

        descriptorOffset = (unsigned int)sizeof(uint32_t) + index * (unsigned int)sizeof(AIVarElem);
        variable = (const AIVarElem*)(resource->bytes + descriptorOffset);
        typeId = (unsigned short)variable->variableType;
        if (typeId >= sizeof(AI_gaw_EnumLink) / sizeof(AI_gaw_EnumLink[0]) || AI_gaw_EnumLink[typeId] < 0)
            return 0;
        typeIndex = (unsigned short)AI_gaw_EnumLink[typeId];
        typeSize = (unsigned short)AI_gast_Types[typeIndex].dataTypeSize;
        arrayCount = variable->variableArrayCount;
        arrayDimensions = (arrayCount >> 30) & 0xffu;
        elementCount = arrayCount & 0x3fffffffu;
        if (variable->varOffset < 0)
            return 0;

        if (typeSize == sizeof(uint32_t))
        {
            /* Scalar word-backed types use the native table size directly. */
        }
        else if (typeId == 37u)
        {
            if (typeSize != sizeof(Vec3))
                return 0;
        }
        else if (typeId == 41u)
        {
            if (typeSize != sizeof(tMessage))
                return 0;
        }
        else if (typeId == 44u)
        {
            if (typeSize != sizeof(tTextGet))
                return 0;
        }
        else
        {
            return 0;
        }

        if (elementCount != 0 && typeSize > MAX_HOST_ALLOCATION / elementCount)
            return 0;
        variableStorageBytes = elementCount * typeSize;
        if (arrayCount != 1 && arrayDimensions > (MAX_HOST_ALLOCATION / sizeof(uint32_t)))
            return 0;
        if (expectedDefaultBytes > MAX_HOST_ALLOCATION - arrayDimensions * sizeof(uint32_t) ||
            expectedDefaultBytes + arrayDimensions * sizeof(uint32_t) > MAX_HOST_ALLOCATION - variableStorageBytes)
            return 0;
        if (arrayCount != 1)
            expectedDefaultBytes += arrayDimensions * sizeof(uint32_t);
        expectedDefaultBytes += variableStorageBytes;

        if (expectedDefaultBytes > defaultStorageByteCount)
            return 0;
        if ((unsigned int)variable->varOffset > defaultStorageByteCount ||
            variableStorageBytes > defaultStorageByteCount - (unsigned int)variable->varOffset)
            return 0;
    }
    if (expectedDefaultBytes != defaultStorageByteCount)
        return 0;

    cursor += defaultStorageByteCount + (NATIVE_CALLBACK_SLOT_COUNT * sizeof(uint32_t));
    trailingByteCount = (unsigned int)(end - cursor);
    if (trailingByteCount != 0)
    {
        if (trailingByteCount < sizeof(uint32_t))
            return 0;
        instanceFunctionCount = *(const uint32_t*)cursor;
        cursor += sizeof(uint32_t);
        if (instanceFunctionCount > MAX_SCRIPT_VARIABLES || (size_t)(end - cursor) != instanceFunctionCount * sizeof(uint32_t))
            return 0;
    }

    return 1;
}
