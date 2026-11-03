using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GongSiOrganizer.RadialPrototype.Radial;
using GongSiOrganizer.RadialPrototype.Services;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    private static string WindowClass(IntPtr window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 0;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try { await RunAsync(args); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                File.WriteAllText(args[Array.IndexOf(args, "--output") + 1] + ".error.txt", ex.ToString());
                exitCode = 1;
            }
            finally { app.Shutdown(); }
        });
        app.Run();
        return exitCode;
    }

    private static async Task RunAsync(string[] args)
    {
        string Option(string name) => args[Array.IndexOf(args, name) + 1];
        var output = Path.GetFullPath(Option("--output"));
        var root = Path.GetFullPath(Option("--root"));
        var expected = Path.GetFullPath(Option("--expected"));
        var expectedCount = int.Parse(Option("--count"));
        var point = new NativeScreenPoint(int.Parse(Option("--x")), int.Parse(Option("--y")));
        var explorer = new IntPtr(long.Parse(Option("--window")));
        using var cache = new DirectoryTreeCache(root);
        await cache.ReloadAsync();
        var timer = new Stopwatch();
        var stages = new List<object>();
        var resolver = new FileTargetResolver
        {
            Trace = stage => stages.Add(new { stage, elapsed_ms = timer.Elapsed.TotalMilliseconds })
        };
        var overlay = new RadialOverlayWindow();
        overlay.SetInteractionEnabled(false);
        var samples = new List<object>();
        try
        {
            if (args.Contains("--warm-up"))
            {
                var warmup = resolver.WarmUpAsync();
                overlay.PrepareForFirstShow(cache.CreateMenuSnapshot()!);
                await warmup;
            }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                SetWindowPos(explorer, new IntPtr(-1), 0, 0, 0, 0, 0x53);
                SetForegroundWindow(explorer);
                await Task.Delay(120);
                stages.Clear();
                var hit = NativeDisplay.CaptureHitContext(point);
                if (!ShellMiddleClickScope.IsSupported(hit))
                {
                    throw new InvalidOperationException($"Attempt {attempt + 1}: probe point {point} hit {WindowClass(hit.WindowHandle)} / {WindowClass(hit.RootWindowHandle)}, expected {WindowClass(explorer)}.");
                }
                timer.Restart();
                var resolution = await resolver.ResolveAsync(hit);
                var resolveMs = timer.Elapsed.TotalMilliseconds;
                if (!resolution.IsFileSystemItem || resolution.ItemPaths.Count != expectedCount ||
                    !resolution.ItemPaths.Contains(expected, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Probe resolved a different item or selection: valid={resolution.IsFileSystemItem}, count={resolution.ItemPaths.Count}, expected={expectedCount}, clicked-match={resolution.ItemPaths.Contains(expected, StringComparer.OrdinalIgnoreCase)}, reason={resolution.Reason}.");
                await overlay.ShowMenuAsync(point, resolution.ItemPaths, cache.CreateMenuSnapshot()!, null, default);
                var showMs = timer.Elapsed.TotalMilliseconds;
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                var control = (Visual)VisualTreeHelper.GetChild((Visual)overlay.Content, 0);
                if (VisualTreeHelper.GetDrawing(control) is null)
                    throw new InvalidOperationException("No radial drawing was produced.");
                samples.Add(new { attempt = attempt + 1, resolve_ms = resolveMs,
                    show_ms = showMs, first_draw_ms = timer.Elapsed.TotalMilliseconds,
                    stages = stages.ToArray() });
                await Task.Delay(380);
                if (attempt == 0 && args.Contains("--capture")) overlay.SaveSnapshot(Option("--capture"));
                overlay.HideOverlay();
                await Task.Delay(250);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var json = JsonSerializer.Serialize(new { samples }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(output, json);
            Console.WriteLine(json);
        }
        finally
        {
            overlay.Close();
            SetWindowPos(explorer, new IntPtr(-2), 0, 0, 0, 0, 0x13);
        }
    }
}
