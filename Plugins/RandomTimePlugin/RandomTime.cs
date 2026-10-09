using System.Globalization;
using System.Threading.Channels;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Weather;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace RandomTimePlugin;

public class RandomTime : BackgroundService
{
    private const int SecondsPerDay = 86400;

    private readonly WeatherManager _weatherManager;
    private readonly ACServerConfiguration _serverConfiguration;
    private readonly RandomTimeConfiguration _configuration;
    private readonly List<(int Seconds, float PrefixSum)> _times = [];
    private readonly IRandomWeatherCycle? _weatherCycle;
    private readonly Channel<WeatherCycleEventArgs> _syncCycles = Channel.CreateUnbounded<WeatherCycleEventArgs>(new UnboundedChannelOptions { SingleReader = true });

    public RandomTime(
        RandomTimeConfiguration configuration,
        WeatherManager weatherManager,
        ACServerConfiguration serverConfiguration,
        IRandomWeatherCycle? weatherCycle = null)
    {
        _configuration = configuration;
        _weatherManager = weatherManager;
        _serverConfiguration = serverConfiguration;

        if (serverConfiguration.Extra.EnableRealTime)
            throw new ConfigurationException("RandomTimePlugin does not work with EnableRealTime enabled");

        if (serverConfiguration.Extra.EnablePlugins?.Contains("TimeDilationPlugin") == true)
            throw new ConfigurationException("RandomTimePlugin does not work with TimeDilationPlugin enabled");

        if (_configuration.SyncTimeToWeatherCycle && serverConfiguration.Extra.EnablePlugins?.Contains("RandomWeatherPlugin") != true)
            throw new ConfigurationException("SyncTimeToWeatherCycle requires RandomWeatherPlugin to be enabled");

        if (_configuration.Mode == RandomTimeMode.Default)
            RecalculateWeights();

        if (_configuration.SyncTimeToWeatherCycle)
        {
            _weatherCycle = weatherCycle!;
            _weatherCycle.CycleStarted += OnCycleStarted;
        }
    }

    private void RecalculateWeights()
    {
        var parsed = _configuration.TimeWeights
            .Where(tw => tw.Value > 0)
            .Select(tw => (
                Seconds: (int)DateTime.ParseExact(tw.Key, "H:mm", CultureInfo.InvariantCulture).TimeOfDay.TotalSeconds,
                Weight: tw.Value))
            .ToList();

        float weightSum = parsed.Sum(tw => tw.Weight);

        float prefixSum = 0.0f;
        foreach (var (seconds, weight) in parsed)
        {
            prefixSum += weight / weightSum;
            _times.Add((seconds, prefixSum));
        }
    }

    private int PickRandomTime()
    {
        float rng = Random.Shared.NextSingle();

        foreach (var (seconds, prefixSum) in _times)
        {
            if (rng < prefixSum) return seconds;
        }

        return _times[^1].Seconds;
    }

    private int PickNextTime()
    {
        return _configuration.Mode == RandomTimeMode.TrueRandom
            ? Random.Shared.Next(SecondsPerDay)
            : PickRandomTime();
    }

    private int GetCurrentSeconds() => (int)(_weatherManager.CurrentDateTime.TimeOfDay.TickOfDay / 10_000_000);

    private async Task RunCycleAsync(int transitionDuration, int holdDuration, float baseMultiplier, CancellationToken stoppingToken)
    {
        try
        {
            int next = PickNextTime();

            if (transitionDuration <= 0)
            {
                _weatherManager.SetTime(next);
                _serverConfiguration.Server.TimeOfDayMultiplier = baseMultiplier;

                LogTransition(next, 0, baseMultiplier, holdDuration);
                return;
            }

            int now = GetCurrentSeconds();
            int forwardDelta = (next - now + SecondsPerDay) % SecondsPerDay;
            int backwardDelta = SecondsPerDay - forwardDelta;

            if (Math.Min(forwardDelta, backwardDelta) <= _configuration.SkipTimeChangeRangeSeconds)
            {
                Log.Information("Random time picked {Time} matched current time or was within skip range, skipping transition", TimeSpan.FromSeconds(next));
                return;
            }

            bool goBackwards = _configuration.AllowBackwardsTimeTransitions && backwardDelta < forwardDelta;
            int delta = goBackwards ? -backwardDelta : forwardDelta;
            double multiplier = delta / (transitionDuration / 1000.0);

            _serverConfiguration.Server.TimeOfDayMultiplier = (float)multiplier;
            LogTransition(next, transitionDuration, multiplier, holdDuration);

            try
            {
                await Task.Delay(transitionDuration, stoppingToken);
                _weatherManager.SetTime(next);
            }
            finally
            {
                _serverConfiguration.Server.TimeOfDayMultiplier = baseMultiplier;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during random time update");
        }
    }

    private static void LogTransition(int next, int transitionDuration, double multiplier, int holdDuration)
    {
        Log.Information("Random time transitioning to {Time}, transition duration {TransitionDuration} seconds at {Multiplier}x speed, time duration {HoldDuration} minutes",
            TimeSpan.FromSeconds(next), Math.Round(transitionDuration / 1000.0), Math.Round(multiplier, 1), Math.Round(holdDuration / 60_000.0, 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        float baseMultiplier = _serverConfiguration.Server.TimeOfDayMultiplier;

        if (_configuration.SyncTimeToWeatherCycle)
        {
            await foreach (var args in _syncCycles.Reader.ReadAllAsync(stoppingToken))
            {
                await RunCycleAsync(args.TransitionDurationMs, args.WeatherDurationMs, baseMultiplier, stoppingToken);
            }

            return;
        }

        bool firstRun = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            int holdDuration = Random.Shared.Next(_configuration.MinTimeDurationMilliseconds, _configuration.MaxTimeDurationMilliseconds);
            int transitionDuration = (firstRun && _configuration.RandomizeInitialTime) ? 0 : Random.Shared.Next(_configuration.MinTransitionDurationMilliseconds, _configuration.MaxTransitionDurationMilliseconds);
            firstRun = false;

            await RunCycleAsync(transitionDuration, holdDuration, baseMultiplier, stoppingToken);
            await Task.Delay(holdDuration, stoppingToken);
        }
    }

    private void OnCycleStarted(object? sender, WeatherCycleEventArgs args)
    {
        _syncCycles.Writer.TryWrite(args);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_weatherCycle != null)
            _weatherCycle.CycleStarted -= OnCycleStarted;

        return base.StopAsync(cancellationToken);
    }
}
