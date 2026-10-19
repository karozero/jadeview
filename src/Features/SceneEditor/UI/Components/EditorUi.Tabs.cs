using System.Text;
using ImGuiNET;

namespace JadeView;

internal static partial class EditorUi
{
    internal static unsafe bool BeginTabItemWithFlags(string label, ImGuiTabItemFlags flags)
    {
        int byteCount = Encoding.UTF8.GetByteCount(label);
        byte* utf8 = stackalloc byte[byteCount + 1];
        fixed (char* chars = label)
        {
            Encoding.UTF8.GetBytes(chars, label.Length, utf8, byteCount);
        }
        utf8[byteCount] = 0;
        return ImGuiNative.igBeginTabItem(utf8, null, flags) != 0;
    }

}
