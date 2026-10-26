using System.IO;
using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype;

internal sealed record PrototypeOptions(
    string RootPath,
    string? StartPath,
    bool RootWasSpecified,
    bool Demo,
    bool DemoCorner,
    bool DemoInvalid,
    bool DemoToast,
    bool DemoFolderDialog,
    bool DemoOnboarding,
    int DemoOnboardingPage,
    bool DemoRootChoice,
    bool DemoTrayMenu,
    bool DemoScroll,
    bool DemoScrollTwice,
    int DemoHoverRing,
    bool DisableListener,
    string? SelectionProbePath,
    string? CapturePath,
    int CaptureDelayMilliseconds)
{
    public string DemoPosition { get; init; } = "center";
    public string? DemoFeedback { get; init; }
    public bool DemoReducedMotion { get; init; }
    public bool IsDemoLaunch =>
        Demo ||
        DemoCorner ||
        DemoInvalid ||
        DemoToast ||
        DemoFolderDialog ||
        DemoOnboarding ||
        DemoRootChoice ||
        DemoTrayMenu ||
        DemoScroll ||
        DemoScrollTwice ||
        DemoHoverRing >= 0 ||
        DemoFeedback is not null ||
        !string.IsNullOrWhiteSpace(CapturePath);

    public static PrototypeOptions Parse(IReadOnlyList<string> args)
    {
        var profile = ArchiveNavigationProfile.WholeComputer;
        var rootWasSpecified = false;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--root", StringComparison.OrdinalIgnoreCase))
            {
                profile = ArchiveScope.CreateNavigationProfile(args[index + 1]);
                rootWasSpecified = true;
            }
        }

        string? capturePath = null;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--capture", StringComparison.OrdinalIgnoreCase))
                capturePath = Path.GetFullPath(args[index + 1]);
        }

        string? selectionProbePath = null;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--selection-probe", StringComparison.OrdinalIgnoreCase))
                selectionProbePath = Path.GetFullPath(args[index + 1]);
        }

        var captureDelayMilliseconds = 220;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--capture-delay", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var parsedDelay))
                captureDelayMilliseconds = Math.Clamp(parsedDelay, 0, 5000);
        }

        var demoHoverRing = -1;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--demo-hover-ring", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var parsedRing))
                demoHoverRing = Math.Clamp(parsedRing, -1, 2);
        }

        var demoOnboardingPage = 0;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--demo-onboarding-page", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var parsedPage))
                demoOnboardingPage = Math.Clamp(parsedPage, 0, 3);
        }

        var demoPosition = "center";
        string? demoFeedback = null;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--demo-position", StringComparison.OrdinalIgnoreCase))
                demoPosition = args[index + 1].ToLowerInvariant();
            if (args[index].Equals("--demo-feedback", StringComparison.OrdinalIgnoreCase) &&
                args[index + 1].ToLowerInvariant() is "success" or "partial" or "failure" or "busy")
                demoFeedback = args[index + 1].ToLowerInvariant();
        }

        return new PrototypeOptions(
            profile.RootPath,
            profile.StartPath,
            rootWasSpecified,
            args.Any(value => value.Equals("--demo", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-corner", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-invalid", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-toast", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-folder-dialog", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-onboarding", StringComparison.OrdinalIgnoreCase)),
            demoOnboardingPage,
            args.Any(value => value.Equals("--demo-root-choice", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-tray-menu", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-scroll", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-scroll-twice", StringComparison.OrdinalIgnoreCase)),
            demoHoverRing,
            args.Any(value => value.Equals("--no-listener", StringComparison.OrdinalIgnoreCase)),
            selectionProbePath,
            capturePath,
            captureDelayMilliseconds)
        {
            DemoPosition = demoPosition,
            DemoFeedback = demoFeedback,
            DemoReducedMotion = args.Any(value => value.Equals("--demo-reduced-motion", StringComparison.OrdinalIgnoreCase))
        };
    }
}
