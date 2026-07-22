using System.IO;

namespace GongSiOrganizer.RadialPrototype;

internal sealed record PrototypeOptions(
    string RootPath,
    bool RootWasSpecified,
    bool Demo,
    bool DemoCorner,
    bool DemoInvalid,
    bool DemoToast,
    bool DemoFolderDialog,
    bool DemoOnboarding,
    int DemoOnboardingPage,
    bool DemoTrayMenu,
    bool DemoScroll,
    bool DemoScrollTwice,
    int DemoHoverRing,
    bool DisableListener,
    string? SelectionProbePath,
    string? CapturePath,
    int CaptureDelayMilliseconds)
{
    public bool IsDemoLaunch =>
        Demo ||
        DemoCorner ||
        DemoInvalid ||
        DemoToast ||
        DemoFolderDialog ||
        DemoOnboarding ||
        DemoTrayMenu ||
        DemoScroll ||
        DemoScrollTwice ||
        DemoHoverRing >= 0 ||
        !string.IsNullOrWhiteSpace(CapturePath);

    public static PrototypeOptions Parse(IReadOnlyList<string> args)
    {
        var root = @"E:\GongSi";
        var rootWasSpecified = false;
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals("--root", StringComparison.OrdinalIgnoreCase))
            {
                root = args[index + 1];
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

        return new PrototypeOptions(
            Path.GetFullPath(root),
            rootWasSpecified,
            args.Any(value => value.Equals("--demo", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-corner", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-invalid", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-toast", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-folder-dialog", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-onboarding", StringComparison.OrdinalIgnoreCase)),
            demoOnboardingPage,
            args.Any(value => value.Equals("--demo-tray-menu", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-scroll", StringComparison.OrdinalIgnoreCase)),
            args.Any(value => value.Equals("--demo-scroll-twice", StringComparison.OrdinalIgnoreCase)),
            demoHoverRing,
            args.Any(value => value.Equals("--no-listener", StringComparison.OrdinalIgnoreCase)),
            selectionProbePath,
            capturePath,
            captureDelayMilliseconds);
    }
}
