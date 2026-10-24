#ifndef JADEVIEW_NATIVE_SCRIPT_LAYOUT_CONTRACT_H
#define JADEVIEW_NATIVE_SCRIPT_LAYOUT_CONTRACT_H

/* Include after Iris AI declarations. These are measured x86 host-layout
 * prerequisites, not a claim that all retail serialized types share this ABI.
 * Use typedef assertions so the checks also work with MSVC's default C mode. */
typedef char NativeScriptPointerMustBe32Bit[(sizeof(void*) == 4) ? 1 : -1];
typedef char NativeScriptNodeMustBe8Bytes[(sizeof(tAINode) == 8) ? 1 : -1];
typedef char NativeScriptFunctionMustBe24Bytes[(sizeof(tAIFunction) == 24) ? 1 : -1];
typedef char NativeScriptVariableDescriptorMustBe12Bytes[(sizeof(AIVarElem) == 12) ? 1 : -1];
typedef char NativeScriptVariableNameMustBe30Bytes[(sizeof(AIVarNameElem) == 30) ? 1 : -1];
typedef char NativeScriptInstanceMustHaveFiveCallbackSlots[
    (sizeof(((tAI*)0)->instanceCallBacks) / sizeof(tAIFunction*) == 5) ? 1 : -1];

#endif
