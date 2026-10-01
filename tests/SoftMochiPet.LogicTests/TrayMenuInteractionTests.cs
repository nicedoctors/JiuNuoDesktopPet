using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SoftMochiPet.Services;
using Forms = System.Windows.Forms;

internal static class TrayMenuInteractionTests
{
    private static readonly Type MenuType = typeof(TrayIconService).GetNestedType("MenuInteractionStrip", BindingFlags.NonPublic)!;

    public static void OpeningNotifiesBeforeOtherHandlers() => OnSta(() =>
    {
        using var menu = CreateMenu();
        var order = new List<string>();
        Observe(menu, open =>
        {
            Assert(IsOpen(menu) == open, "The owner must observe the new state inside its synchronous notification.");
            order.Add(open ? "pause" : "resume");
        });
        menu.Opening += (_, _) =>
        {
            Assert(IsOpen(menu), "Rendering must already be paused before ordinary Opening handlers run.");
            order.Add("opening");
        };
        menu.Opened += (_, _) => order.Add("opened");
        Lifecycle(menu, "OnOpening", new CancelEventArgs());
        Lifecycle(menu, "OnOpened", EventArgs.Empty);
        Assert(order.SequenceEqual(new[] { "pause", "opening", "opened" }),
            "Menu state must be delivered synchronously before opening work, without a duplicate Opened notification.");
        Assert(!menu.Visible && !menu.IsHandleCreated, "Lifecycle tests must not show a menu or create a native menu handle.");
    });

    public static void RepeatedOpenCloseIsIdempotent() => OnSta(() =>
    {
        using var menu = CreateMenu();
        var events = new List<bool>();
        Observe(menu, events.Add);
        Lifecycle(menu, "OnClosed", Closed());
        Lifecycle(menu, "OnOpening", new CancelEventArgs());
        Lifecycle(menu, "OnOpening", new CancelEventArgs());
        Lifecycle(menu, "OnOpened", EventArgs.Empty);
        Lifecycle(menu, "OnOpened", EventArgs.Empty);
        Assert(IsOpen(menu) && events.SequenceEqual(new[] { true }),
            "Repeated show requests and Opened confirmations must leave a single active interaction.");
        Lifecycle(menu, "OnClosed", Closed());
        Lifecycle(menu, "OnClosed", Closed());
        Assert(!IsOpen(menu) && events.SequenceEqual(new[] { true, false }),
            "Duplicate Closed events must not restart the render clock repeatedly.");
        Lifecycle(menu, "OnOpening", new CancelEventArgs());
        Lifecycle(menu, "OnOpened", EventArgs.Empty);
        Lifecycle(menu, "OnClosed", Closed());
        Assert(events.SequenceEqual(new[] { true, false, true, false }), "A later independent menu interaction must work normally.");
    });

    public static void CancelledOrFailedOpeningResetsState() => OnSta(() =>
    {
        using var menu = CreateMenu();
        var events = new List<bool>();
        Observe(menu, events.Add);
        CancelEventHandler cancel = (_, e) => e.Cancel = true;
        menu.Opening += cancel;
        var cancelled = new CancelEventArgs();
        Lifecycle(menu, "OnOpening", cancelled);
        Assert(cancelled.Cancel && !IsOpen(menu) && events.SequenceEqual(new[] { true, false }),
            "Cancellation must resume the owner before OnOpening returns even though Closed is never raised.");
        menu.Opening -= cancel;
        CancelEventHandler fail = (_, _) => throw new InvalidOperationException("Simulated opening failure.");
        menu.Opening += fail;
        var threw = false;
        try { Lifecycle(menu, "OnOpening", new CancelEventArgs()); }
        catch (InvalidOperationException) { threw = true; }
        Assert(threw && !IsOpen(menu) && events.SequenceEqual(new[] { true, false, true, false }),
            "An opening exception must not leave the pet permanently paused.");
        menu.Opening -= fail;
        Lifecycle(menu, "OnOpening", new CancelEventArgs());
        Assert(IsOpen(menu), "A failed opening must not disable later successful menu interactions.");
    });

    public static void DisposeClosesOnceAndSubmenusStayIndependent() => OnSta(() =>
    {
        using var menu = CreateMenu();
        var events = new List<bool>();
        Observe(menu, events.Add);
        var characters = new Forms.ToolStripMenuItem("角色");
        characters.DropDownItems.Add("测试角色");
        menu.Items.Add(characters);
        Lifecycle(menu, "OnOpening", new CancelEventArgs());
        Lifecycle(menu, "OnOpened", EventArgs.Empty);
        typeof(Forms.ToolStripDropDown).GetMethod("OnClosed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(characters.DropDown, [Closed()]);
        Assert(IsOpen(menu) && events.SequenceEqual(new[] { true }),
            "Closing or switching a child dropdown must not close the root-menu interaction.");
        menu.Dispose();
        menu.Dispose();
        Assert(!IsOpen(menu) && events.SequenceEqual(new[] { true, false }),
            "Disposing an open menu must emit exactly one final reset.");
        var openingAfterDispose = new CancelEventArgs();
        Lifecycle(menu, "OnOpening", openingAfterDispose);
        Lifecycle(menu, "OnOpened", EventArgs.Empty);
        Assert(openingAfterDispose.Cancel && !IsOpen(menu) && events.SequenceEqual(new[] { true, false }),
            "Disposed menu callbacks must not reactivate the owner pause.");
    });

    private static Forms.ContextMenuStrip CreateMenu()
    {
        var menu = (Forms.ContextMenuStrip)Activator.CreateInstance(MenuType, nonPublic: true)!;
        menu.Items.Add("测试项");
        return menu;
    }
    private static bool IsOpen(Forms.ContextMenuStrip menu) => (bool)MenuType.GetProperty("IsInteractionOpen")!.GetValue(menu)!;
    private static void Observe(Forms.ContextMenuStrip menu, Action<bool> callback) =>
        MenuType.GetEvent("InteractionOpenChanged")!.AddEventHandler(menu, callback);
    private static Forms.ToolStripDropDownClosedEventArgs Closed() => new(Forms.ToolStripDropDownCloseReason.CloseCalled);
    private static void Lifecycle(Forms.ContextMenuStrip menu, string method, object args)
    {
        try { MenuType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, [args]); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
    }
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
