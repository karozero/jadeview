#include "instance_initialization_probe.h"
#include "../../external/irisEng/world.h"

/* The source functions in this include are extracted byte-for-byte by the
 * companion PowerShell script. This harness does not call the callback body. */
#include "../../artifacts/native-script-bridge/instance-initialization-probe/generated_source.c"

static int parse_hex(const char* text, uint32_t* value)
{
    char* end;
    unsigned long parsed;
    parsed = strtoul(text, &end, 16);
    if (!text[0] || *end || parsed > 0xfffffffful)
        return 0;
    *value = (uint32_t)parsed;
    return 1;
}

static int initialize_source_type_links(void)
{
    unsigned int i;
    memset(AI_gaw_EnumLink, 0xff, sizeof(AI_gaw_EnumLink));
    for (i = 0; i < 23; ++i)
    {
        unsigned int functionId = (unsigned short)AI_gast_Types[i].funcid;
        if (functionId >= sizeof(AI_gaw_EnumLink) / sizeof(AI_gaw_EnumLink[0]))
            return 0;
        AI_gaw_EnumLink[functionId] = (int16_t)i;
    }
    return 1;
}

static int verify_queue_entry(unsigned int index, uint32_t expectedKey,
    void* (*expectedCallback)(uint32_t), void** expectedDestination, unsigned char expectedFlags)
{
    loaStackElem* entry;
    if (index >= (unsigned int)LOA_gi_LastRef)
        return 0;
    entry = &LOA_gast_Stack[index];
    return (uint32_t)entry->key == expectedKey && entry->funcPtr == expectedCallback &&
        entry->retValPtr == expectedDestination && entry->flags == expectedFlags;
}

static int resolve_controlled_queue(void)
{
    unsigned int cursor;
    if (LOA_gi_LastRef < 0 || LOA_gi_LastRef > 32)
        return 0;
    for (cursor = 0; cursor < (unsigned int)LOA_gi_LastRef; ++cursor)
    {
        loaStackElem* entry = &LOA_gast_Stack[cursor];
        InitProbeResource* resource = init_probe_find_key((uint32_t)entry->key);
        void* loaded;
        if (!resource || !resource->bytes || !entry->funcPtr || !entry->retValPtr)
            return 0;
        LOA_gi_CurRef = (int)cursor;
        g_initProbeSourceDispatchCount++;
        loaded = entry->funcPtr(resource->offset);
        if (!loaded)
            return 0;
        if ((entry->flags & 4) == 0 && !init_probe_add_address(resource->offset, loaded))
            return 0;
        *entry->retValPtr = loaded;
    }
    LOA_gi_CurRef = LOA_gi_LastRef;
    return 1;
}

static int check_initialization(const tAI* instance, const tAIModel* model,
    const tAIVars* modelVars, const tAIFunction* expectedFunction)
{
    unsigned int i;
    if (!instance || instance->model != model || !model->isResolved || model->vars != modelVars ||
        instance->varsInstance != NULL || !instance->instanceVarDataInitial || !instance->instanceVarDataCurrent ||
        model->instanceCnt != 1 || instance->instanceAICBList != NULL || instance->instanceAICBListNum != 0)
        return 0;
    if (memcmp(instance->instanceVarDataInitial, modelVars->varData, modelVars->varFullSizeToAlloc) != 0 ||
        memcmp(instance->instanceVarDataCurrent, modelVars->varData, modelVars->varFullSizeToAlloc) != 0)
        return 0;
    if (instance->start != 2 || instance->end != 2)
        return 0;
    for (i = 0; i < 5; ++i)
    {
        tAIFunction* expectedSlot = i == 2 ? (tAIFunction*)expectedFunction : NULL;
        tAINode* expectedJump = i == 2 ? (tAINode*)-1 : NULL;
        if (instance->instanceCallBacks[i] != expectedSlot || instance->instanceJumpNodes[i] != expectedJump ||
            instance->instanceCallBackTimeRemaining[i] != 0.0f)
            return 0;
    }
    return 1;
}

static int check_resolved(const tAI* instance, const tAIModel* model, const tAIVars* modelVars)
{
    return instance && instance->model == model && model->isResolved && model->instanceCnt == 1 &&
        instance->varsInstance == NULL && instance->instanceVarDataInitial && instance->instanceVarDataCurrent &&
        memcmp(instance->instanceVarDataInitial, modelVars->varData, modelVars->varFullSizeToAlloc) == 0;
}

int main(int argumentCount, char** arguments)
{
    uint32_t ownerKey, instanceKey, modelKey, instanceOvaKey, modelOvaKey, functionKey;
    uint32_t instanceOffset, modelOffset, instanceOvaOffset, modelOvaOffset, functionOffset;
    InitProbeResource* instanceResource;
    InitProbeResource* modelResource;
    InitProbeResource* instanceOva;
    InitProbeResource* modelOva;
    InitProbeResource* functionResource;
    tAI* instance;
    tAIModel* model;
    tAIVars* vars;
    tAIFunction* function;
    char diagnostic[256];
    int sourceDispatchesAfterFirstInit;

    if (argumentCount != 17 || !parse_hex(arguments[1], &ownerKey) ||
        !parse_hex(arguments[2], &instanceKey) || !parse_hex(arguments[3], &instanceOffset) ||
        !parse_hex(arguments[4], &modelKey) || !parse_hex(arguments[5], &modelOffset) ||
        !parse_hex(arguments[6], &instanceOvaKey) || !parse_hex(arguments[7], &instanceOvaOffset) ||
        !parse_hex(arguments[8], &modelOvaKey) || !parse_hex(arguments[9], &modelOvaOffset) ||
        !parse_hex(arguments[10], &functionKey) || !parse_hex(arguments[11], &functionOffset))
    {
        fprintf(stderr, "ARGUMENT_COUNT=%d\n", argumentCount);
        fprintf(stderr, "Usage: probe ownerKey instanceKey instanceOffset modelKey modelOffset instanceOvaKey instanceOvaOffset modelOvaKey modelOvaOffset functionKey functionOffset fiveResourcePaths\n");
        return 2;
    }

    g_initProbeResources[0].key = instanceKey; g_initProbeResources[0].offset = instanceOffset; g_initProbeResources[0].path = arguments[12];
    g_initProbeResources[1].key = modelKey; g_initProbeResources[1].offset = modelOffset; g_initProbeResources[1].path = arguments[13];
    g_initProbeResources[2].key = instanceOvaKey; g_initProbeResources[2].offset = instanceOvaOffset; g_initProbeResources[2].path = arguments[14];
    g_initProbeResources[3].key = modelOvaKey; g_initProbeResources[3].offset = modelOvaOffset; g_initProbeResources[3].path = arguments[15];
    g_initProbeResources[4].key = functionKey; g_initProbeResources[4].offset = functionOffset; g_initProbeResources[4].path = arguments[16];

    if (!ownerKey || !instanceKey || !modelKey || !instanceOvaKey || !modelOvaKey || !functionKey ||
        !instanceOffset || !modelOffset || !instanceOvaOffset || !modelOvaOffset || !functionOffset ||
        instanceKey == modelKey || instanceKey == instanceOvaKey || instanceKey == modelOvaKey || instanceKey == functionKey ||
        modelKey == instanceOvaKey || modelKey == modelOvaKey || modelKey == functionKey ||
        instanceOvaKey == modelOvaKey || instanceOvaKey == functionKey || modelOvaKey == functionKey ||
        instanceOffset == modelOffset || instanceOffset == instanceOvaOffset || instanceOffset == modelOvaOffset || instanceOffset == functionOffset ||
        modelOffset == instanceOvaOffset || modelOffset == modelOvaOffset || modelOffset == functionOffset ||
        instanceOvaOffset == modelOvaOffset || instanceOvaOffset == functionOffset || modelOvaOffset == functionOffset)
    {
        fprintf(stderr, "ARGUMENT_COUNT=%d\n", argumentCount);
        fprintf(stderr, "Manifest resource keys/offsets are empty or ambiguous.\n");
        return 3;
    }
    if (!init_probe_read_resource(&g_initProbeResources[0]) || !init_probe_read_resource(&g_initProbeResources[1]) ||
        !init_probe_read_resource(&g_initProbeResources[2]) || !init_probe_read_resource(&g_initProbeResources[3]) ||
        !init_probe_read_resource(&g_initProbeResources[4]))
    {
        fprintf(stderr, "Unable to read manifest-selected source resources.\n");
        return 4;
    }

    instanceResource = init_probe_find_key(instanceKey);
    modelResource = init_probe_find_key(modelKey);
    instanceOva = init_probe_find_key(instanceOvaKey);
    modelOva = init_probe_find_key(modelOvaKey);
    functionResource = init_probe_find_key(functionKey);
    diagnostic[0] = '\0';
    if (!instanceResource || instanceResource->byteCount != 8 || !modelResource || modelResource->byteCount <= 0 || modelResource->byteCount % 8 ||
        !init_probe_validate_ova(instanceOva, 1, functionKey, diagnostic, sizeof(diagnostic)) ||
        !init_probe_validate_ova(modelOva, 0, functionKey, diagnostic, sizeof(diagnostic)) ||
        !init_probe_validate_links(instanceKey, modelKey, instanceOvaKey, modelOvaKey, functionKey, diagnostic, sizeof(diagnostic)))
    {
        fprintf(stderr, "PREFLIGHT_REJECTED=%s\n", diagnostic[0] ? diagnostic : "resource chain malformed or outside fixture scope");
        fprintf(stderr, "SOURCE_DISPATCH_COUNT=%u\n", g_initProbeSourceDispatchCount);
        return 5;
    }
    if (!initialize_source_type_links())
    {
        fprintf(stderr, "PREFLIGHT_REJECTED=source type table cannot be bounded\n");
        return 5;
    }

    BIG_gi_ReadMode = 2;
    LOA_gb_SpeedMode = 1;
    LOA_gi_LastRef = 0;
    LOA_gi_CurRef = 0;
    instance = AI_ul_CallbackLoadInstance(instanceOffset);
    if (!instance || LOA_gi_LastRef != 2 ||
        !verify_queue_entry(0, modelKey, (void* (*)(uint32_t))AI_ul_CallbackLoadModel, (void**)&instance->model, 1) ||
        !verify_queue_entry(1, instanceOvaKey, (void* (*)(uint32_t))AI_ul_CallbackLoadVars, (void**)&instance->varsInstance, 9))
    {
        fprintf(stderr, "SOURCE_AIC_LINKS=failed\n");
        return 6;
    }
    if (!resolve_controlled_queue())
    {
        fprintf(stderr, "CONTROLLED_QUEUE_ADAPTER=failed;last_ref=%d\n", LOA_gi_LastRef);
        return 8;
    }
    model = instance->model;
    if (!model || !instance->varsInstance || !model->vars || model->funcNum != 1 || LOA_gi_LastRef != 5 ||
        !verify_queue_entry(2, modelOvaKey, (void* (*)(uint32_t))AI_ul_CallbackLoadVars, (void**)&model->vars, 1) ||
        !verify_queue_entry(3, functionKey, AI_ul_CallbackLoadFunction, (void**)&model->funcs[0], 1) ||
        !verify_queue_entry(4, functionKey, AI_ul_CallbackLoadFunction, (void**)&model->vars->callBacks[2], 1) ||
        model->funcs[0] == NULL || model->vars->callBacks[2] != model->funcs[0])
    {
        fprintf(stderr, "SOURCE_QUEUE_RESOLUTION=failed;last_ref=%d\n", LOA_gi_LastRef);
        return 8;
    }
    function = model->funcs[0];
    vars = model->vars;
    if (function->nodes != NULL || function->funcPtr == NULL || function->usageCount != 2 ||
        model->instanceCnt != 0 || instance->instanceVarDataCurrent != NULL)
    {
        fprintf(stderr, "SOURCE_LOADED_RESOURCES=unexpected_state\n");
        return 9;
    }

    AI_ResolveInstanceRef(instance);
    if (!check_resolved(instance, model, vars))
    {
        fprintf(stderr, "SOURCE_RESOLVE_INSTANCE=postcondition_failed\n");
        return 10;
    }
    AI_ReinitInstance(instance);
    if (!check_initialization(instance, model, vars, function))
    {
        fprintf(stderr, "SOURCE_REINIT_INSTANCE=postcondition_failed\n");
        return 11;
    }
    sourceDispatchesAfterFirstInit = (int)g_initProbeSourceDispatchCount;
    AI_ResolveInstanceRef(instance);
    AI_ReinitInstance(instance);
    if (!check_initialization(instance, model, vars, function) ||
        g_initProbeSourceDispatchCount != (unsigned int)sourceDispatchesAfterFirstInit)
    {
        fprintf(stderr, "SOURCE_REINIT_IDEMPOTENCE=failed\n");
        return 12;
    }

    printf("OWNER=0x%08X\n", ownerKey);
    printf("MODEL=0x%08X\n", modelKey);
    printf("INITIALIZATION=ResolveInstanceRef_then_ReinitInstance\n");
    printf("CALLBACK_SLOT_2=source_registry_pointer;not_executed\n");
    printf("MODEL_DEFAULTS=initial_and_current_buffers_match\n");
    printf("INSTANCE_OVA=empty_and_released\n");
    printf("INSTANCE_COUNT=%d\n", model->instanceCnt);
    printf("QUEUE_SOURCE_CALLBACKS=%u\n", g_initProbeSourceDispatchCount);
    printf("RESULT=bounded_source_instance_initialization_only\n");
    printf("LIMIT=controlled_LOA_address_cache;no_world_or_actor_runtime;callback_not_executed;Iris_source_not_retail_proof\n");

    free(instanceResource->bytes); free(modelResource->bytes); free(instanceOva->bytes); free(modelOva->bytes); free(functionResource->bytes);
    return 0;
}
