using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet;
using SoftMochiPet.Core;
using DrawingRectangle = System.Drawing.Rectangle;
using Point = System.Windows.Point;

internal static class PrankOverlayLifecycleTests
{
    public static void HatCannotReopenOrCloseAgainWhileClosing() => OnSta(() =>
    {
        var type = typeof(PrankEffectWindow).Assembly.GetType("SoftMochiPet.HatPropWindow", throwOnError: true)!;
        var hat = (Window)Activator.CreateInstance(type, CreateBitmap())!;
        var close = type.GetMethod("CloseSafely")!;
        var place = type.GetMethod("Place")!;
        var reentered = false;
        hat.Closing += (_, _) =>
        {
            reentered = true;
            Invoke(place, hat, new Point(30, 30), 40d, 0d, 1d);
            Invoke(close, hat);
        };
        new WindowInteropHelper(hat).EnsureHandle();
        Assert(!hat.IsVisible, "The lifecycle test must never display the hat window.");
        Invoke(close, hat);
        Assert(reentered, "The hidden HWND must exercise the actual WPF Closing event.");
        Invoke(close, hat);
        Invoke(place, hat, new Point(30, 30), 40d, 0d, 1d);
        Assert(!hat.IsVisible, "A closed hat must never be shown again by a late animation tick.");
    });

    public static void SnapshotOverlayCloseIsIdempotent() => OnSta(() =>
    {
        var overlay = new PrankEffectWindow(CreateBitmap(), new DrawingRectangle(0, 0, 40, 40),
            new DrawingRectangle(0, 0, 100, 100), WindowPrankVisualKind.Shatter, IntPtr.Zero);
        var reentered = false;
        overlay.Closing += (_, _) => { reentered = true; overlay.CloseSafely(); };
        new WindowInteropHelper(overlay).EnsureHandle();
        Assert(!overlay.IsVisible, "The lifecycle test must never display the snapshot overlay.");
        overlay.CloseSafely();
        overlay.CloseSafely();
        Assert(reentered && !overlay.IsVisible, "Snapshot cleanup must tolerate a reentrant or repeated close.");
    });

    private static BitmapSource CreateBitmap()
    {
        var pixels = new byte[4 * 4 * 4];
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Invoke(MethodInfo method, object target, params object[] arguments)
    {
        try { method.Invoke(target, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
