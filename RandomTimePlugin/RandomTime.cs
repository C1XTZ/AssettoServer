using AssettoServer.Server.Configuration;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Weather;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace RandomTimePlugin;

public class RandomTime : BackgroundService
{
    private const int SecondsPerDay = 86400;

    private struct TimeWeight
    {
        internal int Seconds { get; init; }
        internal float PrefixSum { get; init; }
    }

    private readonly WeatherManager _weatherManager;
    private readonly ACServerConfiguration _serverConfiguration;
    private readonly RandomTimeConfiguration _configuration;
    private readonly IRandomWeatherCycle? _weatherCycle;
    private readonly List<TimeWeight> _times = [];

    public RandomTime(
        RandomTimeConfiguration configuration,
        WeatherManager weatherManager,
        ACServerConfiguration serverConfiguration,
        IRandomWeatherCycle? weatherCycle = null)
    {
        _configuration = configuration;
        _weatherManager = weatherManager;
        _serverConfiguration = serverConfiguration;
        _weatherCycle = weatherCycle;

        if (serverConfiguration.Extra.EnableRealTime)
            throw new ConfigurationException("RandomTimePlugin does not work with EnableRealTime enabled");

        if (serverConfiguration.Extra.EnablePlugins?.Contains("TimeDilationPlugin") == true)
            throw new ConfigurationException("RandomTimePlugin does not work with TimeDilationPlugin enabled");

        if (_configuration.SyncTimeToWeatherCycle && serverConfiguration.Extra.EnablePlugins?.Contains("RandomWeatherPlugin") != true)
            throw new ConfigurationException("SyncTimeToWeatherCycle requires RandomWeatherPlugin to be enabled");

        if (_configuration.Mode == RandomTimeMode.Default)
            RecalculateWeights();
    }

    private void RecalculateWeights()
    {
        var parsed = _configuration.TimeWeights
            .Where(tw => tw.Value > 0)
            .Select(tw => (
                Seconds: (int)DateTime.ParseExact(tw.Key, "H:mm", System.Globalization.CultureInfo.InvariantCulture).TimeOfDay.TotalSeconds,
                Weight: tw.Value))
            .ToList();

        float weightSum = parsed.Sum(tw => tw.Weight);

        float prefixSum = 0.0f;
        foreach (var (seconds, weight) in parsed)
        {
            prefixSum += weight / weightSum;
            _times.Add(new TimeWeight
            {
                Seconds = seconds,
                PrefixSum = prefixSum,
            });
        }

        _times.Sort((a, b) => a.PrefixSum.CompareTo(b.PrefixSum));
    }

    private int PickRandomTime()
    {
        float rng = Random.Shared.NextSingle();
        int seconds = 0;

        int begin = 0, end = _times.Count - 1;
        while (begin <= end)
        {
            int i = (begin + end) / 2;

            if (_times[i].PrefixSum <= rng)
            {
                begin = i + 1;
            }
            else
            {
                end = i - 1;
                seconds = _times[i].Seconds;
            }
        }

        return seconds;
    }

    private int PickNextTime()
    {
        return _configuration.Mode == RandomTimeMode.TrueRandom
            ? Random.Shared.Next(SecondsPerDay)
            : PickRandomTime();
    }

    private int GetCurrentSeconds() => (int)(_weatherManager.CurrentDateTime.TimeOfDay.TickOfDay / 10_000_000);

    private int ApplyTimeCycle(int transitionDuration, double baseMultiplier, int lastSetSeconds, int holdDuration, CancellationToken stoppingToken)
    {
        int next = PickNextTime();
        double multiplier;

        if (transitionDuration <= 0)
        {
            multiplier = baseMultiplier;
            transitionDuration = 0;

            _weatherManager.SetTime(next);
            _serverConfiguration.Server.TimeOfDayMultiplier = (float)baseMultiplier;
        }
        else
        {
            int rangeSeconds = _configuration.SkipTimeChangeRangeSeconds;
            int reference = rangeSeconds > 0
                ? GetCurrentSeconds()
                : lastSetSeconds;

            int forwardDelta = ((next - reference) % SecondsPerDay + SecondsPerDay) % SecondsPerDay;
            int backwardDelta = SecondsPerDay - forwardDelta;
            bool skip = rangeSeconds > 0
                ? Math.Min(forwardDelta, backwardDelta) <= rangeSeconds
                : forwardDelta == 0;

            if (skip)
            {
                Log.Information("Random time picked {Time} matched last set time or was within skip range, skipping transition. Next attempt in {HoldDuration} minutes",
                    TimeSpan.FromSeconds(next), Math.Round(holdDuration / 60_000.0, 1));
                return lastSetSeconds;
            }

            bool goBackwards = _configuration.AllowBackwardsTimeTransitions && backwardDelta < forwardDelta;
            int delta = goBackwards ? -backwardDelta : forwardDelta;
            multiplier = delta / (transitionDuration / 1000.0);
            _serverConfiguration.Server.TimeOfDayMultiplier = (float)multiplier;

            _ = Task.Delay(transitionDuration, stoppingToken)
                .ContinueWith(_ =>
                {
                    _weatherManager.SetTime(next);
                    _serverConfiguration.Server.TimeOfDayMultiplier = (float)baseMultiplier;
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }

        Log.Information("Random time transitioning to {Time}, transition duration {TransitionDuration} seconds at {Multiplier}x speed, time duration {HoldDuration} minutes",
            TimeSpan.FromSeconds(next), Math.Round(transitionDuration / 1000.0), Math.Round(multiplier, 1), Math.Round(holdDuration / 60_000.0, 1));

        return next;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _configuration.SyncTimeToWeatherCycle
            ? SyncedLoop(stoppingToken)
            : IndependentLoop(stoppingToken);
    }

    private async Task IndependentLoop(CancellationToken stoppingToken)
    {
        bool firstRun = true;
        double baseMultiplier = _serverConfiguration.Server.TimeOfDayMultiplier;
        int lastSetSeconds = GetCurrentSeconds();
        int transitionDuration = 1000;
        int holdDuration = 1000;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                holdDuration = Random.Shared.Next(_configuration.MinTimeDurationMilliseconds, _configuration.MaxTimeDurationMilliseconds);
                transitionDuration = (firstRun && _configuration.RandomizeInitialTime) ? 0 : Random.Shared.Next(_configuration.MinTransitionDurationMilliseconds, _configuration.MaxTransitionDurationMilliseconds);
                firstRun = false;

                lastSetSeconds = ApplyTimeCycle(transitionDuration, baseMultiplier, lastSetSeconds, holdDuration, stoppingToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during random time update");
            }
            finally
            {
                await Task.Delay(transitionDuration + holdDuration, stoppingToken);
            }
        }
    }

    private Task SyncedLoop(CancellationToken stoppingToken)
    {
        double baseMultiplier = _serverConfiguration.Server.TimeOfDayMultiplier;
        int lastSetSeconds = GetCurrentSeconds();

        _weatherCycle!.CycleStarted += (_, args) =>
        {
            try
            {
                lastSetSeconds = ApplyTimeCycle(args.TransitionDurationMs, baseMultiplier, lastSetSeconds, args.WeatherDurationMs, stoppingToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during synced random time update");
            }
        };

        var tcs = new TaskCompletionSource();
        stoppingToken.Register(() => tcs.TrySetResult());
        return tcs.Task;
    }
}
