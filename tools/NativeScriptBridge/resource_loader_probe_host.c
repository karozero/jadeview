#include "resource_loader_probe_host.h"

InputResource g_inputs[4];
struct sGameObjectMain** AI_MainActor = AI_MainActorArr;

static void fail_fast(const char* apiName)
{
    fprintf(stderr, "UNSUPPORTED_ENGINE_API=%s\n", apiName);
    exit(90);
}



/* These callback implementations are not executed by the queue probe. The
 * function addresses are retained only so LOA_MakeFileRef can capture them. */
void* MEM_p_AllocFromEnd(int size)
{
    (void)size;
    fail_fast("MEM_p_AllocFromEnd compact OVA unsupported/should be skipped");
    return NULL;
}

void MEM_FreeFromEnd(void* pointer)
{
    (void)pointer;
    fail_fast("MEM_FreeFromEnd compact OVA unsupported/should be skipped");
}

tAIFunction* AI_ul_CallbackLoadFunction(uint32_t offset)
{
    (void)offset;
    fail_fast("AI_ul_CallbackLoadFunction callback intentionally not executed");
    return NULL;
}

int read_input_resource(InputResource* resource)
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

    for (resourceIndex = 0; resourceIndex < 4; ++resourceIndex)
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

int read_hex_key(const char* text, unsigned int* key)
{
    char* endPointer;
    unsigned long parsedValue;

    parsedValue = strtoul(text, &endPointer, 16);
    if (text[0] == '\0' || *endPointer != '\0' || parsedValue > 0xfffffffful)
        return 0;
    *key = (unsigned int)parsedValue;
    return 1;
}
