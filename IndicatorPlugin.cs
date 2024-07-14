using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        public IndicatorPlugin(ISettings settings, ILogger logger)
        {
            Settings = settings;
            Logger = logger;
            tokenSetter = Tokens.CreateTokenSetter("IndicatorPlugin");
            Logger.Log(settings.Get<string>(lastMapConfigEntry), LogLevel.Trace);
            perfectThreshold = GetPerfectThreshold();
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

            Tokens.AllTokens["hitErrors"].ValueUpdated += OnHitErrorsChanged;
            Tokens.AllTokens["status"].ValueUpdated += OnStatusChanged;

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
            for (int i = hitErrorCount; i < hitErrors.Count; i++)
            {
                int hitError = hitErrors[i];
                string indicatorValue = GetIndicatorValue(hitError, perfectThreshold, ref earlyCount, ref lateCount, ref perfectCount);

                hitErrorSum += hitError;
                hitErrorCount++;
                double averageHitError = (double)hitErrorSum / hitErrorCount;

                UpdateTokenValues(averageHitError, earlyCount, perfectCount, lateCount, hitError);
                Logger.Log($"Processed hit error: {hitError}, Indicator: {indicatorValue}, Average Hit Error: {averageHitError:F2}", LogLevel.Trace);
            }
        }

        private static double GetPerfectThreshold()
        {
            double threshold = 16.5;

            if (Tokens.AllTokens.TryGetValue("mods", out var modsToken) && modsToken.Value is string mods)
            {
                if (mods.Contains("EZ"))
                {
                    threshold = 22.5;
                }
                else if (mods.Contains("HR"))
                {
                    threshold = 11.5;
                }
            }

            return threshold;
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

        private void UpdateTokenValues(double averageHitError, int earlyCount, int perfectCount, int lateCount, int hitError)
        {
            tokenSetter("averageHitErrors", $"{averageHitError:F2}", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("earlyCount", earlyCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("perfectCount", perfectCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("lateCount", lateCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);

            string earlyMs = "", lateMs = "", indicatorEarly = "", indicatorLate = "";
            if (Math.Abs(hitError) > perfectThreshold)
            {
                if (hitError < 0)
                {
                    earlyMs = $"{-Math.Abs(perfectThreshold + hitError)}";
                    indicatorEarly = "EARLY";
                }
                else
                {
                    lateMs = $"+{hitError - perfectThreshold}";
                    indicatorLate = "LATE";
                }
            }

            tokenSetter("earlyMs", earlyMs, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("lateMs", lateMs, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("indicatorEarly", indicatorEarly, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
            tokenSetter("indicatorLate", indicatorLate, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching | OsuStatus.ResultsScreen);
        }

        private void ResetValues()
        {
            hitErrorSum = 0;
            hitErrorCount = 0;
            earlyCount = 0;
            lateCount = 0;
            perfectCount = 0;
            UpdateTokenValues(0, 0, 0, 0, 0);
        }

        private void FreezeValues()
        {
            UpdateTokenValues((double)hitErrorSum / hitErrorCount, earlyCount, perfectCount, lateCount, 0);
        }
    }
}