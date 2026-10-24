#include "ofc_registry_probe.h"
#include <stdarg.h>

OfcProbeInput g_ofcProbeInput;
uint32_t g_ofcProbeCurrentKey;
tAIFunction* g_ofcProbeCachedFunction;
uint32_t g_ofcProbeCachedOffset;

static void release_input(void)
{
    free(g_ofcProbeInput.bytes);
    g_ofcProbeInput.bytes = NULL;
    g_ofcProbeInput.byteCount = 0;
}

void ofc_probe_fail_fast(const char* apiName)
{
    fprintf(stderr, "UNEXPECTED_NATIVE_API=%s\n", apiName);
    release_input();
    exit(90);
}

int ofc_probe_read_input(const char* resourcePath, uint32_t functionKey, uint32_t resourceOffset)
{
    FILE* resourceFile;
    long resourceLength;

    if (fopen_s(&resourceFile, resourcePath, "rb") != 0)
        return 0;

    if (fseek(resourceFile, 0, SEEK_END) != 0)
    {
        fclose(resourceFile);
        return 0;
    }

    resourceLength = ftell(resourceFile);
    if (resourceLength < 0 || (unsigned long)resourceLength > OFC_PROBE_MAX_INPUT_BYTES)
    {
        fclose(resourceFile);
        return 0;
    }

    if (fseek(resourceFile, 0, SEEK_SET) != 0)
    {
        fclose(resourceFile);
        return 0;
    }

    g_ofcProbeInput.bytes = (unsigned char*)malloc((size_t)resourceLength + 1);
    if (!g_ofcProbeInput.bytes)
    {
        fclose(resourceFile);
        return 0;
    }

    g_ofcProbeInput.byteCount = (int)resourceLength;
    if (fread(g_ofcProbeInput.bytes, 1, (size_t)resourceLength, resourceFile) != (size_t)resourceLength)
    {
        fclose(resourceFile);
        release_input();
        return 0;
    }

    g_ofcProbeInput.bytes[resourceLength] = 0;
    g_ofcProbeInput.functionKey = functionKey;
    g_ofcProbeInput.resourceOffset = resourceOffset;
    fclose(resourceFile);
    return 1;
}

void ofc_probe_set_address_cache(tAIFunction* function, uint32_t resourceOffset)
{
    g_ofcProbeCachedFunction = function;
    g_ofcProbeCachedOffset = resourceOffset;
}

void ofc_probe_clear_address_cache(void)
{
    g_ofcProbeCachedFunction = NULL;
    g_ofcProbeCachedOffset = 0;
}

int LOA_ul_SearchAddress(int address)
{
    if (g_ofcProbeCachedFunction && (uint32_t)address == g_ofcProbeCachedOffset)
        return (int)(intptr_t)g_ofcProbeCachedFunction;

    return -1;
}

char* BIG_pc_ReadFileTmp(int offset, int* size)
{
    if ((uint32_t)offset != g_ofcProbeInput.resourceOffset || !g_ofcProbeInput.bytes)
        ofc_probe_fail_fast("BIG_pc_ReadFileTmp outside selected manifest resource");

    *size = g_ofcProbeInput.byteCount;
    return (char*)g_ofcProbeInput.bytes;
}

void* MEM_p_Alloc(unsigned int size)
{
    void* allocation;

    if (size == 0 || size > 1024u * 1024u)
        ofc_probe_fail_fast("MEM_p_Alloc outside bounded OFC metadata size");

    allocation = calloc(1, size);
    if (!allocation)
        ofc_probe_fail_fast("MEM_p_Alloc failed");

    return allocation;
}

void* MEM_p_AllocAligned(int size, int alignment)
{
    (void)size;
    (void)alignment;
    ofc_probe_fail_fast("MEM_p_AllocAligned interpreted-node path must not run for compiled compact OFC");
    return NULL;
}

void LOA_ReadAINode(char** offset, tAINode* node, int readMode)
{
    (void)offset;
    (void)node;
    (void)readMode;
    ofc_probe_fail_fast("LOA_ReadAINode interpreted-node path must not run for registered compact OFC");
}

void LOA_ReadAILocal(char** offset, tAILocal* localData, int readMode)
{
    (void)offset;
    (void)localData;
    (void)readMode;
    ofc_probe_fail_fast("LOA_ReadAILocal editor/local-node path is out of scope");
}

void DBG_OutputDebugString(char* format, ...)
{
    (void)format;
}

int AI_EvalFunc_COLBVGetAllOverlaps_C(struct sGameObjectMain* owner, struct sGameObjectMain** overlaps,
    int mustHaveFlags, int mustNotHaveFlags, int testMode)
{
    (void)owner;
    (void)overlaps;
    (void)mustHaveFlags;
    (void)mustNotHaveFlags;
    (void)testMode;
    ofc_probe_fail_fast("AI_EvalFunc_COLBVGetAllOverlaps_C callback execution is out of scope");
    return 0;
}

BOOL AI_EvalFunc_COLBVObjectObject_C(struct sGameObjectMain* first, struct sGameObjectMain* second)
{
    (void)first;
    (void)second;
    ofc_probe_fail_fast("AI_EvalFunc_COLBVObjectObject_C callback execution is out of scope");
    return FALSE;
}

unsigned int AI_EvalFunc_OBJ_FlagsControlSet_C(struct sGameObjectMain* target, int setFlags, int clearFlags)
{
    (void)target;
    (void)setFlags;
    (void)clearFlags;
    ofc_probe_fail_fast("AI_EvalFunc_OBJ_FlagsControlSet_C callback execution is out of scope");
    return 0;
}

struct sGameObjectMain* AI2C_CheckActorGao(struct sGameObjectMain* actor)
{
    (void)actor;
    ofc_probe_fail_fast("AI2C_CheckActorGao callback execution is out of scope");
    return 0;
}
