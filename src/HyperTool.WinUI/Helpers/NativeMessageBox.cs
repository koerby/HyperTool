using System.Runtime.InteropServices;

namespace HyperTool.WinUI.Helpers;

internal enum NativeMessageBoxButtons : uint
{
    Ok = 0x00000000,
    YesNoCancel = 0x00000003
}

internal enum NativeMessageBoxIcon : uint
{
    None = 0x00000000,
    Error = 0x00000010,
    Question = 0x00000020,
    Warning = 0x00000030,
    Information = 0x00000040
}

internal enum NativeMessageBoxResult : int
{
    Ok = 1,
    Cancel = 2,
    Yes = 6,
    No = 7
}

internal static class NativeMessageBox
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public static NativeMessageBoxResult Show(string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
    {
        var result = MessageBoxW(0, text, caption, (uint)buttons | (uint)icon);
        return Enum.IsDefined(typeof(NativeMessageBoxResult), result)
            ? (NativeMessageBoxResult)result
            : NativeMessageBoxResult.Cancel;
    }
}
