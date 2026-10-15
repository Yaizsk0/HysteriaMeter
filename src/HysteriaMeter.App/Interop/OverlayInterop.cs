using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace HysteriaMeter.App.Interop;

/// <summary>Click-through del overlay y hotkey global para activarlo/desactivarlo.</summary>
internal static partial class OverlayInterop
{
    public const int HotKeyMessage = 0x0312;

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;

    public const uint ModNoRepeat = 0x4000;

    public static void SetClickThrough(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style = enabled
            ? style | WsExTransparent | WsExLayered
            : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow));
    }

    public static bool RegisterHotKey(Window window, int id, uint modifiers, Key key) =>
        RegisterHotKeyNative(new WindowInteropHelper(window).Handle, id, modifiers | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key));

    public static void UnregisterHotKey(Window window, int id) =>
        UnregisterHotKeyNative(new WindowInteropHelper(window).Handle, id);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKeyNative(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKeyNative(IntPtr hWnd, int id);
}
