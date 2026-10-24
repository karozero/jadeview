namespace JadeView;

internal sealed record CinematicScriptFunctionCall(string To, string Dispatch, int? TrackIndex,
    string? IndirectField = null, string? AssignmentFunction = null, string? AssignmentSource = null,
    int? AssignmentLine = null, string? ReferenceVariable = null, string? ResolvedFunctionKey = null);

internal sealed record CinematicScriptFunctionPath(string FunctionName, string Path);

internal sealed record CinematicScriptCallbackDispatchSite(int CallbackId, string Api,
    string Source, int Line, string Call, string EnclosingFunction, string ExecutionContext,
    string ContextEvidence, string DispatcherEligibility, string CallerEligibility,
    string TargetResolution);

internal sealed record CinematicWorldSchedulerOperation(string Operation, string Arguments,
    string[] ArgumentSources, string Source, int Line);

internal sealed record CinematicScriptWorldSchedulerOperation(string FunctionName, string Operation,
    string Arguments, string[] ArgumentSources, string Source, int Line, string CallbackPath);

internal sealed record CinematicScriptEngineCallSite(string Api, string Variant, string[] Arguments,
    CinematicScriptArgumentAssignmentSource[] ArgumentAssignments,
    CinematicScriptArgumentFieldReference[] ArgumentFields,
    CinematicScriptMessageFieldSource[] MessageFields,
    CinematicScriptBranchConditionSource[] BranchConditions, string Source, int Line,
    CinematicScriptLoopContextSource[]? LoopContexts = null,
    CinematicScriptArgumentAssignmentAlternative[]? ArgumentAssignmentAlternatives = null,
    CinematicScriptOvaFieldWrite[]? OvaFieldWrites = null,
    CinematicScriptMessageFieldWrite[]? MessageFieldWrites = null);

internal sealed record CinematicScriptReachableEngineCall(string FunctionName, string Api, string Variant,
    string[] Arguments, CinematicScriptArgumentAssignmentSource[] ArgumentAssignments,
    CinematicScriptArgumentFieldReference[] ArgumentFields, CinematicScriptMessageFieldSource[] MessageFields,
    CinematicScriptBranchConditionSource[] BranchConditions, string Source, int Line, string CallbackPath,
    CinematicScriptLoopContextSource[]? LoopContexts = null,
    CinematicScriptArgumentAssignmentAlternative[]? ArgumentAssignmentAlternatives = null,
    CinematicScriptOvaFieldWrite[]? OvaFieldWrites = null,
    CinematicScriptMessageFieldWrite[]? MessageFieldWrites = null);

internal sealed record CinematicScriptMessageReader(int MessageId, string FunctionName,
    string Source, int Line, string CallbackPath, string ReadApi, string QueueObject);

internal sealed record CinematicScriptMessageReaderSite(int MessageId, string Source, int Line,
    string QueueObject, string ReadApi);

internal sealed record CinematicScriptArgumentAssignmentSource(string Variable, string Expression, int Line,
    int Depth, bool IsUnique = true);

internal sealed record CinematicScriptArgumentAssignmentAlternative(string Variable, string Expression,
    int Line, CinematicScriptBranchConditionSource[] BranchConditions);

internal sealed record CinematicScriptOvaFieldWrite(string VariableName, string Expression, int Line,
    CinematicScriptBranchConditionSource[] BranchConditions);

internal sealed record CinematicScriptArgumentFieldReference(string Name, string? AliasVariable,
    int? AssignmentLine);

internal sealed record CinematicScriptMessageFieldSource(string Name, string Expression, int Line);

internal sealed record CinematicScriptMessageFieldWrite(string Name, string Expression, int Line,
    CinematicScriptBranchConditionSource[] BranchConditions);

internal sealed record CinematicScriptBranchConditionSource(string Kind, string Expression, int Line);

internal sealed record CinematicScriptLoopContextSource(string Kind, string Expression, int Line,
    bool InductionVariableModified = false);

internal sealed record CinematicScriptEngineCallSummary(string Api, string Variant, int CallSiteCount,
    CinematicScriptReachableEngineCall[] Examples);

internal sealed record CinematicScriptSourceReferenceSite(string Kind, string Symbol, string Key,
    string Source, int Line);

internal sealed record CinematicScriptOvaFieldAccessSite(string VariableName, string Access,
    string Source, string Receiver, string Expression,
    CinematicScriptBranchConditionSource[] BranchConditions, string SourceFile, int Line);

internal sealed record CinematicScriptReachableOvaFieldAccess(string VariableName, string Access,
    string Source, string Receiver, string Expression,
    CinematicScriptBranchConditionSource[] BranchConditions, string SourceFile, int Line,
    string FunctionName, string CallbackPath);

internal sealed record CinematicScriptReachableSourceReference(string FunctionName, string Kind, string Symbol,
    uint Key, string Source, int Line, string CallbackPath);

internal sealed record CinematicScriptOvaFieldBinding(string VariableName, string Source, string Value,
    uint? ObjectKey, string? TargetName);
