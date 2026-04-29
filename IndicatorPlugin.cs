using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CollectionManager.Enums;
using StreamCompanionTypes.Attributes;
using StreamCompanionTypes.DataTypes;
using StreamCompanionTypes.Enums;
using StreamCompanionTypes.Interfaces;
using StreamCompanionTypes.Interfaces.Services;
using StreamCompanionTypes.Interfaces.Sources;

namespace Indicator
{
    [SCPluginDependency("OsuMemoryEventSource", "1.0.0")]
    [SCPlugin("Judgement Indicator", "Shows Early/Late in-game overlay", "C4P741N", "https://github.com/c4p741nth/SC-Indicator-Plugin")]
    public class IndicatorPlugin : IPlugin, ISettingsSource, ITokensSource
    {
        public string SettingGroup => "Judgement Indicator Setting";
        private SettingsUserControl? SettingsUserControl;

        private readonly ISettings Settings;
        private readonly ILogger Logger;
        private readonly Tokens.TokenSetter tokenSetter;
        private CancellationTokenSource? tokenUpdateCancellationTokenSource;
        public static ConfigEntry lastMapConfigEntry = new ConfigEntry("IndicatorConfig", "defaultValue");

        private int hitErrorSum;
        private int hitErrorCount;
        private int earlyCount;
        private int lateCount;
        private int perfectCount;
        private double perfectThreshold;
        private CancellationTokenSource? _indicatorClearCts;
        private PlayMode _playMode = PlayMode.Osu;

        public IndicatorPlugin(ISettings settings, ILogger logger)
        {
            Settings = settings;
            Logger = logger;
            tokenSetter = Tokens.CreateTokenSetter("IndicatorPlugin");
            Logger.Log(settings.Get<string>(lastMapConfigEntry), LogLevel.Trace);
            perfectThreshold = 31.5; // OD 8 default; overwritten by UpdatePerfectThreshold() on first map load
        }

        public void Free()
        {
            SettingsUserControl?.Dispose();
        }

        public object GetUiSettings()
        {
            if (SettingsUserControl == null || SettingsUserControl.IsDisposed)
            {
                SettingsUserControl = new SettingsUserControl();
            }
            return SettingsUserControl;
        }

        public Task CreateTokensAsync(IMapSearchResult map, CancellationToken cancellationToken)
        {
            tokenUpdateCancellationTokenSource?.Cancel();
            tokenUpdateCancellationTokenSource = new CancellationTokenSource();

            InitializeTokens();

            Settings.Add(lastMapConfigEntry.Name, map.MapSearchString);
            Logger.Log("CreateTokensAsync", LogLevel.Trace);

            _playMode = map.PlayMode ?? PlayMode.Osu;

            // Unsubscribe first to avoid stacking handlers on each map change
            Tokens.AllTokens["hitErrors"].ValueUpdated -= OnHitErrorsChanged;
            Tokens.AllTokens["status"].ValueUpdated -= OnStatusChanged;
            if (Tokens.AllTokens.TryGetValue("mOD", out var mODToken))
                mODToken.ValueUpdated -= OnModdedODChanged;

            Tokens.AllTokens["hitErrors"].ValueUpdated += OnHitErrorsChanged;
            Tokens.AllTokens["status"].ValueUpdated += OnStatusChanged;
            if (Tokens.AllTokens.TryGetValue("mOD", out var mODTokenSub))
                mODTokenSub.ValueUpdated += OnModdedODChanged;

            // Apply threshold immediately for the current map/mods
            UpdatePerfectThreshold();

            return Task.CompletedTask;
        }

        private void OnStatusChanged(object? sender, IToken e)
        {
            if (e.Value is OsuStatus status)
            {
                if (status == OsuStatus.Playing || status == OsuStatus.Watching)
                {
                    ResetValues();
                }
                else if (status == OsuStatus.ResultsScreen)
                {
                    FreezeValues();
                }
            }
        }

        private void InitializeTokens()
        {
            var tokenNames = new[] { "averageHitErrors", "earlyCount", "perfectCount", "lateCount", "earlyMs", "lateMs" };
            foreach (var tokenName in tokenNames)
            {
                tokenSetter(tokenName, "0", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            }
            tokenSetter("indicatorEarly", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("indicatorLate", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
        }

        private void OnHitErrorsChanged(object? sender, IToken e)
        {
            if (e.Value is List<int> hitErrors)
            {
                ProcessHitErrors(hitErrors);
            }
            else if (hitErrorCount > 0)
            {
                FreezeValues();
            }
            else
            {
                ResetValues();
            }
        }

        private void ProcessHitErrors(List<int> hitErrors)
        {
            int newHitsStart = hitErrorCount;

            // Count all new hits first — avoid calling tokenSetter inside the loop
            for (int i = newHitsStart; i < hitErrors.Count; i++)
            {
                int hitError = hitErrors[i];
                GetIndicatorValue(hitError, perfectThreshold, ref earlyCount, ref lateCount, ref perfectCount);
                hitErrorSum += hitError;
                hitErrorCount++;
            }

            if (hitErrors.Count > newHitsStart)
            {
                double averageHitError = (double)hitErrorSum / hitErrorCount;
                int latestHitError = hitErrors[hitErrors.Count - 1];

                tokenSetter("averageHitErrors", $"{averageHitError:F2}", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
                tokenSetter("earlyCount", earlyCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
                tokenSetter("perfectCount", perfectCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
                tokenSetter("lateCount", lateCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);

                FlashIndicator(latestHitError);
                Logger.Log($"Processed {hitErrors.Count - newHitsStart} hit(s), latest: {latestHitError}, avg: {averageHitError:F2}", LogLevel.Trace);
            }
        }

        // Clears old indicator instantly, then sets the new one — guarantees a visible change
        // even when two consecutive hits have the same direction.
        private void FlashIndicator(int hitError)
        {
            _indicatorClearCts?.Cancel();
            _indicatorClearCts = new CancellationTokenSource();
            var cts = _indicatorClearCts;

            // Always clear first so the overlay sees the token go "" → value on every hit
            ClearIndicatorTokens();

            if (Math.Abs(hitError) <= perfectThreshold)
            {
                Logger.Log($"[Indicator] hit {hitError}ms — PERFECT (threshold ±{perfectThreshold:F1}ms)", LogLevel.Trace);
                return;
            }

            string earlyMs, lateMs, indicatorEarly, indicatorLate;
            if (hitError < 0)
            {
                earlyMs = $"{-Math.Abs(perfectThreshold + hitError)}";
                lateMs = "";
                indicatorEarly = "EARLY";
                indicatorLate = "";
            }
            else
            {
                earlyMs = "";
                lateMs = $"+{hitError - perfectThreshold}";
                indicatorEarly = "";
                indicatorLate = "LATE";
            }

            tokenSetter("earlyMs", earlyMs, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("lateMs", lateMs, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("indicatorEarly", indicatorEarly, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("indicatorLate", indicatorLate, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);

            _ = Task.Delay(800, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                    ClearIndicatorTokens();
            }, TaskContinuationOptions.None);
        }

        private void ClearIndicatorTokens()
        {
            tokenSetter("earlyMs", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("lateMs", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("indicatorEarly", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("indicatorLate", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
        }

        private void OnModdedODChanged(object? sender, IToken e)
        {
            UpdatePerfectThreshold();
        }

        // Computes the "perfect" hit window threshold in ms based on the current game mode.
        // Uses the same DifficultyRange formula as osu!'s own HitWindows classes.
        private void UpdatePerfectThreshold()
        {
            if (!Tokens.AllTokens.TryGetValue("mOD", out var mODToken))
            {
                Logger.Log("[Indicator] mOD token not found — keeping default threshold", LogLevel.Trace);
                return;
            }

            // mOD is stored as double (Math.Round returns double), but be defensive about float too
            if (!TryGetDouble(mODToken.Value, out double mOD) || mOD <= 0)
            {
                Logger.Log($"[Indicator] mOD value is {mODToken.Value?.GetType().Name ?? "null"} ({mODToken.Value}) — keeping default threshold", LogLevel.Trace);
                return;
            }

            double newThreshold = _playMode switch
            {
                // 300 window: DiffRange(OD, 80→20) - 0.5  (matches stable: 79.5 - 6×OD)
                PlayMode.Osu => Math.Floor(DifficultyRange(mOD, 80, 50, 20)) - 0.5,

                // Great window: DiffRange(OD, 50→20) - 0.5  (49.5 - 3×OD)
                PlayMode.Taiko => Math.Floor(DifficultyRange(mOD, 50, 35, 20)) - 0.5,

                // 300 (Great) window — DT/HT don't affect mania windows in stable
                PlayMode.OsuMania => ComputeManiaThreshold(),

                // osu!catch has no traditional timing windows — disable indicator
                PlayMode.CatchTheBeat => -1,

                // Unknown mode — fall back to osu! standard formula
                _ => Math.Floor(DifficultyRange(mOD, 80, 50, 20)) - 0.5,
            };

            perfectThreshold = newThreshold < 0 ? double.MaxValue / 2 : newThreshold;
            Logger.Log($"[Indicator] threshold={perfectThreshold:F1}ms, mode={_playMode}({(int)_playMode}), mOD={mOD:F2}", LogLevel.Trace);
        }

        // Accept both double and float to be safe against internal SC boxing differences.
        private static bool TryGetDouble(object? value, out double result)
        {
            switch (value)
            {
                case double d: result = d; return true;
                case float f: result = f; return true;
                case int i: result = i; return true;
                default: result = 0; return false;
            }
        }
        // Mania 300 window uses only EZ/HR mod effects on OD (not DT/HT).
        private double ComputeManiaThreshold()
        {
            double od = 8;
            if (Tokens.AllTokens.TryGetValue("od", out var odToken) && odToken.Value is double rawOD)
                od = rawOD;

            if (Tokens.AllTokens.TryGetValue("mods", out var modsToken) && modsToken.Value is string mods)
            {
                if (mods.Contains("HR")) od = Math.Min(10, od * 1.4);
                else if (mods.Contains("EZ")) od *= 0.5;
            }

            // 300 (Great) window range: min=64 (OD0), mid=49 (OD5), max=34 (OD10)
            return Math.Floor(DifficultyRange(od, 64, 49, 34)) + 0.5;
        }

        // Piecewise-linear interpolation matching IBeatmapDifficultyInfo.DifficultyRange.
        // Maps OD [0–10] linearly: 0→min, 5→mid, 10→max.
        private static double DifficultyRange(double od, double min, double mid, double max)
        {
            if (od > 5) return mid + (max - mid) * (od - 5) / 5;
            if (od < 5) return mid + (mid - min) * (od - 5) / 5;
            return mid;
        }

        private static string GetIndicatorValue(int hitError, double threshold, ref int earlyCount, ref int lateCount, ref int perfectCount)
        {
            if (hitError > threshold)
            {
                lateCount++;
                return "LATE";
            }
            else if (hitError < -threshold)
            {
                earlyCount++;
                return "EARLY";
            }
            else if (hitError >= -threshold && hitError <= threshold)
            {
                perfectCount++;
                return "PERFECT";
            }
            else
            {
                return "Unknown";
            }
        }

        private void UpdateTokenValues(double averageHitError, int earlyCount, int perfectCount, int lateCount)
        {
            tokenSetter("averageHitErrors", $"{averageHitError:F2}", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("earlyCount", earlyCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("perfectCount", perfectCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("lateCount", lateCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            ClearIndicatorTokens();
        }

        private void ResetValues()
        {
            _indicatorClearCts?.Cancel();
            hitErrorSum = 0;
            hitErrorCount = 0;
            earlyCount = 0;
            lateCount = 0;
            perfectCount = 0;
            UpdateTokenValues(0, 0, 0, 0);
        }

        private void FreezeValues()
        {
            _indicatorClearCts?.Cancel();
            UpdateTokenValues((double)hitErrorSum / hitErrorCount, earlyCount, perfectCount, lateCount);
        }
    }
}