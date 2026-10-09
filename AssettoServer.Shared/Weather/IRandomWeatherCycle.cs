namespace AssettoServer.Shared.Weather;

public class WeatherCycleEventArgs : EventArgs
{
    public int TransitionDurationMs { get; init; }
    public int WeatherDurationMs { get; init; }
}

public interface IRandomWeatherCycle
{
    /// <summary>
    /// Fired at the start of every weather cycle.
    /// Broadcasts TransitionDurationMs and WeatherDurationMs durations.
    /// </summary>
    event EventHandler<WeatherCycleEventArgs> CycleStarted;
}
