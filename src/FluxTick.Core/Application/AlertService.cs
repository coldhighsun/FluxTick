using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Core.Application;

/// <summary>
/// Event data for an alert that fired.
/// </summary>
/// <param name="Rule">
/// A copy of the rule that fired.
/// </param>
/// <param name="Asset">
/// The instrument the rule watches.
/// </param>
/// <param name="Quote">
/// The quote that satisfied the rule.
/// </param>
public sealed record AlertTriggeredEventArgs(AlertRule Rule, Asset Asset, Quote Quote);

/// <summary>
/// Watches the alert rules against the live quotes and announces the ones that fire. A rule fires when its condition
/// becomes true, not while it stays true, so a price that rests above a threshold does not alert with every tick.
/// </summary>
public sealed class AlertService : IDisposable
{
    /// <summary>
    /// The source of the snapshots.
    /// </summary>
    private readonly MarketDataService _market;

    /// <summary>
    /// The settings that hold the rules.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The service logger.
    /// </summary>
    private readonly ILogger<AlertService> _logger;

    /// <summary>
    /// Guards the mutable state below.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The enabled rules keyed by instrument, with what they were last evaluated to. Guarded by <see cref="_gate"/>.
    /// </summary>
    private Dictionary<AssetId, List<Watched>> _watched = [];

    /// <summary>
    /// How many rules are in <see cref="_watched"/>. Guarded by <see cref="_gate"/>.
    /// </summary>
    private int _watchedCount;

    /// <summary>
    /// Keeps the data sources connected while a rule is enabled. Guarded by <see cref="_gate"/>.
    /// </summary>
    private IDisposable? _usage;

    /// <summary>
    /// Whether the service was disposed. Guarded by <see cref="_gate"/>.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlertService"/> class.
    /// </summary>
    /// <param name="market">
    /// The source of the snapshots.
    /// </param>
    /// <param name="settings">
    /// The settings that hold the rules.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    public AlertService(MarketDataService market, SettingsManager settings, ILogger<AlertService> logger)
    {
        _market = market;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Raised when a rule fires. May be raised on any thread, the one that delivers the quote, so handlers must return
    /// quickly and post to the UI thread instead of waiting.
    /// </summary>
    public event EventHandler<AlertTriggeredEventArgs>? AlertTriggered;

    /// <summary>
    /// Starts watching the rules of the current settings. Call it once the settings are loaded.
    /// </summary>
    public void Start()
    {
        // Unsubscribing first keeps a second call from subscribing twice, which would fire every alert twice.
        _market.SnapshotChanged -= OnSnapshotChanged;
        _settings.Changed -= OnSettingsChanged;
        _market.SnapshotChanged += OnSnapshotChanged;
        _settings.Changed += OnSettingsChanged;
        Reload();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _market.SnapshotChanged -= OnSnapshotChanged;
        _settings.Changed -= OnSettingsChanged;
        lock (_gate)
        {
            _disposed = true;
            _watched = [];
            _usage?.Dispose();
            _usage = null;
        }
    }

    /// <summary>
    /// Re-reads the rules when the settings changed.
    /// </summary>
    /// <param name="sender">
    /// The settings manager.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnSettingsChanged(object? sender, EventArgs e) => Reload();

    /// <summary>
    /// Takes over the enabled rules of the current settings. A rule that stays as it is keeps what it was last
    /// evaluated to; a new or changed one starts again, so it only fires when its condition becomes true after that.
    /// The data sources stay connected while at least one rule is enabled.
    /// </summary>
    private void Reload()
    {
        var rules = _settings.Current.Alerts.Where(r => r.Enabled).ToList();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Most changes of the settings, such as the position of the window, are not about the alerts.
            if (rules.Count == _watchedCount && rules.TrueForAll(IsWatchedAsIs))
            {
                return;
            }

            var previous = _watched.Values.SelectMany(list => list).ToDictionary(w => w.Rule.Id);
            var next = new Dictionary<AssetId, List<Watched>>();
            foreach (var rule in rules)
            {
                var kept = previous.GetValueOrDefault(rule.Id);
                var same = kept is not null
                    && kept.Rule.AssetId == rule.AssetId
                    && kept.Rule.Condition == rule.Condition
                    && kept.Rule.Threshold == rule.Threshold;
                var watched = new Watched(rule.Clone()) { Satisfied = same ? kept!.Satisfied : null };
                if (!next.TryGetValue(rule.AssetId, out var list))
                {
                    next[rule.AssetId] = list = [];
                }

                list.Add(watched);
            }

            _watched = next;
            _watchedCount = rules.Count;
            if (rules.Count > 0)
            {
                _usage ??= _market.AcquireUsage();
            }
            else
            {
                _usage?.Dispose();
                _usage = null;
            }
        }
    }

    /// <summary>
    /// Determines whether a rule is watched exactly as it is. Must be called while holding the lock.
    /// </summary>
    /// <param name="rule">
    /// The rule.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the rule is watched with the same values.
    /// </returns>
    private bool IsWatchedAsIs(AlertRule rule) =>
        _watched.TryGetValue(rule.AssetId, out var list) && list.Exists(w => w.Rule.SameAs(rule));

    /// <summary>
    /// Evaluates the rules of an instrument against its new snapshot.
    /// </summary>
    /// <param name="sender">
    /// The market data service.
    /// </param>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    private void OnSnapshotChanged(object? sender, AssetSnapshot snapshot)
    {
        // A price that is not live says nothing about the market: it neither fires a rule nor counts as the condition
        // having stopped.
        if (snapshot.Quote is not { } quote || snapshot.Health != AssetHealth.Live)
        {
            return;
        }

        List<AlertRule> fired = [];
        lock (_gate)
        {
            if (!_watched.TryGetValue(snapshot.Asset.Id, out var list))
            {
                return;
            }

            foreach (var watched in list)
            {
                var satisfied = AlertEvaluator.IsSatisfied(watched.Rule, quote);
                if (AlertEvaluator.Fires(watched.Satisfied, satisfied))
                {
                    fired.Add(watched.Rule.Clone());
                }

                watched.Satisfied = satisfied;
            }
        }

        foreach (var rule in fired)
        {
            Fire(rule, snapshot.Asset, quote);
        }
    }

    /// <summary>
    /// Announces a rule that fired and, unless it repeats, turns it off.
    /// </summary>
    /// <param name="rule">
    /// The rule.
    /// </param>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="quote">
    /// The quote that satisfied the rule.
    /// </param>
    private void Fire(AlertRule rule, Asset asset, Quote quote)
    {
        try
        {
            AlertTriggered?.Invoke(this, new AlertTriggeredEventArgs(rule, asset, quote));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "A handler of an alert for {AssetId} failed", asset.Id);
        }

        if (!rule.Repeat)
        {
            _ = DisableAsync(rule.Id);
        }
    }

    /// <summary>
    /// Turns a rule off after it fired.
    /// </summary>
    /// <param name="id">
    /// The id of the rule.
    /// </param>
    /// <returns>
    /// A task that completes once the settings are updated.
    /// </returns>
    private async Task DisableAsync(string id)
    {
        try
        {
            await _settings.UpdateAsync(settings =>
            {
                foreach (var rule in settings.Alerts.Where(r => r.Id == id))
                {
                    rule.Enabled = false;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Could not turn off alert {AlertId} after it fired", id);
        }
    }

    /// <summary>
    /// An enabled rule with what it was last evaluated to.
    /// </summary>
    /// <param name="rule">
    /// The rule.
    /// </param>
    private sealed class Watched(AlertRule rule)
    {
        /// <summary>
        /// Gets the rule.
        /// </summary>
        public AlertRule Rule { get; } = rule;

        /// <summary>
        /// Gets or sets whether the condition held at the last evaluation, or <see langword="null"/> when there was none.
        /// </summary>
        public bool? Satisfied { get; set; }
    }
}
