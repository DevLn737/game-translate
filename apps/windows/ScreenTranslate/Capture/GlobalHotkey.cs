using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenTranslate.Capture;

public sealed class GlobalHotkey : IDisposable
{
    private const int DefaultHotkeyId = 0x4C44;
    private const int WmHotkey = 0x0312;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private readonly int _hotkeyId;
    private HotkeyBinding? _registeredBinding;
    private bool _disposed;

    public GlobalHotkey(IntPtr windowHandle, int hotkeyId = DefaultHotkeyId)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _hotkeyId = hotkeyId;
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("The WPF window source is unavailable.");
        _source.AddHook(WindowHook);
    }

    public event EventHandler? Triggered;

    public bool TryRegister(HotkeyBinding binding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var previous = _registeredBinding;
        Unregister();

        var virtualKey = unchecked((uint)KeyInterop.VirtualKeyFromKey(binding.Key));
        if (NativeMethods.RegisterHotKey(_windowHandle, _hotkeyId, (uint)binding.Modifiers, virtualKey))
        {
            _registeredBinding = binding;
            return true;
        }

        if (previous is not null)
        {
            var previousVirtualKey = unchecked((uint)KeyInterop.VirtualKeyFromKey(previous.Key));
            if (NativeMethods.RegisterHotKey(
                    _windowHandle,
                    _hotkeyId,
                    (uint)previous.Modifiers,
                    previousVirtualKey))
            {
                _registeredBinding = previous;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _source.RemoveHook(WindowHook);
    }

    private void Unregister()
    {
        if (_registeredBinding is null)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, _hotkeyId);
        _registeredBinding = null;
    }

    private IntPtr WindowHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == _hotkeyId)
        {
            handled = true;
            Triggered?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
