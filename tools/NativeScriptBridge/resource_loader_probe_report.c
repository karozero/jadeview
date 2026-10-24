#include "resource_loader_probe_host.h"

void write_key_line(const char* roleName, int key)
{
    printf("%s=%08X\n", roleName, (unsigned int)key);
}

int verify_reference(int index, int expectedKey, void* expectedCallback, void** expectedDestination, byte expectedFlags)
{
    loaStackElem* queuedReference;

    queuedReference = &LOA_gast_Stack[index];
    return queuedReference->key == expectedKey &&
        queuedReference->funcPtr == expectedCallback &&
        queuedReference->retValPtr == expectedDestination &&
        queuedReference->flags == expectedFlags;
}

int report_vars_resource(const char* label, const tAIVars* variables, int firstReference, int lastReference)
{
    unsigned int variableIndex;
    unsigned int callbackIndex;
    int referenceIndex;
    int matchedSlot;
    unsigned int storageByte;

    printf("OVA_BEGIN=%s|%u|%u\n", label, variables->varNum, variables->varFullSizeToAlloc);
    for (variableIndex = 0; variableIndex < variables->varNum; ++variableIndex)
    {
        const AIVarElem* variable;
        unsigned int value;

        variable = &variables->variables[variableIndex];
        value = 0;
        if (variable->varOffset >= 0 &&
            (unsigned int)variable->varOffset + sizeof(value) <= (unsigned int)variables->varFullSizeToAlloc)
            memcpy(&value, variables->varData + variable->varOffset, sizeof(value));
        printf("OVA_VAR=%s|%u|%s|%u|%u|%u|%04X|%08X\n",
            label,
            variableIndex,
            variables->varNames[variableIndex].varName,
            (unsigned short)variable->variableType,
            variable->varOffset,
            variable->variableArrayCount,
            (unsigned short)variable->flags,
            value);
    }

    for (referenceIndex = firstReference; referenceIndex < lastReference; ++referenceIndex)
    {
        loaStackElem* queuedReference;

        queuedReference = &LOA_gast_Stack[referenceIndex % LOADER_REFERENCE_CAPACITY];
        if (queuedReference->funcPtr != (void* (*)(uint32_t))AI_ul_CallbackLoadFunction || queuedReference->flags != 1)
            return 0;
        matchedSlot = -1;
        for (callbackIndex = 0; callbackIndex < NATIVE_CALLBACK_SLOT_COUNT; ++callbackIndex)
        {
            if (queuedReference->retValPtr == (void**)&variables->callBacks[callbackIndex])
            {
                matchedSlot = (int)callbackIndex;
                break;
            }
        }
        if (matchedSlot >= 0)
        {
            printf("OVA_CALLBACK=%s|%d|%08X|%u|%u\n",
                label, matchedSlot, (unsigned int)queuedReference->key, queuedReference->flags, referenceIndex);
            continue;
        }

        for (callbackIndex = 0; callbackIndex < (unsigned int)variables->instanceFuncNum; ++callbackIndex)
        {
            if (queuedReference->retValPtr == (void**)&variables->instanceFuncs[callbackIndex])
            {
                matchedSlot = (int)callbackIndex;
                break;
            }
        }
        if (matchedSlot >= 0)
            printf("OVA_INSTANCE_FUNCTION=%s|%d|%08X|%u|%u\n",
                label, matchedSlot, (unsigned int)queuedReference->key, queuedReference->flags, referenceIndex);
        else
            return 0;
    }

    printf("OVA_STORAGE=%s|", label);
    for (storageByte = 0; storageByte < variables->varFullSizeToAlloc; ++storageByte)
        printf("%02X", variables->varData[storageByte]);
    printf("\nOVA_END=%s\n", label);
    return 1;
}
