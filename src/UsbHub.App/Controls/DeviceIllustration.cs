using System;
using System.Windows;
using System.Windows.Media;

namespace UsbHub.App.Controls;

public sealed class DeviceIllustration : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(DeviceIllustration), new FrameworkPropertyMetadata("device", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var scale = Math.Min(ActualWidth / 180, ActualHeight / 110);
        dc.PushTransform(new TranslateTransform((ActualWidth - 180 * scale) / 2, (ActualHeight - 110 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(190, 95, 160, 150)), 1.2);
        var body = new LinearGradientBrush(Color.FromRgb(245, 255, 253), Color.FromRgb(132, 187, 176), 70);
        var glow = new Pen(new SolidColorBrush(Color.FromRgb(18, 194, 168)), 2);
        var dark = new SolidColorBrush(Color.FromRgb(34, 101, 92));
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(65, 99, 215, 198), Colors.Transparent), null, new Point(90, 90), 64, 13);
        switch (Kind)
        {
            case "mouse":
                dc.PushTransform(new RotateTransform(-15, 90, 53));
                dc.DrawRoundedRectangle(body, outline, new Rect(62, 8, 56, 90), 27, 27);
                dc.DrawLine(outline, new Point(90, 10), new Point(90, 47));
                dc.DrawRoundedRectangle(dark, outline, new Rect(86, 27, 8, 18), 4, 4);
                dc.DrawLine(glow, new Point(79, 86), new Point(101, 86)); dc.Pop(); break;
            case "keyboard":
                dc.PushTransform(new RotateTransform(0, 90, 55));
                dc.DrawRoundedRectangle(body, outline, new Rect(11, 24, 158, 66), 10, 10);
                for (var row = 0; row < 4; row++) for (var col = 0; col < 12; col++)
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(120, 196, 225, 218)), new Pen(new SolidColorBrush(Color.FromArgb(100, 110, 175, 163)), 0.6), new Rect(19 + col * 12, 31 + row * 12, 9, 9), 2, 2);
                dc.DrawRoundedRectangle(body, null, new Rect(56, 68, 62, 9), 2, 2);
                dc.DrawLine(glow, new Point(24, 86), new Point(156, 86)); dc.Pop(); break;
            case "headphones":
                var geometry = Geometry.Parse("M 49,65 C 40,-4 142,-4 132,65");
                dc.DrawGeometry(null, new Pen(dark, 14), geometry); dc.DrawGeometry(null, new Pen(body, 9), geometry);
                dc.DrawRoundedRectangle(body, outline, new Rect(41, 50, 28, 43), 11, 11);
                dc.DrawRoundedRectangle(body, outline, new Rect(114, 50, 28, 43), 11, 11);
                dc.DrawLine(glow, new Point(46, 64), new Point(46, 78)); break;
            case "xbox-controller":
                dc.DrawGeometry(body, outline, Geometry.Parse("M42,30 C24,31 15,89 33,97 C47,102 58,77 68,77 L112,77 C124,78 135,102 149,96 C166,88 156,34 140,30 C116,23 66,23 42,30 Z"));
                DrawStick(dc, dark, outline, new Point(51, 47), 10);
                DrawStick(dc, dark, outline, new Point(111, 72), 9);
                DrawDPad(dc, dark, new Point(68, 72), 20);
                dc.DrawEllipse(dark, null, new Point(90, 36), 6, 6);
                dc.DrawLine(new Pen(body, 1.5), new Point(87, 33), new Point(93, 39));
                dc.DrawLine(new Pen(body, 1.5), new Point(93, 33), new Point(87, 39));
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(208, 183, 81)), outline, new Point(133, 40), 4, 4);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(204, 111, 116)), outline, new Point(145, 52), 4, 4);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(94, 155, 193)), outline, new Point(121, 52), 4, 4);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(90, 172, 123)), outline, new Point(133, 64), 4, 4);
                dc.DrawRoundedRectangle(dark, null, new Rect(77, 51, 7, 4), 1, 1);
                dc.DrawRoundedRectangle(dark, null, new Rect(94, 51, 7, 4), 1, 1);
                break;
            case "dualsense-controller":
                var shell = new SolidColorBrush(Color.FromRgb(242, 249, 250));
                dc.DrawGeometry(shell, outline, Geometry.Parse("M40,28 C24,33 16,88 30,98 C40,106 54,83 65,75 L115,75 C127,84 141,105 151,97 C166,85 156,33 140,28 C112,21 67,21 40,28 Z"));
                dc.DrawGeometry(dark, null, Geometry.Parse("M64,47 L116,47 L125,71 C114,86 66,86 55,71 Z"));
                dc.DrawRoundedRectangle(body, outline, new Rect(66, 29, 48, 26), 6, 6);
                dc.DrawLine(glow, new Point(65, 32), new Point(63, 50));
                dc.DrawLine(glow, new Point(115, 32), new Point(117, 50));
                DrawDPad(dc, dark, new Point(45, 50), 22);
                DrawStick(dc, new SolidColorBrush(Color.FromRgb(28, 64, 65)), outline, new Point(72, 71), 9);
                DrawStick(dc, new SolidColorBrush(Color.FromRgb(28, 64, 65)), outline, new Point(108, 71), 9);
                var symbols = new Pen(dark, 1.2);
                dc.DrawGeometry(null, symbols, Geometry.Parse("M133,37 L129,44 L137,44 Z"));
                dc.DrawEllipse(null, symbols, new Point(145, 52), 4, 4);
                dc.DrawRectangle(null, symbols, new Rect(117, 48, 8, 8));
                dc.DrawLine(symbols, new Point(130, 61), new Point(136, 67));
                dc.DrawLine(symbols, new Point(136, 61), new Point(130, 67));
                dc.DrawRoundedRectangle(body, null, new Rect(87, 65, 6, 5), 2, 2);
                break;
            case "controller":
                dc.DrawGeometry(body, outline, Geometry.Parse("M42,32 C21,35 18,94 36,96 C49,97 58,76 66,76 L115,76 C127,77 136,99 148,96 C167,90 157,38 141,32 C115,23 68,23 42,32 Z"));
                dc.DrawRoundedRectangle(dark, null, new Rect(46, 43, 8, 27), 2, 2); dc.DrawRoundedRectangle(dark, null, new Rect(36, 53, 28, 8), 2, 2);
                foreach (var pt in new[] { new Point(133, 44), new Point(145, 56), new Point(121, 56), new Point(133, 68) }) dc.DrawEllipse(body, glow, pt, 3, 3);
                break;
            default:
                dc.PushTransform(new RotateTransform(-18, 90, 55));
                dc.DrawRoundedRectangle(body, outline, new Rect(70, 13, 40, 76), 8, 8);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(212, 237, 229)), outline, new Rect(74, 3, 32, 25), 3, 3);
                dc.DrawRectangle(dark, null, new Rect(80, 9, 7, 10)); dc.DrawRectangle(dark, null, new Rect(94, 9, 7, 10));
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(18, 194, 168)), null, new Point(90, 72), 3, 3); dc.Pop(); break;
        }
        dc.Pop(); dc.Pop();
    }

    private static void DrawStick(DrawingContext dc, Brush ink, Pen outline, Point center, double radius)
    {
        dc.DrawEllipse(ink, outline, center, radius, radius);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(110, 210, 239, 231)), 1), center, radius - 3, radius - 3);
    }

    private static void DrawDPad(DrawingContext dc, Brush ink, Point center, double size)
    {
        dc.DrawRoundedRectangle(ink, null, new Rect(center.X - 3, center.Y - size / 2, 6, size), 1.5, 1.5);
        dc.DrawRoundedRectangle(ink, null, new Rect(center.X - size / 2, center.Y - 3, size, 6), 1.5, 1.5);
    }
}
