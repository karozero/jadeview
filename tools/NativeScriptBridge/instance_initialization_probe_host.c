#include "instance_initialization_probe.h"
#include "../../external/irisEng/gameobject.h"

InitProbeResource g_initProbeResources[INIT_PROBE_MAX_RESOURCES];
unsigned int g_initProbeSourceDispatchCount;
struct sGameObjectMain** AI_MainActor = AI_MainActorArr;

typedef struct sInitProbeAddress
{
    uint32_t offset;
    void* address;
} InitProbeAddress;

static InitProbeAddress g_addresses[INIT_PROBE_MAX_CACHE_ENTRIES];
static unsigned int g_addressCount;

void init_probe_fail_fast(const char* apiName)
{
    fprintf(stderr, "UNSUPPORTED_ENGINE_API=%s\n", apiName);
    exit(90);
}

int init_probe_read_resource(InitProbeResource* resource)
{
    FILE* inputFile;
    long inputLength;

    if (!resource || fopen_s(&inputFile, resource->path, "rb") != 0)
        return 0;
    if (fseek(inputFile, 0, SEEK_END) != 0)
    {
        fclose(inputFile);
        return 0;
    }

    inputLength = ftell(inputFile);
    if (inputLength < 0 || (unsigned long)inputLength > INIT_PROBE_MAX_INPUT_BYTES ||
        fseek(inputFile, 0, SEEK_SET) != 0)
    {
        fclose(inputFile);
        return 0;
    }

    resource->bytes = (unsigned char*)malloc((size_t)inputLength + 1);
    if (!resource->bytes)
    {
        fclose(inputFile);
        return 0;
    }
    resource->byteCount = (int)inputLength;
    if (fread(resource->bytes, 1, (size_t)inputLength, inputFile) != (size_t)inputLength)
    {
        fclose(inputFile);
        return 0;
    }
    resource->bytes[inputLength] = 0;
    fclose(inputFile);
    return 1;
}

InitProbeResource* init_probe_find_key(uint32_t key)
{
    unsigned int resourceIndex;
    for (resourceIndex = 0; resourceIndex < INIT_PROBE_MAX_RESOURCES; ++resourceIndex)
        if (g_initProbeResources[resourceIndex].key == key)
            return &g_initProbeResources[resourceIndex];
    return NULL;
}

InitProbeResource* init_probe_find_offset(uint32_t offset)
{
    unsigned int resourceIndex;
    for (resourceIndex = 0; resourceIndex < INIT_PROBE_MAX_RESOURCES; ++resourceIndex)
        if (g_initProbeResources[resourceIndex].offset == offset)
            return &g_initProbeResources[resourceIndex];
    return NULL;
}

char* BIG_pc_ReadFileTmp(int offset, int* size)
{
    InitProbeResource* resource = init_probe_find_offset((uint32_t)offset);
    if (!resource || !resource->bytes)
        init_probe_fail_fast("BIG_pc_ReadFileTmp outside the five manifest-selected resources");
    if (size)
        *size = resource->byteCount;
    return (char*)resource->bytes;
}

void* MEM_p_Alloc(unsigned int size)
{
    void* allocation;
    if (size > INIT_PROBE_MAX_INPUT_BYTES)
        init_probe_fail_fast("MEM_p_Alloc exceeded the bounded probe allocation limit");
    allocation = calloc(1, size ? size : 1);
    if (!allocation)
        init_probe_fail_fast("MEM_p_Alloc failed");
    return allocation;
}

void MEM_Free(void* allocation)
{
    free(allocation);
}

int init_probe_add_address(uint32_t offset, void* address)
{
    unsigned int addressIndex;
    if (!address)
        return 0;
    for (addressIndex = 0; addressIndex < g_addressCount; ++addressIndex)
    {
        if (g_addresses[addressIndex].offset == offset)
            return g_addresses[addressIndex].address == address;
    }
    if (g_addressCount >= INIT_PROBE_MAX_CACHE_ENTRIES)
        return 0;
    g_addresses[g_addressCount].offset = offset;
    g_addresses[g_addressCount].address = address;
    ++g_addressCount;
    return 1;
}

int LOA_ul_SearchAddress(int offset)
{
    unsigned int addressIndex;
    for (addressIndex = 0; addressIndex < g_addressCount; ++addressIndex)
        if (g_addresses[addressIndex].offset == (uint32_t)offset)
            return (int)(intptr_t)g_addresses[addressIndex].address;
    return -1;
}

void LOA_AddPosAddress(unsigned int offset, unsigned int address)
{
    if (!init_probe_add_address(offset, (void*)(uintptr_t)address))
        init_probe_fail_fast("controlled LOA address cache conflict or capacity exceeded");
}

void LOA_DeleteAddress(int address)
{
    unsigned int addressIndex;
    for (addressIndex = 0; addressIndex < g_addressCount; ++addressIndex)
    {
        if ((int)(intptr_t)g_addresses[addressIndex].address == address)
        {
            --g_addressCount;
            g_addresses[addressIndex] = g_addresses[g_addressCount];
            return;
        }
    }
}

int BIG_ul_SearchKeyToPos(uint32_t key)
{
    (void)key;
    init_probe_fail_fast("nonzero serialized references require a real BF key/address cache");
    return -1;
}

void* MEM_p_AllocFromEnd(int size)
{
    (void)size;
    init_probe_fail_fast("editor-only OVA extra-data allocation is unsupported");
    return NULL;
}

void MEM_FreeFromEnd(void* allocation)
{
    (void)allocation;
    init_probe_fail_fast("editor-only OVA extra-data free is unsupported");
}

void* MEM_p_AllocAligned(int size, int alignment)
{
    (void)size;
    (void)alignment;
    init_probe_fail_fast("interpreted OFC nodes are outside this compiled callback fixture");
    return NULL;
}

void LOA_ReadAINode(char** offset, tAINode* node, int readMode)
{
    (void)offset;
    (void)node;
    (void)readMode;
    init_probe_fail_fast("interpreted OFC node read is outside this compiled callback fixture");
}

void LOA_ReadAILocal(char** offset, tAILocal* localData, int readMode)
{
    (void)offset;
    (void)localData;
    (void)readMode;
    init_probe_fail_fast("OFC editor-local data read is outside this fixture");
}

void DBG_OutputDebugString(char* format, ...)
{
    (void)format;
}
