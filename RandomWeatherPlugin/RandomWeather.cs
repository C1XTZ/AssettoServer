using AssettoServer.Server.Weather;
using AssettoServer.Shared.Weather;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace RandomWeatherPlugin;

public class RandomWeather : BackgroundService, IRandomWeatherCycle
{
    public event EventHandler<WeatherCycleEventArgs>? CycleStarted;

    private struct WeatherWeight
    {
        internal WeatherFxType Weather { get; init; }
        internal float PrefixSum { get; init; }
    }

    private readonly WeatherManager _weatherManager;
    private readonly IWeatherTypeProvider _weatherTypeProvider;
    private readonly RandomWeatherConfiguration _configuration;
    private readonly List<WeatherWeight> _weathers = [];

    public RandomWeather(RandomWeatherConfiguration configuration, WeatherManager weatherManager, IWeatherTypeProvider weatherTypeProvider)
    {
        _configuration = configuration;
        _weatherManager = weatherManager;
        _weatherTypeProvider = weatherTypeProvider;

        if (_configuration.Mode == RandomWeatherMode.TransitionTable)
        {
            var next = _weatherTypeProvider.GetWeatherType(_configuration.WeatherTransitions.First().Key);
            var last = _weatherManager.CurrentWeather;
            _weatherManager.SetWeather(new WeatherData(last.Type, next)
            {
                TransitionDuration = 1000,
                TemperatureAmbient = last.TemperatureAmbient,
                TemperatureRoad = (float)WeatherUtils.GetRoadTemperature(_weatherManager.CurrentDateTime.TimeOfDay.TickOfDay / 10_000_000.0, last.TemperatureAmbient,
                    next.TemperatureCoefficient),
                Pressure = last.Pressure,
                Humidity = next.Humidity,
                WindSpeed = last.WindSpeed,
                WindDirection = last.WindDirection,
                RainIntensity = last.RainIntensity,
                RainWetness = last.RainWetness,
                RainWater = last.RainWater,
                TrackGrip = last.TrackGrip
            });

            RecalculateWeights(_configuration.WeatherTransitions[next.WeatherFxType]);
            WarnAboutUnreachableWeathers(_configuration.WeatherTransitions, next.WeatherFxType);
        }
        else if (_configuration.Mode == RandomWeatherMode.Default)
        {
            RecalculateWeights(_configuration.WeatherWeights);
        }
    }

    private void RecalculateWeights(Dictionary<WeatherFxType, float> input)
    {
        _weathers.Clear();

        float weightSum = input
            .Select(wt => wt.Value)
            .Sum();

        float prefixSum = 0.0f;
        foreach (var (weather, weight) in input)
        {
            if (weight > 0 && weather != WeatherFxType.None)
            {
                prefixSum += weight / weightSum;
                _weathers.Add(new WeatherWeight
                {
                    Weather = weather,
                    PrefixSum = prefixSum,
                });
            }
        }

        _weathers.Sort((a, b) =>
        {
            if (a.PrefixSum < b.PrefixSum)
                return -1;
            if (a.PrefixSum > b.PrefixSum)
                return 1;
            return 0;
        });
    }

    private WeatherFxType PickRandom()
    {
        float rng = Random.Shared.NextSingle();
        WeatherFxType weather = WeatherFxType.None;

        int begin = 0, end = _weathers.Count - 1;
        while (begin <= end)
        {
            int i = (begin + end) / 2;

            if (_weathers[i].PrefixSum <= rng)
            {
                begin = i + 1;
            }
            else
            {
                end = i - 1;
                weather = _weathers[i].Weather;
            }
        }

        return weather;
    }

    private static void WarnAboutUnreachableWeathers(Dictionary<WeatherFxType, Dictionary<WeatherFxType, float>> transitions, WeatherFxType start)
    {
        var visited = new HashSet<WeatherFxType> { start };
        var queue = new Queue<WeatherFxType>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!transitions.TryGetValue(current, out var destinations)) continue;

            foreach (var (weather, weight) in destinations)
            {
                if (weight > 0 && visited.Add(weather))
                {
                    queue.Enqueue(weather);
                }
            }
        }

        var unreachable = transitions.Keys.Where(wt => !visited.Contains(wt)).ToList();
        if (unreachable.Count > 0)
        {
            Log.Warning("The following weathers in WeatherTransitions are configured but can never be reached starting from {Start} and will never occur: {Unreachable}",
                start, string.Join(", ", unreachable));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int weatherDuration = 1000;
        int transitionDuration = 1000;
        bool firstRun = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                weatherDuration = Random.Shared.Next(_configuration.MinWeatherDurationMilliseconds, _configuration.MaxWeatherDurationMilliseconds);
                transitionDuration = (firstRun && _configuration.RandomizeInitialWeather) ? 0 : Random.Shared.Next(_configuration.MinTransitionDurationMilliseconds, _configuration.MaxTransitionDurationMilliseconds);
                firstRun = false;

                var next = PickRandom();
                var nextWeatherType = _weatherTypeProvider.GetWeatherType(next);

                var last = _weatherManager.CurrentWeather;

                Log.Information("Random weather transitioning to {WeatherType}, transition duration {TransitionDuration} seconds, weather duration {WeatherDuration} minutes",
                    nextWeatherType.WeatherFxType,
                    Math.Round(transitionDuration / 1000.0f),
                    Math.Round(weatherDuration / 60_000.0f, 1));

                _weatherManager.SetWeather(new WeatherData(last.Type, nextWeatherType)
                {
                    TransitionDuration = transitionDuration,
                    TemperatureAmbient = last.TemperatureAmbient,
                    TemperatureRoad = (float)WeatherUtils.GetRoadTemperature(_weatherManager.CurrentDateTime.TimeOfDay.TickOfDay / 10_000_000.0, last.TemperatureAmbient,
                        nextWeatherType.TemperatureCoefficient),
                    Pressure = last.Pressure,
                    Humidity = nextWeatherType.Humidity,
                    WindSpeed = last.WindSpeed,
                    WindDirection = last.WindDirection,
                    RainIntensity = last.RainIntensity,
                    RainWetness = last.RainWetness,
                    RainWater = last.RainWater,
                    TrackGrip = last.TrackGrip
                });

                if (_configuration.Mode == RandomWeatherMode.TransitionTable)
                    RecalculateWeights(_configuration.WeatherTransitions[next]);

                CycleStarted?.Invoke(this, new WeatherCycleEventArgs
                {
                    TransitionDurationMs = transitionDuration,
                    WeatherDurationMs = weatherDuration
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during random weather update");
            }
            finally
            {
                await Task.Delay(transitionDuration + weatherDuration, stoppingToken);
            }
        }
    }
}
