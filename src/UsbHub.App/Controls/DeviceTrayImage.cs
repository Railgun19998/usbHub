using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UsbHub.App.Controls;

// Simple silhouettes stay legible at notification-area sizes.
internal static class DeviceTrayImage
{
    internal static ImageSource CreateSource(string kind)
    {
        using var icon = Create(kind);
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    internal static System.Drawing.Icon Create(string kind)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var ink = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(72, 132, 121));
            var line = new Pen(ink, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            switch (kind)
            {
                case "mouse":
                    dc.DrawRoundedRectangle(null, line, new Rect(7, 2, 18, 28), 9, 9);
                    dc.DrawLine(line, new Point(16, 3), new Point(16, 11));
                    dc.DrawRoundedRectangle(ink, null, new Rect(14.5, 7, 3, 6), 1.5, 1.5);
                    break;
                case "keyboard":
                    dc.DrawRoundedRectangle(null, line, new Rect(1.5, 7, 29, 19), 3, 3);
                    for (var row = 0; row < 2; row++) for (var col = 0; col < 6; col++)
                        dc.DrawRectangle(ink, null, new Rect(5 + col * 4, 11 + row * 4, 2, 2));
                    dc.DrawLine(line, new Point(10, 22), new Point(22, 22));
                    break;
                case "headphones":
                    dc.DrawGeometry(null, line, Geometry.Parse("M5,20 L5,14 C5,-1 27,-1 27,14 L27,20"));
                    dc.DrawRoundedRectangle(ink, null, new Rect(2, 15, 7, 14), 3, 3);
                    dc.DrawRoundedRectangle(ink, null, new Rect(23, 15, 7, 14), 3, 3);
                    break;
                case "xbox-controller":
                    dc.DrawGeometry(null, line, Geometry.Parse("M8,8 C3,8 0,24 3,27 C6,29 10,21 12,21 L20,21 C22,21 26,29 29,26 C32,24 29,8 24,8 Z"));
                    dc.DrawEllipse(ink, null, new Point(8, 13), 3, 3);
                    dc.DrawEllipse(ink, null, new Point(20, 21), 2.8, 2.8);
                    dc.DrawLine(line, new Point(11, 19), new Point(11, 24));
                    dc.DrawLine(line, new Point(8.5, 21.5), new Point(13.5, 21.5));
                    foreach (var point in new[] { new Point(24, 11), new Point(27, 14), new Point(21, 14), new Point(24, 17) })
                        dc.DrawEllipse(ink, null, point, 1.1, 1.1);
                    break;
                case "dualsense-controller":
                    dc.DrawGeometry(null, line, Geometry.Parse("M7,7 C2,10 -1,25 3,28 C6,30 10,23 12,22 L20,22 C22,23 26,30 29,28 C33,25 30,10 25,7 Z"));
                    dc.DrawRoundedRectangle(null, new Pen(ink, 1.3), new Rect(11, 8, 10, 7), 2, 2);
                    dc.DrawLine(line, new Point(6, 11), new Point(6, 17));
                    dc.DrawLine(line, new Point(3, 14), new Point(9, 14));
                    dc.DrawEllipse(ink, null, new Point(12, 21), 2.7, 2.7);
                    dc.DrawEllipse(ink, null, new Point(20, 21), 2.7, 2.7);
                    foreach (var point in new[] { new Point(25, 11), new Point(28, 14), new Point(22, 14), new Point(25, 17) })
                        dc.DrawEllipse(ink, null, point, 1.1, 1.1);
                    break;
                case "controller":
                    dc.DrawGeometry(null, line, Geometry.Parse("M8,8 C3,8 0,24 3,26 C6,29 10,21 12,21 L20,21 C22,21 26,29 29,26 C32,24 29,8 24,8 Z"));
                    dc.DrawLine(line, new Point(7, 12), new Point(7, 19)); dc.DrawLine(line, new Point(3.5, 15.5), new Point(10.5, 15.5));
                    dc.DrawEllipse(ink, null, new Point(22, 13), 1.5, 1.5); dc.DrawEllipse(ink, null, new Point(26, 17), 1.5, 1.5);
                    break;
                default:
                    dc.DrawRoundedRectangle(null, line, new Rect(9, 10, 14, 20), 3, 3);
                    dc.DrawRectangle(null, line, new Rect(11, 2, 10, 8));
                    dc.DrawLine(line, new Point(14, 5), new Point(14, 7)); dc.DrawLine(line, new Point(18, 5), new Point(18, 7));
                    break;
            }
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
        using var drawing = new System.Drawing.Bitmap(stream);
        var handle = drawing.GetHicon();
        try { using var source = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)source.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
