/*
 * Private synthetic command-capture harness for Iris' compiled
 * Activateur_avec_SF callbacks. The callback implementation is compiled
 * directly from external/irisEng; this file supplies only a closed set of
 * synthetic engine API adapters for the imports those two functions use.
 *
 * This harness does not load a BF, schedule a world, or apply mutations to
 * world/EOT state. FlagsControlSet and FlagInactiveSet are captured as intents.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../../external/irisEng/ai/ai_bge/ai_compiledscripts_inc.h"

#define FLAG_WORD_COUNT 128
#define INTENT_CAPACITY 8
#define RESULT_PENDING 5
#define RESULT_UNCHANGED 1000

typedef enum eIntentType
{
    IntentFlagsControlSet,
    IntentFlagInactiveSet
} IntentType;

typedef struct sCommandIntent
{
    IntentType type;
    int targetId;
    unsigned int addMask;
    unsigned int removeMask;
    int value;
} CommandIntent;

typedef struct sSyntheticObject
{
    tGameObjectMain object;
    int stableId;
} SyntheticObject;

static int g_failures;
static CommandIntent g_intents[INTENT_CAPACITY];
static int g_intentCount;
static int g_initResult;
static int g_waitResult;
static int g_elapsedResult;
static const char* g_activeCase;
static SyntheticObject g_actor;
static SyntheticObject g_owner;
static tGameObjectMain* g_mainActors[2];
static int g_globalFlags[FLAG_WORD_COUNT];
static tAIFunction g_initFunction;
static tAIFunction g_loopFunction;
static tAI g_instance;
static tAINode* g_jumpNode;

int* ARR_gpi_Global = g_globalFlags;
struct sGameObjectMain** AI_MainActor = g_mainActors;

int Activateur_avec_SF_Acs_init(void);
int Activateur_avec_SF_Acs_loop(void);

static void fail(const char* message)
{
    fprintf(stderr, "FAIL[%s]: %s\n", g_activeCase, message);
    ++g_failures;
}

static void require_true(int condition, const char* message)
{
    if (!condition)
        fail(message);
}

static void reset_fixture(const char* caseName)
{
    memset(g_intents, 0, sizeof(g_intents));
    memset(g_globalFlags, 0, sizeof(g_globalFlags));
    memset(&g_actor, 0, sizeof(g_actor));
    memset(&g_owner, 0, sizeof(g_owner));
    memset(&g_initFunction, 0, sizeof(g_initFunction));
    memset(&g_loopFunction, 0, sizeof(g_loopFunction));
    memset(&g_instance, 0, sizeof(g_instance));
    g_intentCount = 0;
    g_initResult = -1;
    g_waitResult = -1;
    g_elapsedResult = -1;
    g_failures = 0;
    g_activeCase = caseName;
    g_actor.stableId = 101;
    g_owner.stableId = 202;
    g_mainActors[0] = &g_actor.object;
    g_mainActors[1] = &g_owner.object;

    g_loopFunction.funcPtr = Activateur_avec_SF_Acs_loop;
    g_initFunction.funcPtr = Activateur_avec_SF_Acs_init;
    g_instance.instanceVarDataCurrent = (byte*)calloc(1, sizeof(tScriptStruct_ActivateurAvecSF));
    if (!g_instance.instanceVarDataCurrent)
    {
        fprintf(stderr, "Unable to allocate synthetic callback vars.\n");
        exit(2);
    }

    AI_gpst_CurrentInstance = &g_instance;
    AI_gpst_CurrentGameObject = &g_owner.object;
    AI_gpst_CurrentFunction = &g_initFunction;
    AI_gul_CurrentTrack = 0;
    AI_gppst_CurrentJumpNode = &g_jumpNode;
    g_jumpNode = 0;
    TIM_gf_MainClock = 10.25f;
    TIM_gf_LockTime = 0.25f;
}

static void finish_fixture(void)
{
    free(g_instance.instanceVarDataCurrent);
    g_instance.instanceVarDataCurrent = 0;
}

static tScriptStruct_ActivateurAvecSF* current_vars(void)
{
    return (tScriptStruct_ActivateurAvecSF*)g_instance.instanceVarDataCurrent;
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
    return (g_globalFlags[wordIndex] & (1u << (unsignedFlag & 0x1Fu))) != 0;
}

static int object_id(tGameObjectMain* object)
{
    if (object == &g_actor.object)
        return g_actor.stableId;
    if (object == &g_owner.object)
        return g_owner.stableId;

    fail("callback emitted an intent for an object outside this fixture");
    return -1;
}

static void add_intent(CommandIntent intent)
{
    if (g_intentCount >= INTENT_CAPACITY)
    {
        fail("intent buffer capacity exceeded");
        return;
    }

    g_intents[g_intentCount++] = intent;
}

/* These adapters follow the cited Iris primitive semantics and fail closed
 * outside the single synthetic function/owner/object fixture. */
unsigned int AI_ul_RealResolveOneRefFunction(unsigned int key)
{
    if (key != 0x3500A7A4u)
        return 0;
    return (unsigned int)&g_loopFunction;
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
    if (!function || function != &g_loopFunction || !AI_gpst_CurrentInstance ||
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

    if (object != &g_actor.object && object != &g_owner.object)
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

static int validate_intent(int index, IntentType type, int targetId,
    unsigned int addMask, unsigned int removeMask, int value)
{
    if (index < 0 || index >= g_intentCount)
        return 0;

    return g_intents[index].type == type &&
        g_intents[index].targetId == targetId &&
        g_intents[index].addMask == addMask &&
        g_intents[index].removeMask == removeMask &&
        g_intents[index].value == value;
}

static void print_intents(void)
{
    int intentIndex;

    printf("{\"case\":\"%s\",\"results\":[%d,", g_activeCase, g_initResult);
    if (g_waitResult >= 0)
        printf("%d,", g_waitResult);
    else
        printf("null,");
    if (g_elapsedResult >= 0)
        printf("%d],", g_elapsedResult);
    else
        printf("null],");
    printf("\"callback_slot_is_loop\":%s,\"jump_sentinel\":%s,\"intents\":[",
        g_instance.instanceCallBacks[0] == &g_loopFunction ? "true" : "false",
        g_instance.instanceJumpNodes[0] == (tAINode*)-1 ? "true" : "false");

    for (intentIndex = 0; intentIndex < g_intentCount; ++intentIndex)
    {
        CommandIntent* intent = &g_intents[intentIndex];
        if (intentIndex)
            printf(",");
        if (intent->type == IntentFlagsControlSet)
            printf("{\"type\":\"FlagsControlSet\",\"target\":%d,\"add\":%u,\"remove\":%u}",
                intent->targetId, intent->addMask, intent->removeMask);
        else
            printf("{\"type\":\"FlagInactiveSet\",\"target\":%d,\"value\":%d}",
                intent->targetId, intent->value);
    }
    printf("]}\n");
}

static int run_positive_clear_case(void)
{
    int initResult;
    int waitResult;
    int elapsedResult;

    reset_fixture("positive-clear");
    current_vars()->des_i_SpFlag = 37;
    current_vars()->des_i_SpFlag_etat = 1;
    current_vars()->des_o_acteur = 1;
    current_vars()->des_i_control_flags = 0x02000000;
    current_vars()->des_f_frequence_test = 2.0f;
    g_globalFlags[37 >> 5] |= (int)(1u << (37 & 31));

    initResult = Activateur_avec_SF_Acs_init();
    g_initResult = initResult;
    require_true(initResult == RESULT_PENDING, "init should return 5 after switching to loop");
    require_true(current_vars()->f_temps == 10.0f, "init should capture MainClock minus LockTime");
    require_true(g_instance.instanceCallBacks[0] == &g_loopFunction, "init should replace callback slot with loop");
    require_true(g_instance.instanceJumpNodes[0] == (tAINode*)-1, "compiled loop should receive the native jump sentinel");
    require_true(g_intentCount == 2, "matching flag should emit control and inactive intents");
    require_true(validate_intent(0, IntentFlagsControlSet, g_actor.stableId, 0, 0x02000000u, 0),
        "actor reference 1 should resolve through AI_MainActor and emit a clear/remove mask");
    require_true(validate_intent(1, IntentFlagInactiveSet, g_owner.stableId, 0, 0, 1),
        "matching flag should emit owner inactive intent");

    TIM_gf_MainClock = 10.75f;
    waitResult = Activateur_avec_SF_Acs_loop();
    g_waitResult = waitResult;
    require_true(waitResult == RESULT_UNCHANGED, "loop should return 1000 while interval has not elapsed");
    require_true(g_jumpNode == (tAINode*)-1, "nonelapsed loop should set the current jump node to -1");
    require_true(g_intentCount == 2, "nonelapsed loop should emit no new intents");

    TIM_gf_MainClock = 12.25f;
    elapsedResult = Activateur_avec_SF_Acs_loop();
    g_elapsedResult = elapsedResult;
    require_true(elapsedResult == RESULT_UNCHANGED, "elapsed loop should return 1000");
    require_true(current_vars()->f_temps == 12.0f, "elapsed loop should reset its timestamp");
    require_true(g_intentCount == 4, "elapsed matching loop should repeat both intents");
    require_true(validate_intent(2, IntentFlagsControlSet, g_actor.stableId, 0, 0x02000000u, 0),
        "elapsed loop should repeat the positive clear mask");
    require_true(validate_intent(3, IntentFlagInactiveSet, g_owner.stableId, 0, 0, 1),
        "elapsed loop should repeat owner inactive intent");
    print_intents();
    finish_fixture();
    return g_failures == 0;
}

static int run_negative_set_case(void)
{
    int result;

    reset_fixture("negative-set");
    current_vars()->des_i_SpFlag = 46;
    current_vars()->des_i_SpFlag_etat = 1;
    current_vars()->des_o_acteur = 0;
    current_vars()->des_i_control_flags = -0x04000000;
    current_vars()->des_f_frequence_test = 1.0f;
    g_globalFlags[46 >> 5] |= (int)(1u << (46 & 31));

    result = Activateur_avec_SF_Acs_init();
    g_initResult = result;
    require_true(result == RESULT_PENDING, "init should return 5 after switching to loop");
    require_true(validate_intent(0, IntentFlagsControlSet, g_owner.stableId, 0x04000000u, 0, 0),
        "negative control mask should resolve null actor fallback and emit a set/add mask");
    require_true(validate_intent(1, IntentFlagInactiveSet, g_owner.stableId, 0, 0, 1),
        "matching flag should emit inactive intent on owner");

    TIM_gf_MainClock = 11.25f;
    result = Activateur_avec_SF_Acs_loop();
    g_elapsedResult = result;
    require_true(result == RESULT_UNCHANGED, "elapsed loop should return 1000");
    require_true(validate_intent(2, IntentFlagsControlSet, g_owner.stableId, 0x04000000u, 0, 0),
        "elapsed loop should retain the negative set mask");
    require_true(g_intentCount == 4, "elapsed loop should emit one control and one inactive intent");
    print_intents();
    finish_fixture();
    return g_failures == 0;
}

static int run_saved_flag_mismatch_case(void)
{
    int result;

    reset_fixture("saved-flag-mismatch");
    current_vars()->des_i_SpFlag = 58;
    current_vars()->des_i_SpFlag_etat = 1;
    current_vars()->des_o_acteur = (int)&g_actor.object;
    current_vars()->des_i_control_flags = 0x01000000;
    current_vars()->des_f_frequence_test = 1.0f;

    result = Activateur_avec_SF_Acs_init();
    g_initResult = result;
    require_true(result == RESULT_PENDING, "init should switch to loop even if saved flag mismatches");
    require_true(g_intentCount == 0, "mismatching saved flag should emit no state intents");

    TIM_gf_MainClock = 11.25f;
    result = Activateur_avec_SF_Acs_loop();
    g_elapsedResult = result;
    require_true(result == RESULT_UNCHANGED, "elapsed mismatching loop should return 1000");
    require_true(g_intentCount == 0, "mismatching saved flag should remain side-effect free");
    require_true(current_vars()->f_temps == 11.0f, "elapsed mismatch still resets the timer");
    print_intents();
    finish_fixture();
    return g_failures == 0;
}

static int run_same_track_case(void)
{
    int result;

    reset_fixture("init-same-track");
    current_vars()->des_i_SpFlag = 58;
    current_vars()->des_i_SpFlag_etat = 0;
    current_vars()->des_o_acteur = (int)&g_actor.object;
    current_vars()->des_i_control_flags = 0;
    current_vars()->des_f_frequence_test = 1.0f;
    AI_gpst_CurrentFunction = &g_loopFunction;
    g_instance.instanceCallBacks[0] = &g_loopFunction;

    result = Activateur_avec_SF_Acs_init();
    g_initResult = result;
    require_true(result == RESULT_UNCHANGED, "selecting the already-current loop should return 1000");
    require_true(g_instance.instanceCallBacks[0] == &g_loopFunction, "same-track call should preserve existing callback slot");
    require_true(g_intentCount == 2, "init actions before same-track selection should still be captured");
    require_true(validate_intent(0, IntentFlagsControlSet, g_actor.stableId, 0, 0, 0),
        "zero control mask should still preserve the source call intent");
    require_true(validate_intent(1, IntentFlagInactiveSet, g_owner.stableId, 0, 0, 1),
        "matching clear-state should emit inactive intent");
    print_intents();
    finish_fixture();
    return g_failures == 0;
}

int main(void)
{
    int success = 1;

    success &= run_positive_clear_case();
    success &= run_negative_set_case();
    success &= run_saved_flag_mismatch_case();
    success &= run_same_track_case();

    if (!success || g_failures)
        return 1;
    return 0;
}
