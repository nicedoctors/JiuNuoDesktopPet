using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace SoftMochiPet;

/// <summary>A user-requested, disposable window used only by the isolated feature-test process.</summary>
public sealed class FeatureTestTargetWindow : Window
{
    public const string WindowTitle = "菲比啾比专用测试窗口";
    private readonly Process _parent;
    private readonly DispatcherTimer _parentTimer;

    private FeatureTestTargetWindow(Process parent)
    {
        _parent = parent;
        Title = WindowTitle;
        Width = 580;
        Height = 390;
        MinWidth = 260;
        MinHeight = 180;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = false;
        ShowInTaskbar = true;
        ShowActivated = false;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(244, 243, 255));

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = "给菲比啾比的小玩具",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 43, 98)),
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(heading);

        var tiles = new Grid();
        tiles.RowDefinitions.Add(new RowDefinition());
        tiles.RowDefinitions.Add(new RowDefinition());
        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        Color[] colors = [Color.FromRgb(160, 212, 250), Color.FromRgb(202, 182, 245),
            Color.FromRgb(255, 206, 154), Color.FromRgb(172, 226, 205)];
        string[] labels = ["01  跳跳", "02  帽子", "03  纸片", "04  回来啦"];
        for (var index = 0; index < colors.Length; index++)
        {
            var tile = new Border
            {
                Background = new SolidColorBrush(colors[index]),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(4),
                Child = new TextBlock
                {
                    Text = labels[index], FontSize = 25, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(38, 51, 83)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                },
            };
            Grid.SetRow(tile, index / 2);
            Grid.SetColumn(tile, index % 2);
            tiles.Children.Add(tile);
        }
        Grid.SetRow(tiles, 1);
        root.Children.Add(tiles);

        var footer = new TextBlock
        {
            Text = "仅用于窗口互动测试，不含文件内容。\n可以移动、最小化，或直接关闭。",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(74, 74, 100)),
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        _parentTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _parentTimer.Tick += CheckParent;
        Loaded += (_, _) => _parentTimer.Start();
        Closed += (_, _) =>
        {
            _parentTimer.Stop();
            _parentTimer.Tick -= CheckParent;
            _parent.Dispose();
        };
    }

    public static bool TryCreate(string[] args, out FeatureTestTargetWindow? target)
    {
        target = null;
        if (!FeatureTestTargetArguments.TryReadParentPid(args, out var parentPid) || parentPid == Environment.ProcessId)
            return false;
        Process? parent = null;
        try
        {
            parent = Process.GetProcessById(parentPid);
            // Holding the native process handle keeps later checks tied to this
            // parent instance rather than a reused numeric PID.
            parent.EnableRaisingEvents = true;
            if (parent.HasExited) { parent.Dispose(); return false; }
            target = new FeatureTestTargetWindow(parent);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            parent?.Dispose();
            return false;
        }
    }

    private void CheckParent(object? sender, EventArgs args)
    {
        try
        {
            if (!_parent.HasExited) return;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A lost parent ends only this disposable test window.
        }
        Close();
    }
}

public static class FeatureTestTargetArguments
{
    public static bool TryReadParentPid(string[]? args, out int parentPid)
    {
        parentPid = 0;
        return args is { Length: 3 } && args[0] == "--feibi-feature-target" && args[1] == "--parent-pid" &&
            int.TryParse(args[2], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out parentPid) && parentPid > 0;
    }
}
