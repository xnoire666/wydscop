using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Wydscop.Interop;

namespace Wydscop.Services;

/// <summary>
/// A system-wide hotkey (works no matter which app is in front), registered with RegisterHotKey.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly int _id;
    private bool _disposed;

    public event EventHandler? Pressed;

    public bool IsRegistered { get; }

    /// <summary>Win32 error when registration failed (1409 = already registered by another app).</summary>
    public int ErrorCode { get; }

    public string DisplayName { get; }

    public GlobalHotkey(Window owner, ModifierKeys modifiers, Key key, int id = 0x4546)
    {
        _id = id;
        _hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);

        uint mods = NativeMethods.MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        IsRegistered = NativeMethods.RegisterHotKey(_hwnd, _id, mods, vk);
        if (!IsRegistered) ErrorCode = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();

        DisplayName = modifiers == ModifierKeys.None
            ? key.ToString()
            : $"{modifiers.ToString().Replace(", ", "+")}+{key}";
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsRegistered) NativeMethods.UnregisterHotKey(_hwnd, _id);
        _source.RemoveHook(WndProc);
    }
}
