#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../../external/irisEng/memory.h"
#include "../../external/irisEng/ai.h"
#include "../../external/irisEng/ai/ai_compiledscripts.h"
#include "../../external/irisEng/bigfiles.h"
#include "../../external/irisEng/loading.h"
#include "native_layout_contract.h"

#define INSTANCE_INPUT_TOKEN 0x1000u
#define MODEL_INPUT_TOKEN 0x2000u
#define MAX_HOST_ALLOCATION (16u * 1024u * 1024u)

typedef struct sInputResource
{
    unsigned int token;
    const char* path;
    unsigned char* bytes;
    int byteCount;
} InputResource;

static InputResource g_inputs[2];

extern tAIModel* AI_ul_CallbackLoadModel(uint32_t offset);
extern tAIVars* AI_ul_CallbackLoadVars(uint32_t offset);
extern tAIFunction* AI_ul_CallbackLoadFunction(uint32_t offset);

static void fail_fast(const char* apiName)
{
    fprintf(stderr, "UNSUPPORTED_ENGINE_API=%s\n", apiName);
    exit(90);
}

#include "../../artifacts/native-script-bridge/resource-loader-probe/generated_source_slice.c"

/* These callback implementations are not executed by the queue probe. The
 * function addresses are retained only so LOA_MakeFileRef can capture them. */
tAIVars* AI_ul_CallbackLoadVars(uint32_t offset)
{
    (void)offset;
    fail_fast("AI_ul_CallbackLoadVars callback intentionally not executed");
    return NULL;
}

tAIFunction* AI_ul_CallbackLoadFunction(uint32_t offset)
{
    (void)offset;
    fail_fast("AI_ul_CallbackLoadFunction callback intentionally not executed");
    return NULL;
}

static int read_input_resource(InputResource* resource)
{
    FILE* inputFile;
    long inputLength;

    inputFile = fopen(resource->path, "rb");
    if (!inputFile)
        return 0;

    if (fseek(inputFile, 0, SEEK_END) != 0)
    {
        fclose(inputFile);
        return 0;
    }

    inputLength = ftell(inputFile);
    if (inputLength < 0 || inputLength > 16 * 1024 * 1024)
    {
        fclose(inputFile);
        return 0;
    }

    if (fseek(inputFile, 0, SEEK_SET) != 0)
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

static InputResource* find_input_resource(unsigned int token)
{
    int resourceIndex;

    for (resourceIndex = 0; resourceIndex < 2; ++resourceIndex)
    {
        if (g_inputs[resourceIndex].token == token)
            return &g_inputs[resourceIndex];
    }

    return NULL;
}

char* BIG_pc_ReadFileTmp(int offset, int* size)
{
    InputResource* resource;

    resource = find_input_resource((unsigned int)offset);
    if (!resource)
        fail_fast("BIG_pc_ReadFileTmp outside manifest-selected fixture resources");

    if (size)
        *size = resource->byteCount;
    return (char*)resource->bytes;
}

void* MEM_p_Alloc(unsigned int size)
{
    void* allocation;

    if (size > MAX_HOST_ALLOCATION)
        fail_fast("MEM_p_Alloc outside bounded fixture allocation limit");

    allocation = calloc(1, size ? size : 1);
    if (!allocation)
        fail_fast("MEM_p_Alloc host allocation failure");
    return allocation;
}

static int read_hex_key(const char* text, unsigned int* key)
{
    char* endPointer;
    unsigned long parsedValue;

    parsedValue = strtoul(text, &endPointer, 16);
    if (text[0] == '\0' || *endPointer != '\0' || parsedValue > 0xfffffffful)
        return 0;
    *key = (unsigned int)parsedValue;
    return 1;
}

static void write_key_line(const char* roleName, int key)
{
    printf("%s=%08X\n", roleName, (unsigned int)key);
}

static int verify_reference(int index, int expectedKey, void* expectedCallback, void** expectedDestination, byte expectedFlags)
{
    loaStackElem* queuedReference;

    queuedReference = &LOA_gast_Stack[index];
    return queuedReference->key == expectedKey &&
        queuedReference->funcPtr == expectedCallback &&
        queuedReference->retValPtr == expectedDestination &&
        queuedReference->flags == expectedFlags;
}

int main(int argumentCount, char** arguments)
{
    unsigned int expectedModelKey;
    unsigned int expectedInstanceVariablesKey;
    unsigned int expectedModelVariablesKey;
    unsigned int expectedFunctionKey;
    tAI* loadedInstance;
    tAIModel* loadedModel;

    if (argumentCount != 7)
    {
        fprintf(stderr, "Usage: resource_loader_probe <instance-key> <instance-vars-key> <model-vars-key> <function-key> <instance-resource> <model-resource>\n");
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
    if (!read_input_resource(&g_inputs[0]) || !read_input_resource(&g_inputs[1]))
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

    printf("RESULT=source_callback_resource_references_only\n");
    return 0;
}
