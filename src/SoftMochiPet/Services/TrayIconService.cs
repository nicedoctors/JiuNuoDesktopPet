using System.Drawing;
using System.IO;
using System.ComponentModel;
using SoftMochiPet.Core;
using Forms = System.Windows.Forms;

namespace SoftMochiPet.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly MenuInteractionStrip _menu;
    private readonly Forms.ToolStripMenuItem _mute;
    private readonly Forms.ToolStripMenuItem _startWithWindows;
    private readonly Forms.ToolStripMenuItem _fastingMode;
    private readonly Forms.ToolStripMenuItem _infiniteMode;
    private readonly Forms.ToolStripMenuItem _coexistenceMode;
    private readonly Forms.ToolStripMenuItem _speechBubbles;
    private readonly Forms.ToolStripMenuItem? _pairMenu;
    private readonly Forms.ToolStripMenuItem? _pairDialogueItem;
    private readonly Forms.ToolStripMenuItem _quietMode;
    private readonly Forms.ToolStripMenuItem _allowMischief;
    private readonly Forms.ToolStripMenuItem _mischiefStatus;
    private readonly Forms.ToolStripMenuItem _prankOnce;
    private readonly Forms.ToolStripMenuItem _tease;
    private readonly Forms.ToolStripMenuItem _returnWindows;
    private readonly Forms.TrackBar _sizeSlider;
    private readonly Forms.Label _sizeLabel;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _characterItems = new(StringComparer.Ordinal);
    private readonly BehaviorControlMenu? _behaviorControlMenu;

    private PetCharacterProfile _character;
    private bool _coexisting;
    private Icon? _customIcon;

    private bool _suppressSettingEvents;
    private bool _disposed;

    public TrayIconService(
        bool soundEnabled,
        double petSize,
        bool startWithWindows,
        bool fastingMode,
        bool quietMode,
        PetCharacterProfile? character = null,
        bool allowMischief = true,
        bool behaviorControl = false,
        bool infiniteMode = false,
        bool coexistenceMode = false,
        bool showSpeechBubbles = false)
    {
        _character = character ?? PetCharacterProfile.Get("nuonuo");
        _customIcon = LoadCharacterIcon(_character);

        _mute = new Forms.ToolStripMenuItem("禁音")
        {
            Checked = !soundEnabled,
            CheckOnClick = true,
        };
        _mute.CheckedChanged += (_, _) => SoundEnabledChanged?.Invoke(!_mute.Checked);

        _startWithWindows = CreateToggle("开机自启", startWithWindows);
        _fastingMode = CreateToggle("禁食模式（永不饥饿）", fastingMode);
        _infiniteMode = CreateToggle("无限模式（隐藏双方进度条）", infiniteMode);
        _coexistenceMode = CreateToggle("双宠共存", coexistenceMode);
        _speechBubbles = CreateToggle("显示头顶对话气泡", showSpeechBubbles);
        _coexisting = coexistenceMode;
        _quietMode = CreateToggle("安静模式（只站立或睡觉）", quietMode);
        _allowMischief = CreateToggle("允许自主捣蛋", allowMischief);
        _mischiefStatus = new Forms.ToolStripMenuItem { Enabled = false };
        SetMischiefStatus(allowMischief ? "蓄力中" : "已关闭");
        _prankOnce = new Forms.ToolStripMenuItem("捣蛋一下", null, (_, _) => MischiefRequested?.Invoke());
        _tease = new Forms.ToolStripMenuItem("逗逗她", null, (_, _) => TeaseRequested?.Invoke());
        _returnWindows = new Forms.ToolStripMenuItem("把窗口还回来", null, (_, _) => ReturnWindowsRequested?.Invoke())
        {
            Enabled = false,
        };
        _allowMischief.CheckedChanged += (_, _) =>
        {
            if (!_suppressSettingEvents)
            {
                AllowMischiefChanged?.Invoke(_allowMischief.Checked);
            }
        };
        _startWithWindows.CheckedChanged += (_, _) =>
        {
            if (!_suppressSettingEvents)
            {
                StartWithWindowsChanged?.Invoke(_startWithWindows.Checked);
            }
        };
        _fastingMode.CheckedChanged += (_, _) =>
        {
            if (!_suppressSettingEvents)
            {
                FastingModeChanged?.Invoke(_fastingMode.Checked);
            }
        };
        _quietMode.CheckedChanged += (_, _) =>
        {
            UpdateCharacterMenu();
            if (!_suppressSettingEvents)
            {
                QuietModeChanged?.Invoke(_quietMode.Checked);
            }
        };

        _sizeLabel = new Forms.Label
        {
            AutoSize = false,
            Location = new Point(8, 5),
            Size = new Size(226, 21),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _sizeSlider = new Forms.TrackBar
        {
            AutoSize = false,
            Location = new Point(4, 27),
            Size = new Size(234, 34),
            Minimum = PetSizePolicy.MinimumPercent,
            Maximum = PetSizePolicy.MaximumPercent,
            TickFrequency = 20,
            SmallChange = 2,
            LargeChange = 10,
            Value = PetSizePolicy.ToPercent(petSize),
        };
        UpdateSizeLabel();
        _sizeSlider.ValueChanged += (_, _) =>
        {
            UpdateSizeLabel();
            PetSizeChanged?.Invoke(PetSizePolicy.FromPercent(_sizeSlider.Value));
        };
        var sizePanel = new Forms.Panel
        {
            AutoSize = false,
            Size = new Size(242, 64),
        };
        sizePanel.Controls.Add(_sizeLabel);
        sizePanel.Controls.Add(_sizeSlider);
        var sizeHost = new Forms.ToolStripControlHost(sizePanel)
        {
            AutoSize = false,
            Size = sizePanel.Size,
            Margin = new Forms.Padding(2),
        };
        _infiniteMode.CheckedChanged += (_, _) =>
        {
            if (!_suppressSettingEvents) InfiniteModeChanged?.Invoke(_infiniteMode.Checked);
        };
        _coexistenceMode.CheckedChanged += (_, _) =>
        {
            if (!_suppressSettingEvents) CoexistenceModeChanged?.Invoke(_coexistenceMode.Checked);
        };
        _speechBubbles.CheckedChanged += (_, _) =>
        {
            UpdateCharacterMenu();
            if (!_suppressSettingEvents) SpeechBubblesChanged?.Invoke(_speechBubbles.Checked);
        };

        var characterMenu = new Forms.ToolStripMenuItem("桌宠角色");
        foreach (var profile in PetCharacterProfile.All)
        {
            var item = new Forms.ToolStripMenuItem(profile.DisplayName)
            {
                Checked = profile.Id == _character.Id,
            };
            item.Click += (_, _) => CharacterChanged?.Invoke(profile.Id);
            _characterItems.Add(profile.Id, item);
            characterMenu.DropDownItems.Add(item);
        }

        _menu = new MenuInteractionStrip();
        _menu.InteractionOpenChanged += open => MenuOpenChanged?.Invoke(open);
        if (behaviorControl)
        {
            _behaviorControlMenu = new BehaviorControlMenu(_character,
                action => BehaviorControlRequested?.Invoke(action),
                () => BehaviorControlStopRequested?.Invoke(),
                enabled => BehaviorAutonomyChanged?.Invoke(enabled));
            _menu.Items.Add(_behaviorControlMenu.Status);
            _menu.Items.Add(_behaviorControlMenu.Actions);
            _menu.Items.Add(_behaviorControlMenu.Stop);
            _menu.Items.Add(_behaviorControlMenu.Autonomy);
            _menu.Items.Add(new Forms.ToolStripSeparator());
            _startWithWindows.Available = false;
        }
        _menu.Items.Add("让她回家", null, (_, _) => BringHomeRequested?.Invoke());
        _menu.Items.Add(characterMenu);
        if (!behaviorControl) _menu.Items.Add(_coexistenceMode);
        if (!behaviorControl)
        {
            _pairMenu = new Forms.ToolStripMenuItem("双人互动演示");
            _pairMenu.DropDownItems.Add("菲比揪糯糯的脸", null, (_, _) => PairInteractionRequested?.Invoke("cheek"));
            _pairMenu.DropDownItems.Add("靠在一起睡觉", null, (_, _) => PairInteractionRequested?.Invoke("sleep"));
            _pairMenu.DropDownItems.Add("从帽子里掏零食投喂", null, (_, _) => PairInteractionRequested?.Invoke("feed"));
            _pairMenu.DropDownItems.Add("小跟屁虫", null, (_, _) => PairInteractionRequested?.Invoke("follow"));
            _pairMenu.DropDownItems.Add("路过蹭一下", null, (_, _) => PairInteractionRequested?.Invoke("nuzzle"));
            _pairMenu.DropDownItems.Add("一起玩小球", null, (_, _) => PairInteractionRequested?.Invoke("ball"));
            _pairMenu.DropDownItems.Add("一起看热闹", null, (_, _) => PairInteractionRequested?.Invoke("watch"));
            _pairDialogueItem = new Forms.ToolStripMenuItem("让她们聊两句", null,
                (_, _) => PairDialogueRequested?.Invoke());
            _pairMenu.DropDownItems.Add(_pairDialogueItem);
            _menu.Items.Add(_pairMenu);
        }
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(sizeHost);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_startWithWindows);
        _menu.Items.Add(_fastingMode);
        if (!behaviorControl) _menu.Items.Add(_infiniteMode);
        _menu.Items.Add(_allowMischief);
        _menu.Items.Add(_mischiefStatus);
        _menu.Items.Add(_prankOnce);
        _menu.Items.Add(_tease);
        _menu.Items.Add(_returnWindows);
        _menu.Items.Add(_quietMode);
        if (!behaviorControl) _menu.Items.Add(_speechBubbles);
        _menu.Items.Add(_mute);
        _menu.Items.Add("退出桌宠", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _customIcon ?? SystemIcons.Application,
            Text = TrayTitle,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => BringHomeRequested?.Invoke();
        UpdateCharacterMenu();
    }

    private static Icon? LoadCharacterIcon(PetCharacterProfile character)
    {
        if (!string.IsNullOrWhiteSpace(character.IconRelativePath))
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, character.IconRelativePath);
                if (File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }
            }
            catch (Exception)
            {
                // A missing or unreadable character icon must not prevent selecting the character.
            }
        }

        // The executable's embedded icon belongs to Nuonuo, not to other characters.
        return character.Id == "nuonuo" ? LoadApplicationIcon() : null;
    }

    private static Icon? LoadApplicationIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            {
                var embeddedIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (embeddedIcon is not null)
                {
                    return embeddedIcon;
                }
            }
        }
        catch (Exception)
        {
            // Fall back to the loose icon for development layouts that do not use an app host.
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "pet.ico");
        try
        {
            return File.Exists(iconPath) ? new Icon(iconPath) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public event Action? BringHomeRequested;
    public event Action? ExitRequested;
    public event Action<bool>? SoundEnabledChanged;
    public event Action<double>? PetSizeChanged;
    public event Action<bool>? StartWithWindowsChanged;
    public event Action<bool>? FastingModeChanged;
    public event Action<bool>? InfiniteModeChanged;
    public event Action<bool>? CoexistenceModeChanged;
    public event Action<bool>? SpeechBubblesChanged;
    public event Action<string>? PairInteractionRequested;
    public event Action? PairDialogueRequested;
    public event Action<bool>? QuietModeChanged;
    public event Action<string>? CharacterChanged;
    public event Action<bool>? AllowMischiefChanged;
    public event Action? MischiefRequested;
    public event Action? TeaseRequested;
    public event Action? ReturnWindowsRequested;
    public event Action? FeatureTestStopRequested;
    public event Action? FeatureTestRetryRequested;
    public event Action<bool>? MenuOpenChanged;
    public event Action<BehaviorControlAction>? BehaviorControlRequested;
    public event Action? BehaviorControlStopRequested;
    public event Action<bool>? BehaviorAutonomyChanged;

    public bool IsMenuOpen => _menu.IsInteractionOpen;

    private Forms.ToolStripMenuItem? _featureTestStatus;
    private Forms.ToolStripMenuItem? _featureTestStop;
    private Forms.ToolStripMenuItem? _featureTestRetry;

    public void ConfigureFeatureTest()
    {
        foreach (Forms.ToolStripItem item in _menu.Items)
            item.Enabled = ReferenceEquals(item, _mute) || item.Text == "退出桌宠";
        _featureTestStatus = new Forms.ToolStripMenuItem("准备专属功能测试…") { Enabled = false };
        _featureTestStop = new Forms.ToolStripMenuItem("停止专属功能测试", null,
            (_, _) => FeatureTestStopRequested?.Invoke());
        _featureTestRetry = new Forms.ToolStripMenuItem("重试本项", null,
            (_, _) => FeatureTestRetryRequested?.Invoke()) { Enabled = false };
        _menu.Items.Insert(0, _featureTestStatus);
        _menu.Items.Insert(1, _featureTestStop);
        _menu.Items.Insert(2, _featureTestRetry);
        _menu.Items.Insert(3, new Forms.ToolStripSeparator());
    }

    public void SetFeatureTestProgress(string text, bool running, bool canRetry = false)
    {
        if (_featureTestStatus is not null) _featureTestStatus.Text = text;
        if (_featureTestStop is not null) _featureTestStop.Enabled = running;
        if (_featureTestRetry is not null) _featureTestRetry.Enabled = running && canRetry;
    }

    private static Forms.ToolStripMenuItem CreateToggle(string text, bool isChecked) => new(text)
    {
        Checked = isChecked,
        CheckOnClick = true,
    };

    public void SetStartWithWindows(bool enabled)
    {
        _suppressSettingEvents = true;
        try
        {
            _startWithWindows.Checked = enabled;
        }
        finally
        {
            _suppressSettingEvents = false;
        }
    }

    public void SetCharacter(PetCharacterProfile character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var previousIcon = _customIcon;
        var icon = LoadCharacterIcon(character);
        _notifyIcon.Icon = icon ?? SystemIcons.Application;
        _customIcon = icon;
        previousIcon?.Dispose();

        _character = character;
        _notifyIcon.Text = TrayTitle;
        foreach (var (id, item) in _characterItems)
        {
            item.Checked = id == character.Id;
        }

        UpdateSizeLabel();
        UpdateCharacterMenu();
    }

    public void SetCoexistence(bool enabled)
    {
        _suppressSettingEvents = true;
        try { _coexistenceMode.Checked = enabled; }
        finally { _suppressSettingEvents = false; }
        _coexisting = enabled;
        _notifyIcon.Text = TrayTitle;
        UpdateSizeLabel();
        UpdateCharacterMenu();
    }

    public void SetInfiniteMode(bool enabled)
    {
        _suppressSettingEvents = true;
        try { _infiniteMode.Checked = enabled; }
        finally { _suppressSettingEvents = false; }
    }

    public void SetAllowMischief(bool enabled)
    {
        _suppressSettingEvents = true;
        try
        {
            _allowMischief.Checked = enabled;
        }
        finally
        {
            _suppressSettingEvents = false;
        }
    }

    public void SetWindowReturnAvailable(bool available)
    {
        _returnWindows.Enabled = available;
    }

    public void SetMischiefStatus(string status)
    {
        if (_disposed) return;
        var text = $"捣蛋状态：{status}";
        if (!string.Equals(_mischiefStatus.Text, text, StringComparison.Ordinal))
            _mischiefStatus.Text = text;
    }

    public void SetBehaviorControlStatus(string status) => _behaviorControlMenu?.SetStatus(status);

    public void SetBehaviorControlAvailability(IReadOnlyDictionary<BehaviorControlAction, bool> availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        _behaviorControlMenu?.SetAvailability(availability);
    }

    private void UpdateCharacterMenu()
    {
        if (_pairMenu is not null) _pairMenu.Visible = _coexisting;
        if (_pairDialogueItem is not null) _pairDialogueItem.Enabled = _coexisting && _speechBubbles.Checked && !_quietMode.Checked;
        _fastingMode.Visible = _coexisting || _character.SupportsFood;
        _fastingMode.ToolTipText = _coexisting
            ? "糯糯不再饥饿，同时隐藏双方进度条；菲比的捣蛋蓄力不变。"
            : "糯糯不再饥饿，并隐藏饥饿条。";
        _allowMischief.Visible = _coexisting || _character.UsesMischief;
        _mischiefStatus.Visible = _coexisting || _character.UsesMischief;
        _prankOnce.Visible = (_coexisting || _character.UsesMischief) && _behaviorControlMenu is null;
        _tease.Visible = (_coexisting || _character.UsesMischief) && _behaviorControlMenu is null;
        _returnWindows.Visible = _coexisting || _character.UsesMischief;
        _behaviorControlMenu?.SetCharacter(_character);
    }

    private string TrayTitle => _coexisting ? "啾糯桌宠 · 双宠共存"
        : _behaviorControlMenu is null ? $"啾糯桌宠 · {_character.DisplayName}"
        : $"啾糯桌宠 · 行为控制版 · {_character.DisplayName}";

    private void UpdateSizeLabel()
    {
        _sizeLabel.Text = $"{(_coexisting ? "两人" : _character.DisplayName)}大小  {_sizeSlider.Value}%";
    }

    public void ShowMenu()
    {
        if (_disposed || _menu.IsDisposed || _menu.Visible || IsMenuOpen) return;
        _menu.Show(Forms.Cursor.Position);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _customIcon?.Dispose();
    }

    private sealed class BehaviorControlMenu
    {
        private readonly Dictionary<BehaviorControlAction, Forms.ToolStripMenuItem> _items = new();
        private readonly Dictionary<string, Forms.ToolStripMenuItem> _groups = new(StringComparer.Ordinal);
        private PetCharacterProfile _character;

        public Forms.ToolStripMenuItem Status { get; } = new("行为控制版：请选择行为") { Enabled = false };
        public Forms.ToolStripMenuItem Actions { get; } = new("直接触发行为");
        public Forms.ToolStripMenuItem Stop { get; }
        public Forms.ToolStripMenuItem Autonomy { get; }

        public BehaviorControlMenu(PetCharacterProfile character, Action<BehaviorControlAction> request,
            Action stop, Action<bool> autonomyChanged)
        {
            _character = character;
            Stop = new Forms.ToolStripMenuItem("停止当前行为", null, (_, _) => stop());
            Autonomy = CreateToggle("自动日常活动（行为控制版）", false);
            Autonomy.ToolTipText = "开启后允许自行散步、休息等日常活动；此选项仅用于当前行为控制会话。";
            Autonomy.CheckedChanged += (_, _) => autonomyChanged(Autonomy.Checked);
            foreach (var definition in BehaviorControlCatalog.All.Where(item => item.Action != BehaviorControlAction.Stop))
            {
                if (!_groups.TryGetValue(definition.Group, out var group))
                {
                    group = new Forms.ToolStripMenuItem(definition.Group);
                    _groups.Add(definition.Group, group);
                    Actions.DropDownItems.Add(group);
                }
                var item = new Forms.ToolStripMenuItem(definition.Title)
                {
                    Tag = definition.Action,
                    ToolTipText = definition.Description,
                    Enabled = false,
                };
                item.Click += (_, _) => request(definition.Action);
                _items.Add(definition.Action, item);
                group.DropDownItems.Add(item);
            }
            SetCharacter(character);
        }

        public void SetStatus(string status)
        {
            var text = $"行为控制版：{status}";
            if (!string.Equals(Status.Text, text, StringComparison.Ordinal)) Status.Text = text;
        }

        public void SetCharacter(PetCharacterProfile character)
        {
            _character = character;
            foreach (var definition in BehaviorControlCatalog.All.Where(item => item.Action != BehaviorControlAction.Stop))
            {
                var item = _items[definition.Action];
                item.Available = definition.Supports(character);
                item.Enabled = false;
            }
            foreach (var group in _groups.Values)
                group.Available = group.DropDownItems.Cast<Forms.ToolStripItem>().Any(item => item.Available);
        }

        public void SetAvailability(IReadOnlyDictionary<BehaviorControlAction, bool> availability)
        {
            foreach (var (action, item) in _items)
                item.Enabled = BehaviorControlCatalog.Get(action).Supports(_character) &&
                    availability.TryGetValue(action, out var enabled) && enabled;
        }
    }

    private sealed class MenuInteractionStrip : Forms.ContextMenuStrip
    {
        private bool _disposingMenu;
        public bool IsInteractionOpen { get; private set; }
        public event Action<bool>? InteractionOpenChanged;

        protected override void OnOpening(CancelEventArgs e)
        {
            if (_disposingMenu || IsDisposed)
            {
                e.Cancel = true;
                return;
            }
            var completed = false;
            try
            {
                // Notify the WPF owner synchronously before opening handlers can
                // enter the menu's message loop or trigger another render tick.
                SetInteractionOpen(true);
                if (_disposingMenu || IsDisposed) { e.Cancel = true; return; }
                base.OnOpening(e);
                completed = true;
            }
            finally
            {
                // A cancelled Opening has neither an Opened nor a Closed event.
                if (!completed || e.Cancel || _disposingMenu || IsDisposed) SetInteractionOpen(false);
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            if (_disposingMenu || IsDisposed) return;
            SetInteractionOpen(true);
            base.OnOpened(e);
        }

        protected override void OnClosed(Forms.ToolStripDropDownClosedEventArgs e)
        {
            SetInteractionOpen(false);
            base.OnClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposingMenu = true;
                SetInteractionOpen(false);
            }
            base.Dispose(disposing);
        }

        private void SetInteractionOpen(bool open)
        {
            if (IsInteractionOpen == open) return;
            IsInteractionOpen = open;
            InteractionOpenChanged?.Invoke(open);
        }
    }
}
