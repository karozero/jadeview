#ifndef JADEVIEW_INSTANCE_INITIALIZATION_PROBE_H
#define JADEVIEW_INSTANCE_INITIALIZATION_PROBE_H

#define _CRT_SECURE_NO_WARNINGS
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "../../external/irisEng/ai.h"
#include "../../external/irisEng/events.h"
#include "../../external/irisEng/memory.h"
#include "../../external/irisEng/dbg.h"
#include "../../external/irisEng/bigfiles.h"
#include "../../external/irisEng/loading.h"
#include "../../external/irisEng/ai/ai_compiledscripts.h"
#include "../../external/irisEng/ai/ai_colfunctions.h"
#include "../../external/irisEng/ai/ai_gameobjectfunctions.h"
#include "native_layout_contract.h"

#define INIT_PROBE_MAX_RESOURCES 5
#define INIT_PROBE_MAX_INPUT_BYTES (16u * 1024u * 1024u)
#define INIT_PROBE_MAX_CACHE_ENTRIES 32

typedef struct sInitProbeResource
{
    uint32_t key;
    uint32_t offset;
    const char* path;
    unsigned char* bytes;
    int byteCount;
} InitProbeResource;

extern InitProbeResource g_initProbeResources[INIT_PROBE_MAX_RESOURCES];
extern unsigned int g_initProbeSourceDispatchCount;

int init_probe_read_resource(InitProbeResource* resource);
InitProbeResource* init_probe_find_key(uint32_t key);
InitProbeResource* init_probe_find_offset(uint32_t offset);
int init_probe_add_address(uint32_t offset, void* address);
void init_probe_fail_fast(const char* apiName);
int init_probe_validate_ova(const InitProbeResource* resource, int isInstanceOva,
    uint32_t expectedFunctionKey, char* diagnostic, size_t diagnosticCapacity);
int init_probe_validate_links(uint32_t expectedInstanceKey, uint32_t expectedModelKey, uint32_t expectedInstanceOvaKey,
    uint32_t expectedModelOvaKey, uint32_t expectedFunctionKey,
    char* diagnostic, size_t diagnosticCapacity);

#endif
