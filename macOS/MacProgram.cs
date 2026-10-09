using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace KitsuDesktop.Mac;

internal static class MacProgram
{
    internal static bool Smoke;
    internal static bool SelfTest;
    internal static bool Failed;
    internal static string DataDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Kitsu")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kitsu", "MacPreview");

    [STAThread]
    public static int Main(string[] args)
    {
        Smoke = args.Contains("--smoke-test");
        SelfTest = args.Contains("--self-test");
        FileStream instance = null;
        try
        {
            if (SelfTest) { Console.WriteLine(MacArt.ValidateArtwork()); return 0; }
            int preview = Array.IndexOf(args, "--render-preview");
            if (preview >= 0 && preview + 1 < args.Length) { MacArt.SavePreview(args[preview + 1]); Console.WriteLine("PASS: saved animation preview."); return 0; }
            if (!Smoke && !SelfTest)
            {
                Directory.CreateDirectory(DataDirectory);
                try { instance = new FileStream(Path.Combine(DataDirectory, "running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { Console.WriteLine("Кицу уже запущена. Откройте её меню в строке меню macOS."); return 0; }
            }
            int result = AppBuilder.Configure<KitsuApplication>().UsePlatformDetect().LogToTrace()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            return Failed ? 1 : result;
        }
        catch (Exception ex) { ReportError(ex); return 1; }
        finally { instance?.Dispose(); }
    }

    internal static void ReportError(Exception ex)
    {
        Failed = true;
        Console.Error.WriteLine(ex);
        try { Directory.CreateDirectory(DataDirectory); File.WriteAllText(Path.Combine(DataDirectory, "error.log"), DateTime.Now.ToString("O") + Environment.NewLine + ex); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class KitsuApplication : Application
{
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                MacProgram.ReportError(e.Exception); e.Handled = true; desktop.Shutdown(1);
            };
            var pet = new MacPet(MacProgram.Smoke || MacProgram.SelfTest);
            desktop.MainWindow = pet;
            pet.Closed += (_, _) => desktop.Shutdown();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

// Quartz reports desktop points, matching Avalonia's macOS screen rectangles and
// NSWindow positions. Reading pointer position does not require Accessibility.
internal static class NativeDesktop
{
    [StructLayout(LayoutKind.Sequential)] private struct QuartzPoint { public double X, Y; }
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern QuartzPoint CGEventGetLocation(IntPtr ev);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr selector, byte value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendLong(IntPtr receiver, IntPtr selector, long value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern long GetLong(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void OrderRelative(IntPtr receiver, IntPtr selector, long ordering, long windowNumber);
    private static PixelPoint latest;

    internal static PixelPoint Pointer
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return latest;
            IntPtr ev = CGEventCreate(IntPtr.Zero);
            if (ev == IntPtr.Zero) return latest;
            try { QuartzPoint p = CGEventGetLocation(ev); latest = new PixelPoint((int)p.X, (int)p.Y); return latest; }
            finally { CFRelease(ev); }
        }
    }
    internal static void Observe(PixelPoint p) => latest = p;

    internal static IntPtr WindowHandle(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return IntPtr.Zero;
        var platform = window.TryGetPlatformHandle();
        if (platform == null) return IntPtr.Zero;
        // The generic macOS handle may be an NSView. Use the explicit NSWindow
        // interface, so Objective-C window messages always reach a window.
        var macInterface = platform.GetType().GetInterfaces().FirstOrDefault(t => t.Name == "IMacOSTopLevelPlatformHandle");
        var property = macInterface?.GetProperty("NSWindow");
        if (property?.GetValue(platform) is IntPtr handle) return handle;
        return platform.HandleDescriptor == "NSWindow" ? platform.Handle : IntPtr.Zero;
    }
    internal static void IgnoreMouse(Window window, bool ignore)
    {
        IntPtr handle = WindowHandle(window);
        if (handle != IntPtr.Zero) SendBool(handle, sel_registerName("setIgnoresMouseEvents:"), ignore ? (byte)1 : (byte)0);
    }
    internal static void Front(Window window)
    {
        IntPtr handle = WindowHandle(window);
        if (handle != IntPtr.Zero) SendVoid(handle, sel_registerName("orderFrontRegardless"));
    }
    internal static void KeepAbove(Window window, params Window[] furniture)
    {
        IntPtr handle = WindowHandle(window);
        if (handle == IntPtr.Zero) return;
        IntPtr indexSelector = sel_registerName("orderedIndex");
        foreach (Window item in furniture)
        {
            IntPtr other = WindowHandle(item);
            if (other == IntPtr.Zero || !item.IsVisible) continue;
            if (GetLong(handle, indexSelector) > GetLong(other, indexSelector))
                OrderRelative(handle, sel_registerName("orderWindow:relativeTo:"), 1, GetLong(other, sel_registerName("windowNumber")));
        }
    }
    internal static void VerifyOverlay(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        IntPtr handle = WindowHandle(window);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("No NSWindow handle for " + window.Title);
        if (window.ActualTransparencyLevel != WindowTransparencyLevel.Transparent) throw new InvalidOperationException("Transparent overlay was not applied to " + window.Title);
        IntPtr selector = sel_registerName("ignoresMouseEvents");
        IgnoreMouse(window, true);
        if ((GetLong(handle, selector) & 255) != 1) throw new InvalidOperationException("Click-through could not be enabled.");
        IgnoreMouse(window, false);
        if ((GetLong(handle, selector) & 255) != 0) throw new InvalidOperationException("Click-through could not be disabled.");
    }
    internal static void ConfigureOverlay(Window window)
    {
        IntPtr handle = WindowHandle(window);
        if (handle == IntPtr.Zero) return;
        SendBool(handle, sel_registerName("setHasShadow:"), 0);
        // Join all Spaces; do not acquire keyboard focus or steal a full-screen app.
        SendLong(handle, sel_registerName("setCollectionBehavior:"), 1 | 16 | 256);
    }
}
