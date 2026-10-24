/* Closed synthetic host; source callback bodies remain in external/irisEng. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "capture_host.h"

CaptureHostContext g_host;
int* ARR_gpi_Global = g_host.globalFlags;
struct sGameObjectMain** AI_MainActor = g_host.mainActors;

static void fail(const char* message)
{
    fprintf(stderr, "FAIL[%s]: %s\n", g_host.activeCase, message);
    ++g_host.failures;
}

void host_reset_fixture(const char* caseName)
{
    memset(g_host.intents, 0, sizeof(g_host.intents));
    memset(g_host.globalFlags, 0, sizeof(g_host.globalFlags));
    memset(&g_host.actor, 0, sizeof(g_host.actor));
    memset(&g_host.owner, 0, sizeof(g_host.owner));
    memset(&g_host.initFunction, 0, sizeof(g_host.initFunction));
    memset(&g_host.loopFunction, 0, sizeof(g_host.loopFunction));
    memset(&g_host.instance, 0, sizeof(g_host.instance));
    g_host.intentCount = 0;



    g_host.failures = 0;
    g_host.activeCase = caseName;
    g_host.actor.stableId = 101;
    g_host.owner.stableId = 202;
    g_host.mainActors[0] = &g_host.actor.object;
    g_host.mainActors[1] = &g_host.owner.object;

    g_host.loopFunction.funcPtr = Activateur_avec_SF_Acs_loop;
    g_host.initFunction.funcPtr = Activateur_avec_SF_Acs_init;
    g_host.instance.instanceVarDataCurrent = (byte*)calloc(1, sizeof(tScriptStruct_ActivateurAvecSF));
    if (!g_host.instance.instanceVarDataCurrent)
    {
        fprintf(stderr, "Unable to allocate synthetic callback vars.\n");
        exit(2);
    }

    AI_gpst_CurrentInstance = &g_host.instance;
    AI_gpst_CurrentGameObject = &g_host.owner.object;
    AI_gpst_CurrentFunction = &g_host.initFunction;
    AI_gul_CurrentTrack = 0;
    AI_gppst_CurrentJumpNode = &g_host.jumpNode;
    g_host.jumpNode = 0;
    TIM_gf_MainClock = 10.25f;
    TIM_gf_LockTime = 0.25f;
}

void finish_fixture(void)
{
    free(g_host.instance.instanceVarDataCurrent);
    g_host.instance.instanceVarDataCurrent = 0;
}

tScriptStruct_ActivateurAvecSF* current_vars(void)
{
    return (tScriptStruct_ActivateurAvecSF*)g_host.instance.instanceVarDataCurrent;
}

static int test_global_flag_is_set(int flagId)
{
    unsigned int unsignedFlag;
    unsigned int wordIndex;

    if (flagId < 0 || flagId >= FLAG_WORD_COUNT * 32)
    {
        fail("saved-flag index is outside the synthetic fixture range");
        return 0;
    }

    unsignedFlag = (unsigned int)flagId;
    wordIndex = unsignedFlag >> 5;
    return (g_host.globalFlags[wordIndex] & (1u << (unsignedFlag & 0x1Fu))) != 0;
}

static int object_id(tGameObjectMain* object)
{
    if (object == &g_host.actor.object)
        return g_host.actor.stableId;
    if (object == &g_host.owner.object)
        return g_host.owner.stableId;

    fail("callback emitted an intent for an object outside this fixture");
    return -1;
}

static void add_intent(CommandIntent intent)
{
    if (g_host.intentCount >= INTENT_CAPACITY)
    {
        fail("intent buffer capacity exceeded");
        return;
    }

    g_host.intents[g_host.intentCount++] = intent;
}

/* These adapters follow the cited Iris primitive semantics and fail closed
 * outside the single synthetic function/owner/object fixture. */
unsigned int AI_ul_RealResolveOneRefFunction(unsigned int key)
{
    if (key != 0x3500A7A4u)
        return 0;
    return (unsigned int)&g_host.loopFunction;
}

struct sGameObjectMain* AI2C_CheckActorGao(struct sGameObjectMain* object)
{
    unsigned int actorReference = (unsigned int)object;

    if (actorReference == 0)
        return 0;
    if (actorReference <= 2)
        return AI_MainActor[actorReference - 1];
    return object;
}

int AI_EvalFunc_AITrackCurChangeNow_C(struct sAIFunction* function)
{
    if (!function || function != &g_host.loopFunction || !AI_gpst_CurrentInstance ||
        AI_gul_CurrentTrack < 0 || AI_gul_CurrentTrack >= 5)
    {
        fail("track-change adapter received an unsupported function/context");
        return RESULT_UNCHANGED;
    }

    if (AI_gpst_CurrentFunction == function)
        return RESULT_UNCHANGED;

    AI_gpst_CurrentFunction = function;
    AI_gpst_CurrentInstance->instanceCallBacks[AI_gul_CurrentTrack] = function;
    AI_gpst_CurrentInstance->instanceJumpNodes[AI_gul_CurrentTrack] = function->nodes;
    if (!AI_gpst_CurrentInstance->instanceJumpNodes[AI_gul_CurrentTrack] && function->funcPtr)
        AI_gpst_CurrentInstance->instanceJumpNodes[AI_gul_CurrentTrack] = (tAINode*)-1;
    return RESULT_PENDING;
}

BOOL AI_EvalFunc_TIMEElapsed_C(float time, float amountInSec)
{
    return TIM_gf_MainClock - TIM_gf_LockTime - time >= amountInSec;
}

void AI_EvalFunc_OBJ_FlagInactiveSet_C(struct sGameObjectMain* object, int value)
{
    CommandIntent intent;

    memset(&intent, 0, sizeof(intent));
    intent.type = IntentFlagInactiveSet;
    intent.targetId = object_id(object);
    intent.value = value;
    add_intent(intent);
}

unsigned int AI_EvalFunc_OBJ_FlagsControlSet_C(struct sGameObjectMain* object, int addMask, int removeMask)
{
    CommandIntent intent;
    unsigned int currentControlFlags;
    unsigned int resultingControlFlags;

    if (object != &g_host.actor.object && object != &g_host.owner.object)
    {
        fail("FlagsControlSet received an object outside this fixture");
        return 0;
    }

    memset(&intent, 0, sizeof(intent));
    intent.type = IntentFlagsControlSet;
    intent.targetId = object_id(object);
    intent.addMask = (unsigned int)addMask;
    intent.removeMask = (unsigned int)removeMask;
    add_intent(intent);

    currentControlFlags = (unsigned int)object->flags2 & 0xFFFF0000u;
    resultingControlFlags = (unsigned int)addMask |
        (~(unsigned int)removeMask & currentControlFlags);
    return resultingControlFlags & 0xFFFF0000u;
}

BOOL AI_EvalFunc_ARRGlobalIntFlagTest_C(int flagId)
{
    return test_global_flag_is_set(flagId);
}
