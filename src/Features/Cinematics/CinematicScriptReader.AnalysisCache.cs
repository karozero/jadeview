using System.Runtime.CompilerServices;

namespace JadeView;

internal static partial class CinematicScriptReader
{
    private static readonly ConditionalWeakTable<CinematicScriptOwner, OwnerAnalysis> OwnerAnalyses = new();

    private static OwnerAnalysis GetOwnerAnalysis(CinematicScriptOwner owner)
    {
        OwnerAnalysis analysis = OwnerAnalyses.GetValue(owner, current => new(current));
        if (analysis.Matches(owner)) return analysis;
        OwnerAnalyses.Remove(owner);
        return OwnerAnalyses.GetValue(owner, current => new(current));
    }

    /// <summary>Static source reachability is shared across frame evaluations, never runtime field values.</summary>
    private sealed class OwnerAnalysis
    {
        private readonly string[] callbackNames;
        private readonly string[] modelFunctionNames;
        internal Lazy<CinematicScriptFunctionPath[]> CallbackPaths { get; }
        internal Lazy<CinematicScriptMessageReader[]> MessageReaders { get; }
        internal Lazy<CinematicScriptReachableOvaFieldAccess[]> OvaFieldAccesses { get; }

        internal OwnerAnalysis(CinematicScriptOwner owner)
        {
            callbackNames = owner.CallbackNames.ToArray();
            modelFunctionNames = owner.ModelFunctionNames.ToArray();
            CallbackPaths = new(() => ReadNestedCallbackPaths(owner));
            MessageReaders = new(() => ReadReachableMessageReaders(owner));
            OvaFieldAccesses = new(() => ReadReachableOvaFieldAccesses(owner));
        }

        internal bool Matches(CinematicScriptOwner owner) =>
            callbackNames.SequenceEqual(owner.CallbackNames, StringComparer.Ordinal)
            && modelFunctionNames.SequenceEqual(owner.ModelFunctionNames, StringComparer.Ordinal);
    }
}
