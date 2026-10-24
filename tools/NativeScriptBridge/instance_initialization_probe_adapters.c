#include "instance_initialization_probe.h"

struct sGameObjectMain* AI2C_CheckActorGao(struct sGameObjectMain* actor)
{
    (void)actor;
    init_probe_fail_fast("Activateur callback body must not execute in initialization proof");
    return NULL;
}

BOOL AI_EvalFunc_COLBVObjectObject_C(struct sGameObjectMain* first, struct sGameObjectMain* second)
{
    (void)first;
    (void)second;
    init_probe_fail_fast("collision callback API is outside initialization proof");
    return FALSE;
}

int AI_EvalFunc_COLBVGetAllOverlaps_C(struct sGameObjectMain* owner,
    struct sGameObjectMain** overlaps, int mustHaveFlags, int mustNotHaveFlags, int testMode)
{
    (void)owner;
    (void)overlaps;
    (void)mustHaveFlags;
    (void)mustNotHaveFlags;
    (void)testMode;
    init_probe_fail_fast("collision overlap API is outside initialization proof");
    return 0;
}

unsigned int AI_EvalFunc_OBJ_FlagsControlSet_C(struct sGameObjectMain* target, int setFlags, int clearFlags)
{
    (void)target;
    (void)setFlags;
    (void)clearFlags;
    init_probe_fail_fast("object state API is outside initialization proof");
    return 0;
}
