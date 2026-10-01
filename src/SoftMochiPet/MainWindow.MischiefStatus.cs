using Brush = System.Windows.Media.Brush;
using SoftMochiPet.Core;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private Brush? _mischiefActiveBrush;
    private Brush MischiefActiveBrush => _mischiefActiveBrush ??= MischiefBarFill.Background;
    private bool _mischiefWaitingForTarget;
    private string? _lastMischiefStatusReason;
    private double _nextMischiefStatusLog;

    private void UpdateMischiefStatus()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var reason = !_settings.AllowMischief ? "disabled"
            : _sessionLocked ? "session_locked"
            : _settings.QuietMode ? "quiet"
            : _state is PetState.Sleeping or PetState.Waking ? "sleeping"
            : _prankPhase != PrankPhase.None ? "performing"
            : _mischief.CooldownRemainingSeconds > 0 ? "cooldown"
            : _isDragging ? "dragging"
            : _animator.Hatless ? "hat_recovery"
            : _prankArtwork is null || _windowPranks is null ? "assets_unavailable"
            : HasHeldPrank && _mischief.Value >= 100 ? "hat_occupied_full"
            : _mischiefWaitingForTarget ? "no_eligible_window"
            : _enclosureHandle != IntPtr.Zero ? "enclosed"
            : _mischief.AutonomousActionDue && _state is not (PetState.Idle or PetState.Curious or PetState.Running)
                ? "pet_busy" : "charging";
        var cooldown = (int)Math.Ceiling(_mischief.CooldownRemainingSeconds);
        var attempt = _mischief.SecondsUntilNextAttempt is { } seconds
            ? Math.Max(seconds, _nextPrankSearch - now) : (double?)null;
        var countdown = (int)Math.Ceiling(Math.Max(0, attempt ?? 0));
        var text = reason switch
        {
            "disabled" => "自主捣蛋已关闭",
            "session_locked" => "已暂停（锁屏）",
            "quiet" => "已暂停（安静模式）",
            "sleeping" => "已暂停（睡觉）",
            "performing" => _manualRestoreInProgress ? "归还窗口中" : "捣蛋中",
            "cooldown" => $"冷却中 · {cooldown} 秒",
            "dragging" => "蓄力中 · 等你松手",
            "hat_recovery" => "等她戴好帽子",
            "assets_unavailable" => "动作素材不可用（见日志）",
            "hat_occupied_full" => "已满 · 等帽子腾空，小捣蛋照常",
            "no_eligible_window" => $"等待合适窗口 · {countdown} 秒后重试",
            "enclosed" => "蓄力中 · 等她离开窗口内部",
            "pet_busy" => "等当前动作结束",
            _ => $"蓄力中 · {Math.Floor(_mischief.Value):0}/100" + (HasHeldPrank ? " · 帽子已占用" : ""),
        };
        _trayIcon.SetMischiefStatus(text);
        if (reason == _lastMischiefStatusReason && now < _nextMischiefStatusLog) return;
        _lastMischiefStatusReason = reason;
        _nextMischiefStatusLog = now + 30;
        DiagnosticsLog.WriteEvent("MischiefStatus", ("Reason", reason),
            ("Value", Math.Round(_mischief.Value, 2)), ("CooldownSeconds", cooldown),
            ("NextAttemptSeconds", attempt is { } remaining ? Math.Round(Math.Max(0, remaining), 2) : null),
            ("AutonomyEnabled", _settings.AllowMischief), ("HatOccupied", HasHeldPrank), ("State", _state));
    }
}
