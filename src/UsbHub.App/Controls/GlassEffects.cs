using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UsbHub.App.Controls;

internal static class GlassEffects
{
    internal static bool MotionEnabled { get; set; } = true;
    internal static bool CanAnimate => MotionEnabled && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
}

public sealed class GlassAtmosphere : FrameworkElement
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DateTime _started = DateTime.UtcNow;

    public GlassAtmosphere()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => UpdateMotion();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    private void UpdateMotion() { if (IsVisible && GlassEffects.CanAnimate) _timer.Start(); else _timer.Stop(); }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(244, 250, 249)), null, new Rect(RenderSize));
        var t = GlassEffects.CanAnimate ? (DateTime.UtcNow - _started).TotalSeconds / 20 : 0;
        DrawGlow(dc, Color.FromArgb(160, 138, 238, 216), 0.06 + Math.Sin(t) * 0.06, 0.13, 0.64, 0.80);
        DrawGlow(dc, Color.FromArgb(145, 170, 212, 255), 0.95, 0.15 + Math.Sin(t + 1) * 0.08, 0.65, 0.85);
        DrawGlow(dc, Color.FromArgb(100, 208, 190, 240), 0.54 + Math.Sin(t + 2) * 0.08, 1.03, 0.64, 0.50);
    }

    private void DrawGlow(DrawingContext dc, Color color, double x, double y, double rx, double ry)
    {
        var brush = new RadialGradientBrush(color, Colors.Transparent);
        dc.DrawEllipse(brush, null, new Point(ActualWidth * x, ActualHeight * y), ActualWidth * rx, ActualHeight * ry);
    }
}

public sealed class GlassCard : Border
{
    private Point? _pointer;
    public GlassCard()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        MouseMove += (_, e) => SetHighlight(e.GetPosition(this));
        MouseLeave += (_, _) => { _pointer = null; RenderTransform = Transform.Identity; InvalidateVisual(); };
    }

    internal void SetHighlight(Point point)
    {
        if (!GlassEffects.CanAnimate) return;
        _pointer = point;
        var x = point.X / Math.Max(1, ActualWidth) - 0.5;
        var y = point.Y / Math.Max(1, ActualHeight) - 0.5;
        RenderTransform = new TransformGroup { Children = { new SkewTransform(-y * 1.4, x * 0.8), new ScaleTransform(1.006, 1.006), new TranslateTransform(0, -2) } };
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Sample only the background sibling, never the window containing this card.
        // Nested background sampling would otherwise form a recursive VisualBrush.
        if (Window.GetWindow(this)?.FindName("Atmosphere") is GlassAtmosphere { Opacity: > 0 } backdrop && backdrop.ActualWidth > 0)
        {
            var origin = TranslatePoint(new Point(), backdrop);
            var area = new Rect(RenderSize);
            dc.PushClip(new RectangleGeometry(area, CornerRadius.TopLeft, CornerRadius.TopLeft));
            var sampled = new VisualBrush(backdrop) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(origin, RenderSize), Stretch = Stretch.Fill };
            dc.DrawRectangle(sampled, null, area);
            // Thin concentric sampling bands approximate a convex glass perimeter.
            for (var band = 0; band < 4; band++)
            {
                var outer = new Rect(band * 2, band * 2, Math.Max(0, ActualWidth - band * 4), Math.Max(0, ActualHeight - band * 4));
                var inner = outer; inner.Inflate(-2, -2);
                if (inner.IsEmpty || inner.Width <= 0 || inner.Height <= 0) break;
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(outer, 22 - band * 2, 22 - band * 2), new RectangleGeometry(inner, 20 - band * 2, 20 - band * 2));
                var inset = (4 - band) * 1.7;
                var view = new Rect(origin.X + inset, origin.Y + inset, Math.Max(1, ActualWidth - inset * 2), Math.Max(1, ActualHeight - inset * 2));
                dc.PushClip(ring);
                dc.DrawRectangle(new VisualBrush(backdrop) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = view, Stretch = Stretch.Fill }, null, area);
                dc.Pop();
            }
            dc.Pop();
        }
        base.OnRender(dc);
        var clip = new RectangleGeometry(new Rect(RenderSize), CornerRadius.TopLeft, CornerRadius.TopLeft);
        dc.PushClip(clip);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1), new Point(22, 1), new Point(Math.Max(22, ActualWidth - 22), 1));
        var rim = new Rect(2, 2, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4));
        dc.DrawRoundedRectangle(null, new Pen(new LinearGradientBrush(Color.FromArgb(190, 255, 255, 255), Color.FromArgb(32, 71, 142, 121), 70), 1), rim, 20, 20);
        if (_pointer is { } point)
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(180, 255, 255, 255), Colors.Transparent), null, point, 180, 180);
        dc.Pop();
    }
}

public sealed class BatteryBar : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(double), typeof(BatteryBar), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(BatteryBar), new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty GrowthProperty = DependencyProperty.Register("Growth", typeof(double), typeof(BatteryBar), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public BatteryBar() => Loaded += (_, _) => Grow();
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) { if (((BatteryBar)sender).IsLoaded) ((BatteryBar)sender).Grow(); }
    private void Grow()
    {
        if (GlassEffects.CanAnimate) BeginAnimation(GrowthProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(850)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    protected override void OnRender(DrawingContext dc)
    {
        var height = Math.Min(5, ActualHeight);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(130, 202, 226, 221)), null, new Rect(0, 0, ActualWidth, height), 3, 3);
        dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, ActualWidth * Math.Clamp(Percent, 0, 100) / 100 * (double)GetValue(GrowthProperty), height), 3, 3);
    }
}
