using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Umbra.Common;

namespace Umbra.Plugin.AutoRetainer.Services;

/// <summary>
/// AutoRetainer の設定から、回収できる人数とベンチャー中の人数を読む。
/// 毎回は読まず、ベル／工房を触ったとき、設定が変わったとき、次の帰還時刻だけ更新する。
/// </summary>
public sealed class AutoRetainerStatusReader : IDisposable
{
    private static readonly string[] WatchedAddons =
    [
        "RetainerList",
        "Retainer",
        "RetainerTaskResult",
        "HousingWorkshopSubmersibleList",
        "HousingWorkshopAirshipList",
        "SubmersibleExplorationResult",
        "AirShipExplorationResult"
    ];

    private static readonly TimeSpan EventDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan WatcherDebounce = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SafetyRefresh = TimeSpan.FromMinutes(10);

    private readonly string _configPath;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly IGameGui _gameGui;
    private readonly FileSystemWatcher? _watcher;
    private readonly Dictionary<string, bool> _addonOpen = [];
    private readonly object _lock = new();

    private ArRoster _roster = new();
    private AutoRetainerStatus _cached = AutoRetainerStatus.Unavailable;
    private bool _dirty = true;
    private DateTime _refreshAfterUtc = DateTime.MinValue;
    private DateTime _nextCompletionUtc = DateTime.MaxValue;
    private DateTime _safetyRefreshUtc = DateTime.UtcNow;
    private DateTime _watcherRefreshUtc = DateTime.MaxValue;
    private bool _wasAtBell;

    public AutoRetainerStatusReader()
    {
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "pluginConfigs",
            "AutoRetainer",
            "DefaultConfig.json");

        _condition = Framework.Service<ICondition>();
        _clientState = Framework.Service<IClientState>();
        _gameGui = Framework.Service<IGameGui>();
        _wasAtBell = _condition[ConditionFlag.OccupiedSummoningBell];

        foreach (var addon in WatchedAddons)
            _addonOpen[addon] = IsAddonOpen(addon);

        _condition.ConditionChange += OnConditionChange;
        _clientState.Login += OnLogin;

        var directory = Path.GetDirectoryName(_configPath);
        if (directory != null && Directory.Exists(directory))
        {
            _watcher = new FileSystemWatcher(directory, "DefaultConfig.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += OnConfigFileChanged;
            _watcher.Created += OnConfigFileChanged;
            _watcher.Renamed += OnConfigFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public AutoRetainerStatus Read(Func<ulong, bool> isCharacterEnabled, Func<ulong, string, bool> isRetainerEnabled)
    {
        PollGameUi();

        var now = DateTime.UtcNow;
        var shouldReload = false;

        lock (_lock)
        {
            if (_dirty
                || now >= _refreshAfterUtc
                || now >= _nextCompletionUtc
                || now >= _safetyRefreshUtc
                || now >= _watcherRefreshUtc)
            {
                shouldReload = true;
                _dirty = false;
                _refreshAfterUtc = DateTime.MaxValue;
                _watcherRefreshUtc = DateTime.MaxValue;
            }
        }

        if (shouldReload)
        {
            try
            {
                _roster = ArRoster.Load();
            }
            catch (Exception ex)
            {
                Logger.Warning($"AutoRetainer config read failed: {ex.Message}");
                _cached = AutoRetainerStatus.Unavailable;
                _nextCompletionUtc = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                return _cached;
            }

            _safetyRefreshUtc = now + SafetyRefresh;
        }

        var previousText = _cached.Text;
        var (status, nextCompletionUtc) = BuildStatus(_roster, isCharacterEnabled, isRetainerEnabled);
        _cached = status;
        _nextCompletionUtc = nextCompletionUtc;

        if (status.Text != previousText)
            Logger.Info($"AutoRetainer status: {status.Text}");

        return _cached;
    }

    public void Dispose()
    {
        _condition.ConditionChange -= OnConditionChange;
        _clientState.Login -= OnLogin;

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnConfigFileChanged;
            _watcher.Created -= OnConfigFileChanged;
            _watcher.Renamed -= OnConfigFileChanged;
            _watcher.Dispose();
        }
    }

    private void PollGameUi()
    {
        foreach (var addon in WatchedAddons)
        {
            var open = IsAddonOpen(addon);
            if (_addonOpen[addon] && !open)
                RequestRefresh(EventDelay);

            _addonOpen[addon] = open;
        }
    }

    private bool IsAddonOpen(string name)
    {
        return _gameGui.GetAddonByName(name) != nint.Zero;
    }

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (flag != ConditionFlag.OccupiedSummoningBell)
            return;

        if (_wasAtBell && !value)
            RequestRefresh(EventDelay);

        _wasAtBell = value;
    }

    private void OnLogin()
    {
        RequestRefresh(TimeSpan.FromSeconds(2));
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs args)
    {
        lock (_lock)
        {
            _watcherRefreshUtc = DateTime.UtcNow + WatcherDebounce;
        }
    }

    private void RequestRefresh(TimeSpan delay)
    {
        lock (_lock)
        {
            var when = DateTime.UtcNow + delay;
            if (when < _refreshAfterUtc)
                _refreshAfterUtc = when;
        }
    }

    private static (AutoRetainerStatus Status, DateTime NextCompletionUtc) BuildStatus(
        ArRoster roster,
        Func<ulong, bool> isCharacterEnabled,
        Func<ulong, string, bool> isRetainerEnabled)
    {
        if (roster.Characters.Count == 0)
            return (AutoRetainerStatus.Unavailable, DateTime.MaxValue);

        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var rReady = 0;
        var rBusy = 0;
        var mReady = 0;
        var mBusy = 0;
        var nextCompletion = long.MaxValue;

        foreach (var character in roster.Characters)
        {
            if (!isCharacterEnabled(character.Cid))
                continue;

            if (!character.ExcludeRetainer)
            {
                foreach (var retainer in character.Retainers)
                {
                    if (!isRetainerEnabled(character.Cid, retainer.Name) || !retainer.HasVenture)
                        continue;

                    if (retainer.VentureEndsAt <= now)
                    {
                        rReady++;
                    }
                    else
                    {
                        rBusy++;
                        if (retainer.VentureEndsAt < nextCompletion)
                            nextCompletion = retainer.VentureEndsAt;
                    }
                }
            }

            if (!character.ExcludeWorkshop)
            {
                foreach (var sub in character.Submarines)
                {
                    if (sub.ReturnTime == 0)
                        continue;

                    if ((long)sub.ReturnTime <= now)
                    {
                        mReady++;
                    }
                    else
                    {
                        mBusy++;
                        if ((long)sub.ReturnTime < nextCompletion)
                            nextCompletion = (long)sub.ReturnTime;
                    }
                }
            }
        }

        var nextCompletionUtc = nextCompletion == long.MaxValue
            ? DateTime.MaxValue
            : DateTimeOffset.FromUnixTimeSeconds(nextCompletion).UtcDateTime;

        return (
            new AutoRetainerStatus(
                $"R{rReady}|{rBusy}　M{mReady}|{mBusy}",
                $"リテイナー: 回収できる {rReady} 人 / ベンチャー中 {rBusy} 人\n潜水艦: 回収できる {mReady} 隻 / 航海中 {mBusy} 隻\nクリックで AutoRetainer (/ays) を開きます。",
                true,
                rReady,
                rBusy,
                mReady,
                mBusy),
            nextCompletionUtc);
    }
}

public readonly record struct AutoRetainerStatus(
    string Text,
    string Tooltip,
    bool Available,
    int RetainerReady,
    int RetainerBusy,
    int SubReady,
    int SubBusy)
{
    public bool HasRetainerReady => RetainerReady > 0;
    public bool HasSubReady => SubReady > 0;

    public static AutoRetainerStatus Unavailable { get; } = new(
        "ARオフ",
        "AutoRetainer のデータを読めませんでした。プラグインを有効にして、一度 /ays を開いてください。",
        false,
        0,
        0,
        0,
        0);
}
