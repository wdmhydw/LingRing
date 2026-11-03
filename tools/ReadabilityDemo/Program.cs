using System.IO;
using System.Windows;
using GongSiOrganizer.RadialPrototype.Presentation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new ReadabilityLabWindow();
        var result = 0;
        if (args.Length is 2 or 4 && args[0] == "--capture")
            window.Loaded += async (_, _) =>
            {
                try
                {
                    await window.CaptureComparisonsAsync(Path.GetFullPath(args[1]),
                        args.Length == 4 && args[2] == "--fixture" ? Path.GetFullPath(args[3]) : null);
                }
                catch (Exception exception)
                {
                    Directory.CreateDirectory(args[1]);
                    File.WriteAllText(Path.Combine(args[1], "error.txt"), exception.ToString()); result = 1;
                }
                finally { window.Close(); }
            };
        app.Run(window);
        return result;
    }
}
