using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GongSiOrganizer.RadialPrototype.Presentation;

internal static class UiMotion
{
    internal static bool ReduceForPreview { get; set; }
    public static bool Enabled => !ReduceForPreview && SystemParameters.ClientAreaAnimation;
    public const int Hover = 180;
    public const int Open = 360;
    public const int Content = 360;
    public const int Wheel = 240;
    public const int Exit = 130;
    public const int RingSlotDuration = 500;
    public const int RingSlotStagger = 20;
    public const int RingOpen = RingSlotDuration + 3 * RingSlotStagger;

    public static double RingEase(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        return t * t * (3 - 2 * t);
    }

    // Small overshoot for the painted surface; labels and hit geometry stay put on hover.
    public static double Settle(double progress)
    {
        var t = Math.Clamp(progress, 0, 1) - 1;
        return 1 + 2.1 * t * t * t + 1.1 * t * t;
    }

    public static IEasingFunction EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

    public static void Reveal(FrameworkElement element, double distance = 10, int duration = Content)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!Enabled) { element.RenderTransform = Transform.Identity; return; }
        var translation = new TranslateTransform();
        element.RenderTransform = translation;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration * .75))
            { EasingFunction = EaseOut(), FillBehavior = FillBehavior.Stop });
        translation.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = EaseOut(), FillBehavior = FillBehavior.Stop });
    }
}
