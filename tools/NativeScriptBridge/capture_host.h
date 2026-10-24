#ifndef NATIVE_SCRIPT_CAPTURE_HOST_H
#define NATIVE_SCRIPT_CAPTURE_HOST_H

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

typedef struct sCaptureHostContext
{
    int failures;
    CommandIntent intents[INTENT_CAPACITY];
    int intentCount;
    const char* activeCase;
    SyntheticObject actor;
    SyntheticObject owner;
    tGameObjectMain* mainActors[2];
    int globalFlags[FLAG_WORD_COUNT];
    tAIFunction initFunction;
    tAIFunction loopFunction;
    tAI instance;
    tAINode* jumpNode;
} CaptureHostContext;

extern CaptureHostContext g_host;
int Activateur_avec_SF_Acs_init(void);
int Activateur_avec_SF_Acs_loop(void);
void host_reset_fixture(const char* caseName);
void finish_fixture(void);
tScriptStruct_ActivateurAvecSF* current_vars(void);

#endif
