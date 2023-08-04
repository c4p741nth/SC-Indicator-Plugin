﻿using StreamCompanionTypes.DataTypes;
using StreamCompanionTypes.Enums;
using StreamCompanionTypes.Interfaces;
using StreamCompanionTypes.Interfaces.Consumers;
using StreamCompanionTypes.Interfaces.Services;
using StreamCompanionTypes.Interfaces.Sources;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace indicator
{
    public class IndicatorPlugin : IPlugin, ITokensSource, IMapDataConsumer
    {
        public string Description => "This is EARLY/LATE in-game overlay";
        public string Name => "Indicator Plugin";
        public string Author => "C4P741N";
        public string Url => "github.com/c4p741nth";

        private ISettings Settings;
        private ILogger Logger;
        private Tokens.TokenSetter tokenSetter;
        private CancellationTokenSource tokenUpdateCancellationTokenSource;

        public static ConfigEntry lastMapConfigEntry = new ConfigEntry("Indicator", "defaultValue");

        public IndicatorPlugin(ISettings settings, ILogger logger)
        {
            Settings = settings;
            Logger = logger;
            tokenSetter = Tokens.CreateTokenSetter("IndicatorPlugin");
            Logger.Log(settings.Get<string>(lastMapConfigEntry), LogLevel.Trace);
        }

        public async Task CreateTokensAsync(IMapSearchResult map, CancellationToken cancellationToken)
        {
            // Clear any existing token update tasks if they exist
            tokenUpdateCancellationTokenSource?.Cancel();
            tokenUpdateCancellationTokenSource = new CancellationTokenSource();

            // Initialize token values
            tokenSetter("indicator", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
            tokenSetter("averageHitErrors", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
            tokenSetter("earlyCount", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
            tokenSetter("perfectCount", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
            tokenSetter("lateCount", "", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
            Settings.Add(lastMapConfigEntry.Name, map.MapSearchString);
            Logger.Log("CreateTokensAsync", LogLevel.Trace);

            int hitErrorSum = 0;
            int hitErrorCount = 0;
            int earlyCount = 0;
            int lateCount = 0;
            int perfectCount = 0;

            while (!tokenUpdateCancellationTokenSource.Token.IsCancellationRequested)
            {
                if (Tokens.AllTokens.TryGetValue("hitErrors", out var hitErrorsToken) && hitErrorsToken.Value is List<int> hitErrors)
                {
                    // Get the length or count of hitErrors list
                    int hitErrorsCount = hitErrors.Count;

                    // Only proceed if there are new hitErrors to process
                    if (hitErrorsCount > hitErrorCount)
                    {
                        // Process new hitErrors from hitErrorCount to the latest hitError
                        for (int i = hitErrorCount; i < hitErrorsCount; i++)
                        {
                            int lastHitError = hitErrors[i];
                            string indicatorValue;

                            if (lastHitError >= -16.5 && lastHitError <= 16.5)
                            {
                                indicatorValue = "";
                                perfectCount++;
                            }
                            else if (lastHitError > 16.5)
                            {
                                indicatorValue = "LATE+";
                                lateCount++;
                            }
                            else if (lastHitError < 16.5)
                            {
                                indicatorValue = "EARLY-";
                                earlyCount++;
                            }
                            else
                            {
                                // Handle other cases or set a default value if needed
                                indicatorValue = "Unknown";
                            }

                            // Calculate average hit error
                            hitErrorSum += lastHitError;
                            hitErrorCount++;

                            // Update tokens for each new hitError
                            double averageHitError = (double)hitErrorSum / hitErrorCount;
                            tokenSetter("indicator", $"{indicatorValue}", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
                            tokenSetter("averageHitErrors", $"{averageHitError:F2}", TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
                            tokenSetter("earlyCount", earlyCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
                            tokenSetter("perfectCount", perfectCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
                            tokenSetter("lateCount", lateCount, TokenType.Live, null, null, OsuStatus.Playing | OsuStatus.Watching);
                            Logger.Log(lastHitError, LogLevel.Trace);
                        }
                    }
                }

                // Introduce a small delay to reduce CPU usage
                await Task.Delay(1, tokenUpdateCancellationTokenSource.Token);
            }
        }


        public Task SetNewMapAsync(IMapSearchResult map, CancellationToken cancellationToken)
        {
            // Do: execute actions based on token values
            // Don't: update token values (unless these are live)

            if (map.PlayMode == CollectionManager.Enums.PlayMode.Osu && map.BeatmapsFound.Count > 0)
            {
                var beatmap = map.BeatmapsFound[0];
                var starRating = (double)Tokens.AllTokens["mStars"].Value;
            }

            Logger.Log("SetNewMapAsync", LogLevel.Trace);
            return Task.CompletedTask;
        }
    }
}
