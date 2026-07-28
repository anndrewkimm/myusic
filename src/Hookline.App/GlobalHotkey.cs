using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Hookline.App;

internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x484C;
    private const int HotkeyMessage = 0x0312;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint VirtualKeyH = 0x48;

    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    public GlobalHotkey()
    {
        var parameters = new HwndSourceParameters(
            "Hookline.GlobalHotkey"
        )
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
    }

    public event EventHandler? Pressed;

    public bool TryRegister()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registered)
        {
            return true;
        }

        _registered = RegisterHotKey(
            _source.Handle,
            HotkeyId,
            ModifierControl | ModifierAlt,
            VirtualKeyH
        );
        return _registered;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _disposed = true;
    }

    private IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled
    )
    {
        if (
            message == HotkeyMessage
            && wordParameter.ToInt32() == HotkeyId
        )
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr window,
        int id,
        uint modifiers,
        uint virtualKey
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
