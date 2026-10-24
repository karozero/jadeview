#include "resource_loader_probe_host.h"

/* Original extracted source bodies remain unmodified. */
#include "../../artifacts/native-script-bridge/resource-loader-probe/generated_source_slice.c"

int main(int argumentCount, char** arguments)
{
    unsigned int expectedModelKey;
    unsigned int expectedInstanceVariablesKey;
    unsigned int expectedModelVariablesKey;
    unsigned int expectedFunctionKey;
    tAI* loadedInstance;
    tAIModel* loadedModel;

    tAIVars* instanceVariables;
    tAIVars* modelVariables;
    int firstInstanceOvaReference;
    int lastInstanceOvaReference;
    int firstModelOvaReference;
    int lastModelOvaReference;

    if (argumentCount != 9)
    {
        fprintf(stderr, "Usage: resource_loader_probe <model-key> <instance-vars-key> <model-vars-key> <function-key> <instance-aic> <model-aim> <instance-ova> <model-ova>\n");
        return 2;
    }

    if (!read_hex_key(arguments[1], &expectedModelKey) ||
        !read_hex_key(arguments[2], &expectedInstanceVariablesKey) ||
        !read_hex_key(arguments[3], &expectedModelVariablesKey) ||
        !read_hex_key(arguments[4], &expectedFunctionKey))
    {
        fprintf(stderr, "Invalid expected hexadecimal key.\n");
        return 2;
    }

    g_inputs[0].token = INSTANCE_INPUT_TOKEN;
    g_inputs[0].path = arguments[5];
    g_inputs[1].token = MODEL_INPUT_TOKEN;
    g_inputs[1].path = arguments[6];
    g_inputs[2].token = INSTANCE_VARS_INPUT_TOKEN;
    g_inputs[2].path = arguments[7];
    g_inputs[3].token = MODEL_VARS_INPUT_TOKEN;
    g_inputs[3].path = arguments[8];
    if (!read_input_resource(&g_inputs[0]) || !read_input_resource(&g_inputs[1]) ||
        !read_input_resource(&g_inputs[2]) || !read_input_resource(&g_inputs[3]))
    {
        fprintf(stderr, "Unable to read one of the explicit fixture resources.\n");
        return 2;
    }

    BIG_gi_ReadMode = 0;
    LOA_gb_SpeedMode = 0;
    LOA_gi_LastRef = 0;
    if (g_inputs[0].byteCount != 8 || g_inputs[1].byteCount == 0 || (g_inputs[1].byteCount % 8) != 0)
    {
        fprintf(stderr, "Input resource lengths do not meet the bounded AIC/AIM prerequisites.\n");
        return 3;
    }

    loadedInstance = AI_ul_CallbackLoadInstance(INSTANCE_INPUT_TOKEN);
    if (!loadedInstance || LOA_gi_LastRef != 2)
    {
        fprintf(stderr, "Native AI instance callback produced an unexpected reference count.\n");
        return 3;
    }

    write_key_line("INSTANCE_MODEL_REF", LOA_gast_Stack[0].key);
    write_key_line("INSTANCE_VARS_REF", LOA_gast_Stack[1].key);
    if (!verify_reference(0, (int)expectedModelKey,
            (void* (*)(uint32_t))AI_ul_CallbackLoadModel,
            (void**)&loadedInstance->model, 1) ||
        !verify_reference(1, (int)expectedInstanceVariablesKey,
            (void* (*)(uint32_t))AI_ul_CallbackLoadVars,
            (void**)&loadedInstance->varsInstance, 9))
    {
        fprintf(stderr, "Native AI instance callback references did not match the manifest links.\n");
        return 4;
    }

    loadedModel = AI_ul_CallbackLoadModel(MODEL_INPUT_TOKEN);
    if (!loadedModel || LOA_gi_LastRef != 4 || loadedModel->funcNum != 1)
    {
        fprintf(stderr, "Native AI model callback produced an unexpected reference count or function count.\n");
        return 5;
    }

    write_key_line("MODEL_VARS_REF", LOA_gast_Stack[2].key);
    write_key_line("MODEL_FUNCTION_REF", LOA_gast_Stack[3].key);
    if (!verify_reference(2, (int)expectedModelVariablesKey,
            (void* (*)(uint32_t))AI_ul_CallbackLoadVars,
            (void**)&loadedModel->vars, 1) ||
        !verify_reference(3, (int)expectedFunctionKey,
            (void* (*)(uint32_t))AI_ul_CallbackLoadFunction,
            (void**)&loadedModel->funcs[0], 1))
    {
        fprintf(stderr, "Native AI model callback references did not match the manifest links.\n");
        return 6;
    }

    if (!validate_compact_ova(&g_inputs[2]) || !validate_compact_ova(&g_inputs[3]))
    {
        fprintf(stderr, "OVA resource is malformed or uses an unsupported compact type/layout.\n");
        return 7;
    }

    BIG_gi_ReadMode = 2;
    LOA_gb_SpeedMode = 1;
    firstInstanceOvaReference = LOA_gi_LastRef;
    instanceVariables = AI_ul_CallbackLoadVars(INSTANCE_VARS_INPUT_TOKEN);
    lastInstanceOvaReference = LOA_gi_LastRef;
    if (!instanceVariables)
    {
        fprintf(stderr, "Native instance OVA callback returned null.\n");
        return 8;
    }
    if (!report_vars_resource("INSTANCE", instanceVariables, firstInstanceOvaReference, lastInstanceOvaReference))
    {
        fprintf(stderr, "Instance OVA queued an unsupported callback destination or callback contract.\n");
        return 10;
    }

    firstModelOvaReference = LOA_gi_LastRef;
    modelVariables = AI_ul_CallbackLoadVars(MODEL_VARS_INPUT_TOKEN);
    lastModelOvaReference = LOA_gi_LastRef;
    if (!modelVariables)
    {
        fprintf(stderr, "Native model OVA callback returned null.\n");
        return 9;
    }
    if (!report_vars_resource("MODEL", modelVariables, firstModelOvaReference, lastModelOvaReference))
    {
        fprintf(stderr, "Model OVA queued an unsupported callback destination or callback contract.\n");
        return 11;
    }

    printf("RESULT=source_AIC_AIM_OVA_resource_references_only\n");
    return 0;
}
