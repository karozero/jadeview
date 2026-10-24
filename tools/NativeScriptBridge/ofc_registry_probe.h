#ifndef JADEVIEW_OFC_REGISTRY_PROBE_H
#define JADEVIEW_OFC_REGISTRY_PROBE_H

#include <stdint.h>
#include "../../external/irisEng/ai.h"
#include "../../external/irisEng/events.h"
#include "../../external/irisEng/memory.h"
#include "../../external/irisEng/dbg.h"
#include "../../external/irisEng/bigfiles.h"
#include "../../external/irisEng/loading.h"
#include "../../external/irisEng/ai/ai_colfunctions.h"
#include "../../external/irisEng/ai/ai_gameobjectfunctions.h"
#include "native_layout_contract.h"

#define OFC_PROBE_MAX_INPUT_BYTES (1024u * 1024u)

typedef struct sOfcProbeInput
{
    uint32_t functionKey;
    uint32_t resourceOffset;
    unsigned char* bytes;
    int byteCount;
} OfcProbeInput;

extern OfcProbeInput g_ofcProbeInput;
extern uint32_t g_ofcProbeCurrentKey;
extern tAIFunction* g_ofcProbeCachedFunction;
extern uint32_t g_ofcProbeCachedOffset;

int ofc_probe_read_input(const char* resourcePath, uint32_t functionKey, uint32_t resourceOffset);
void ofc_probe_set_address_cache(tAIFunction* function, uint32_t resourceOffset);
void ofc_probe_clear_address_cache(void);

void ofc_probe_fail_fast(const char* apiName);

#endif
