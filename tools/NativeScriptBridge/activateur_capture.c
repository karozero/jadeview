/* Assertion-backed scenarios for the private synthetic Iris callback host. */
#include <stdio.h>
#include "capture_host.h"

static int g_initResult;
static int g_waitResult;
static int g_elapsedResult;

static void require_true(int condition, const char* message)
{
    if (!condition)
    {
        fprintf(stderr, "FAIL[%s]: %s\n", g_host.activeCase, message);
        ++g_host.failures;
    }
}

static void reset_fixture(const char* caseName)
{
    host_reset_fixture(caseName);
    g_initResult = -1;
    g_waitResult = -1;
    g_elapsedResult = -1;
}

static int validate_intent(int index, IntentType type, int targetId,
    unsigned int addMask, unsigned int removeMask, int value)
{
    if (index < 0 || index >= g_host.intentCount)
        return 0;

    return g_host.intents[index].type == type &&
        g_host.intents[index].targetId == targetId &&
        g_host.intents[index].addMask == addMask &&
        g_host.intents[index].removeMask == removeMask &&
        g_host.intents[index].value == value;
}

static void print_intents(void)
{
    int intentIndex;

    printf("{\"case\":\"%s\",\"results\":[%d,", g_host.activeCase, g_initResult);
    if (g_waitResult >= 0)
        printf("%d,", g_waitResult);
    else
        printf("null,");
    if (g_elapsedResult >= 0)
        printf("%d],", g_elapsedResult);
    else
        printf("null],");
    printf("\"callback_slot_is_loop\":%s,\"jump_sentinel\":%s,\"intents\":[",
        g_host.instance.instanceCallBacks[0] == &g_host.loopFunction ? "true" : "false",
        g_host.instance.instanceJumpNodes[0] == (tAINode*)-1 ? "true" : "false");

    for (intentIndex = 0; intentIndex < g_host.intentCount; ++intentIndex)
    {
        CommandIntent* intent = &g_host.intents[intentIndex];
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
    g_host.globalFlags[37 >> 5] |= (int)(1u << (37 & 31));

    initResult = Activateur_avec_SF_Acs_init();
    g_initResult = initResult;
    require_true(initResult == RESULT_PENDING, "init should return 5 after switching to loop");
    require_true(current_vars()->f_temps == 10.0f, "init should capture MainClock minus LockTime");
    require_true(g_host.instance.instanceCallBacks[0] == &g_host.loopFunction, "init should replace callback slot with loop");
    require_true(g_host.instance.instanceJumpNodes[0] == (tAINode*)-1, "compiled loop should receive the native jump sentinel");
    require_true(g_host.intentCount == 2, "matching flag should emit control and inactive intents");
    require_true(validate_intent(0, IntentFlagsControlSet, g_host.actor.stableId, 0, 0x02000000u, 0),
        "actor reference 1 should resolve through AI_MainActor and emit a clear/remove mask");
    require_true(validate_intent(1, IntentFlagInactiveSet, g_host.owner.stableId, 0, 0, 1),
        "matching flag should emit owner inactive intent");

    TIM_gf_MainClock = 10.75f;
    waitResult = Activateur_avec_SF_Acs_loop();
    g_waitResult = waitResult;
    require_true(waitResult == RESULT_UNCHANGED, "loop should return 1000 while interval has not elapsed");
    require_true(g_host.jumpNode == (tAINode*)-1, "nonelapsed loop should set the current jump node to -1");
    require_true(g_host.intentCount == 2, "nonelapsed loop should emit no new intents");

    TIM_gf_MainClock = 12.25f;
    elapsedResult = Activateur_avec_SF_Acs_loop();
    g_elapsedResult = elapsedResult;
    require_true(elapsedResult == RESULT_UNCHANGED, "elapsed loop should return 1000");
    require_true(current_vars()->f_temps == 12.0f, "elapsed loop should reset its timestamp");
    require_true(g_host.intentCount == 4, "elapsed matching loop should repeat both intents");
    require_true(validate_intent(2, IntentFlagsControlSet, g_host.actor.stableId, 0, 0x02000000u, 0),
        "elapsed loop should repeat the positive clear mask");
    require_true(validate_intent(3, IntentFlagInactiveSet, g_host.owner.stableId, 0, 0, 1),
        "elapsed loop should repeat owner inactive intent");
    print_intents();
    finish_fixture();
    return g_host.failures == 0;
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
    g_host.globalFlags[46 >> 5] |= (int)(1u << (46 & 31));

    result = Activateur_avec_SF_Acs_init();
    g_initResult = result;
    require_true(result == RESULT_PENDING, "init should return 5 after switching to loop");
    require_true(validate_intent(0, IntentFlagsControlSet, g_host.owner.stableId, 0x04000000u, 0, 0),
        "negative control mask should resolve null actor fallback and emit a set/add mask");
    require_true(validate_intent(1, IntentFlagInactiveSet, g_host.owner.stableId, 0, 0, 1),
        "matching flag should emit inactive intent on owner");

    TIM_gf_MainClock = 11.25f;
    result = Activateur_avec_SF_Acs_loop();
    g_elapsedResult = result;
    require_true(result == RESULT_UNCHANGED, "elapsed loop should return 1000");
    require_true(validate_intent(2, IntentFlagsControlSet, g_host.owner.stableId, 0x04000000u, 0, 0),
        "elapsed loop should retain the negative set mask");
    require_true(g_host.intentCount == 4, "elapsed loop should emit one control and one inactive intent");
    print_intents();
    finish_fixture();
    return g_host.failures == 0;
}

static int run_saved_flag_mismatch_case(void)
{
    int result;

    reset_fixture("saved-flag-mismatch");
    current_vars()->des_i_SpFlag = 58;
    current_vars()->des_i_SpFlag_etat = 1;
    current_vars()->des_o_acteur = (int)&g_host.actor.object;
    current_vars()->des_i_control_flags = 0x01000000;
    current_vars()->des_f_frequence_test = 1.0f;

    result = Activateur_avec_SF_Acs_init();
    g_initResult = result;
    require_true(result == RESULT_PENDING, "init should switch to loop even if saved flag mismatches");
    require_true(g_host.intentCount == 0, "mismatching saved flag should emit no state intents");

    TIM_gf_MainClock = 11.25f;
    result = Activateur_avec_SF_Acs_loop();
    g_elapsedResult = result;
    require_true(result == RESULT_UNCHANGED, "elapsed mismatching loop should return 1000");
    require_true(g_host.intentCount == 0, "mismatching saved flag should remain side-effect free");
    require_true(current_vars()->f_temps == 11.0f, "elapsed mismatch still resets the timer");
    print_intents();
    finish_fixture();
    return g_host.failures == 0;
}

static int run_same_track_case(void)
{
    int result;

    reset_fixture("init-same-track");
    current_vars()->des_i_SpFlag = 58;
    current_vars()->des_i_SpFlag_etat = 0;
    current_vars()->des_o_acteur = (int)&g_host.actor.object;
    current_vars()->des_i_control_flags = 0;
    current_vars()->des_f_frequence_test = 1.0f;
    AI_gpst_CurrentFunction = &g_host.loopFunction;
    g_host.instance.instanceCallBacks[0] = &g_host.loopFunction;

    result = Activateur_avec_SF_Acs_init();
    g_initResult = result;
    require_true(result == RESULT_UNCHANGED, "selecting the already-current loop should return 1000");
    require_true(g_host.instance.instanceCallBacks[0] == &g_host.loopFunction, "same-track call should preserve existing callback slot");
    require_true(g_host.intentCount == 2, "init actions before same-track selection should still be captured");
    require_true(validate_intent(0, IntentFlagsControlSet, g_host.actor.stableId, 0, 0, 0),
        "zero control mask should still preserve the source call intent");
    require_true(validate_intent(1, IntentFlagInactiveSet, g_host.owner.stableId, 0, 0, 1),
        "matching clear-state should emit inactive intent");
    print_intents();
    finish_fixture();
    return g_host.failures == 0;
}

int main(void)
{
    int success = 1;

    success &= run_positive_clear_case();
    success &= run_negative_set_case();
    success &= run_saved_flag_mismatch_case();
    success &= run_same_track_case();

    if (!success || g_host.failures)
        return 1;
    return 0;
}
