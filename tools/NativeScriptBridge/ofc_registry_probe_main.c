#include "ofc_registry_probe.h"
#include "../../external/irisEng/ai/ai_compiledscripts.h"
#include <stdlib.h>

extern tAIFunction* AI_ul_CallbackLoadFunction(uint32_t offset);
extern int (*OFCRegistryProbeTarget(void))();
extern const char* OFCRegistryProbeSymbolName(void);

static int parse_uint32(const char* text, uint32_t* value)
{
    char* endPointer;
    unsigned long parsedValue;

    parsedValue = strtoul(text, &endPointer, 0);
    if (text[0] == '\0' || *endPointer != '\0' || parsedValue > 0xfffffffful)
        return 0;

    *value = (uint32_t)parsedValue;
    return 1;
}

static int fail(const char* message)
{
    fprintf(stderr, "OFC_PROBE_FAILED=%s\n", message);
    free(g_ofcProbeInput.bytes);
    return 1;
}

static int verify_manifest_resource_and_source_row(int expectedKey)
{
    tAIFunction* loadedFunction;
    tAIFunction* cachedFunction;
    uint32_t expectedStackCount;
    uint32_t expectedNodeBytes;
    uint32_t expectedLocalDataBytes;

    if (g_ofcProbeInput.byteCount != 12)
        return fail("fixture is not the bounded 12-byte compact metadata resource");

    expectedStackCount = *(uint32_t*)(g_ofcProbeInput.bytes + 0);
    expectedNodeBytes = *(uint32_t*)(g_ofcProbeInput.bytes + 4);
    expectedLocalDataBytes = *(uint32_t*)(g_ofcProbeInput.bytes + 8);
    if (expectedNodeBytes == 0 || expectedNodeBytes % sizeof(tAINode) != 0 ||
        expectedNodeBytes > OFC_PROBE_MAX_INPUT_BYTES || expectedLocalDataBytes != 0)
        return fail("compact metadata is outside the bounded stack/node/no-locals test contract");

    if (AI2C_i_SizeofArray() != 1 || AI2C_gat_fctdefs[0].key != expectedKey ||
        AI2C_gat_fctdefs[0].func != OFCRegistryProbeTarget())
        return fail("generated active registry row does not match the selected manifest key");

    LOA_gi_LastRef = 17;
    {
        void* clearedReference = (void*)1;
        LOA_MakeFileRef(0, &clearedReference, NULL, 1);
        if (clearedReference != NULL || LOA_gi_LastRef != 17)
            return fail("source LOA_MakeFileRef key-zero behavior changed");

        clearedReference = (void*)1;
        LOA_MakeFileRef(-1, &clearedReference, NULL, 1);
        if (clearedReference != NULL || LOA_gi_LastRef != 17)
            return fail("source LOA_MakeFileRef key-FFFFFFFF behavior changed");
    }

    LOA_gi_CurRef = 3;
    LOA_gast_Stack[LOA_gi_CurRef].key = (int)g_ofcProbeInput.functionKey;
    LOA_gb_SpeedMode = 1;
    BIG_gi_ReadMode = 2;
    LOA_ul_FileTypeSize_36 = 0;
    ofc_probe_clear_address_cache();

    loadedFunction = AI_ul_CallbackLoadFunction(g_ofcProbeInput.resourceOffset);
    if (!loadedFunction)
        return fail("source loader returned null on metadata resource");
    if (loadedFunction->usageCount != 1 || loadedFunction->stackOffsetNum != (int)expectedStackCount ||
        loadedFunction->nodes != NULL || loadedFunction->localStringConstsData != NULL ||
        loadedFunction->funcPtr != OFCRegistryProbeTarget())
        return fail("source loader metadata or callback pointer differs from expected fixture state");

    if (LOA_ul_FileTypeSize_36 != sizeof(tAIFunction))
        return fail("native loader allocation counter did not reflect one tAIFunction");

    ofc_probe_set_address_cache(loadedFunction, g_ofcProbeInput.resourceOffset);
    cachedFunction = AI_ul_CallbackLoadFunction(g_ofcProbeInput.resourceOffset);
    if (cachedFunction != loadedFunction || cachedFunction->usageCount != 2)
        return fail("controlled one-entry cache did not preserve pointer and increment usageCount");

    printf("OFC_KEY=0x%08X\n", g_ofcProbeInput.functionKey);
    printf("OFC_HEADER=stack:%lu,node_bytes:%lu,local_data_bytes:%lu\n",
        (unsigned long)expectedStackCount, (unsigned long)expectedNodeBytes,
        (unsigned long)expectedLocalDataBytes);
    printf("REGISTRY_SYMBOL=%s\n", OFCRegistryProbeSymbolName());
    printf("CALLBACK_POINTER_MATCH=1\n");
    printf("CACHE_USAGE=1->2\n");
    printf("LOA_SENTINEL_KEYS=0,FFFFFFFF->clear_and_no_enqueue\n");
    printf("RESULT=source_OFC_metadata_and_callback_identity_only\n");
    printf("LIMIT=callback_not_executed;address_cache_is_controlled_harness_adapter\n");
    free(g_ofcProbeInput.bytes);
    g_ofcProbeInput.bytes = NULL;
    return 0;
}

int main(int argumentCount, char** arguments)
{
    uint32_t functionKey;
    uint32_t resourceOffset;

    if (argumentCount != 4 || !parse_uint32(arguments[1], &functionKey) ||
        !parse_uint32(arguments[2], &resourceOffset))
        return fail("expected <function-key> <resource-offset> <manifest-resource-path>");

    if (!ofc_probe_read_input(arguments[3], functionKey, resourceOffset))
        return fail("could not load bounded manifest-selected OFC resource");

    return verify_manifest_resource_and_source_row((int)functionKey);
}
