using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GongSiOrganizer.RadialPrototype.Presentation;

internal static class UiMotion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    public const int Hover = 180;
    public const int Open = 360;
    public const int Content = 360;
    public const int Wheel = 240;
    public const int Exit = 130;

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
