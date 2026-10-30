using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace UsbHub.App.Native;

// Version 4 supplies WM_CONTEXTMENU for right-click and keyboard context requests.
internal sealed class TrayIcon : IDisposable
{
    internal const int CallbackMessage = 0x8001;
    private readonly HwndSource _source;
    private readonly Icon _icon;
    private readonly Guid _id;
    private readonly bool _registerIcon;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private string _text;

    public event Action? OpenRequested;
    public event Action<Rectangle>? ContextRequested;
    internal IntPtr Handle => _source.Handle;

    public TrayIcon(Guid id, Icon icon, string text, bool registerIcon = true)
    {
        _id = id; _icon = (Icon)icon.Clone(); _text = text; _registerIcon = registerIcon;
        _source = new HwndSource(new HwndSourceParameters("usbHub.Tray")
        {
            Width = 1, Height = 1, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80
        });
        _source.AddHook(WindowMessage);
        Add();
    }

    private NotifyIconData Data(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = 1,
        Flags = flags | 0x20, Callback = CallbackMessage, Icon = _icon.Handle, Guid = _id,
        Tip = _text.Length > 127 ? _text[..126] + "…" : _text, Info = "", InfoTitle = ""
    };

    private void Add()
    {
        if (!_registerIcon) return;
        var data = Data(0x01 | 0x02 | 0x04);
        Shell_NotifyIcon(0, ref data);
        data.Version = 4;
        Shell_NotifyIcon(4, ref data);
    }

    public void SetText(string text)
    {
        _text = text;
        var data = Data(0x04);
        Shell_NotifyIcon(1, ref data);
    }

    public void ShowNotification(string title, string body)
    {
        var data = Data(0x10);
        data.InfoTitle = title.Length > 63 ? title[..63] : title;
        data.Info = body.Length > 255 ? body[..254] + "…" : body;
        data.InfoFlags = 0x02 | 0x80; // Warning; respect Windows quiet time.
        Shell_NotifyIcon(1, ref data);
    }

    internal bool TryGetBounds(out Rectangle rectangle)
    {
        rectangle = Rectangle.Empty;
        if (!_registerIcon) return false;
        var identifier = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = _source.Handle, Id = 1, Guid = _id };
        var result = Shell_NotifyIconGetRect(ref identifier, out var bounds);
        rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        return result == 0 && rectangle.Width > 0 && rectangle.Height > 0;
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if ((uint)message == _taskbarCreated) Add();
        if (message != CallbackMessage) return IntPtr.Zero;
        handled = true;
        switch ((int)(lparam.ToInt64() & 0xFFFF))
        {
            case 0x0400: // NIN_SELECT
            case 0x0401: // NIN_KEYSELECT
            case 0x0203: // WM_LBUTTONDBLCLK
            case 0x0405: // NIN_BALLOONUSERCLICK
                OpenRequested?.Invoke(); break;
            case 0x007B: // WM_CONTEXTMENU
                if (!TryGetBounds(out var rectangle))
                {
                    var point = System.Windows.Forms.Cursor.Position;
                    rectangle = new Rectangle(point.X, point.Y, 1, 1);
                }
                ContextRequested?.Invoke(rectangle);
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        var data = Data(0);
        if (_registerIcon) Shell_NotifyIcon(2, ref data);
        _source.RemoveHook(WindowMessage); _source.Dispose(); _icon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyIconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
