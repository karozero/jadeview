#ifndef JADEVIEW_RESOURCE_LOADER_PROBE_HOST_H
#define JADEVIEW_RESOURCE_LOADER_PROBE_HOST_H

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
#define MODEL_VARS_INPUT_TOKEN 0x3000u
#define INSTANCE_VARS_INPUT_TOKEN 0x4000u
#define MAX_HOST_ALLOCATION (16u * 1024u * 1024u)
#define MAX_SCRIPT_VARIABLES 4096u

enum
{
    LOADER_REFERENCE_CAPACITY = sizeof(LOA_gast_Stack) / sizeof(LOA_gast_Stack[0]),
    NATIVE_CALLBACK_SLOT_COUNT = sizeof(((tAIVars*)0)->callBacks) / sizeof(tAIFunction*)
};

typedef struct sInputResource
{
    unsigned int token;
    const char* path;
    unsigned char* bytes;
    int byteCount;
} InputResource;

extern InputResource g_inputs[4];
extern tAIModel* AI_ul_CallbackLoadModel(uint32_t offset);
extern tAI* AI_ul_CallbackLoadInstance(uint32_t offset);
extern tAIVars* AI_ul_CallbackLoadVars(uint32_t offset);
extern tAIFunction* AI_ul_CallbackLoadFunction(uint32_t offset);
extern tOfc_scriptengine_functions_types AI_gast_Types[23];

int read_input_resource(InputResource* resource);
int read_hex_key(const char* text, unsigned int* key);
int validate_compact_ova(const InputResource* resource);
void write_key_line(const char* roleName, int key);
int verify_reference(int index, int expectedKey, void* expectedCallback, void** expectedDestination, byte expectedFlags);
int report_vars_resource(const char* label, const tAIVars* variables, int firstReference, int lastReference);

#endif
