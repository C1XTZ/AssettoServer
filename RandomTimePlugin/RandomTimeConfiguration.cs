using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace RandomTimePlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class RandomTimeConfiguration : IValidateConfiguration<RandomTimeConfigurationValidator>
{
    [YamlMember(Description = "If true, RandomTimePlugin synchronizes its cycles with RandomWeatherPlugin, using RandomWeatherPlugin's hold/transition durations instead of the settings below.\nIf false, both plugins generate hold/transition durations independently, even if their Min/Max ranges are identical.\nRequires RandomWeatherPlugin to be enabled.")]
    public bool SyncTimeToWeatherCycle { get; set; } = false;

    [YamlMember(Description = "Which mode should be used for time randomization \nAvailable values: 'Default' and 'TrueRandom'\nDefault picks from the weighted TimeWeights table, TrueRandom picks a completely random time")]
    public RandomTimeMode Mode { get; set; } = RandomTimeMode.Default;

    [YamlMember(Description = "Minimum duration until next time change\nIgnored if SyncTimeToWeatherCycle is true")]
    public int MinTimeDurationMinutes { get; set; } = 15;

    [YamlMember(Description = "Maximum duration until next time change\nIgnored if SyncTimeToWeatherCycle is true")]
    public int MaxTimeDurationMinutes { get; set; } = 60;

    [YamlMember(Description = "Minimum time transition duration\nIgnored if SyncTimeToWeatherCycle is true")]
    public int MinTransitionDurationSeconds { get; set; } = 60;

    [YamlMember(Description = "Maximum time transition duration\nIgnored if SyncTimeToWeatherCycle is true")]
    public int MaxTransitionDurationSeconds { get; set; } = 300;

    [YamlMember(Description = "If the next time is within this many minutes of the comparison time, time keeps running unchanged instead of transitioning\nSet to 0 to compare against the last time this plugin set, only discarding on an exact match\nSet above 0 to instead compare against the actual ingame time with a +/- minute tolerance - Recommended when using RandomTimeMode: 'TrueRandom' or time multiplier above 0")]
    public int SkipTimeChangeRangeMinutes { get; set; } = 0;

    [YamlMember(Description = "If true, transitions will move time backwards when it is a shorter path than going forwards - e.g. 18:00 -> 17:00 goes backwards 1 hour instead of wrapping forwards ~23 hours")]
    public bool AllowBackwardsTimeTransitions { get; set; } = false;

    [YamlMember(Description = "If true, the server will start and apply the first random time instantly, skipping the initial transition from the time configured in server_cfg.ini\nIgnored if SyncTimeToWeatherCycle is true")]
    public bool RandomizeInitialTime { get; set; } = false;

    [YamlMember(Description = "Weights for random time selection (format HH:mm, 24h), removing a weight or setting it to 0 blacklists a time\nYou can add additional times and use decimals like 0.1")]
    public Dictionary<string, float> TimeWeights { get; init; } = new()
    {
        { "00:00", 1.0f },
        { "01:00", 1.0f },
        { "02:00", 1.0f },
        { "03:00", 1.0f },
        { "04:00", 1.0f },
        { "05:00", 1.0f },
        { "06:00", 1.0f },
        { "07:00", 1.0f },
        { "08:00", 1.0f },
        { "09:00", 1.0f },
        { "10:00", 1.0f },
        { "11:00", 1.0f },
        { "12:00", 1.0f },
        { "13:00", 1.0f },
        { "14:00", 1.0f },
        { "15:00", 1.0f },
        { "16:00", 1.0f },
        { "17:00", 1.0f },
        { "18:00", 1.0f },
        { "19:00", 1.0f },
        { "20:00", 1.0f },
        { "21:00", 1.0f },
        { "22:00", 1.0f },
        { "23:00", 1.0f },
    };

    [YamlIgnore] public int MinTimeDurationMilliseconds => MinTimeDurationMinutes * 60_000;
    [YamlIgnore] public int MaxTimeDurationMilliseconds => MaxTimeDurationMinutes * 60_000;
    [YamlIgnore] public int MinTransitionDurationMilliseconds => MinTransitionDurationSeconds * 1_000;
    [YamlIgnore] public int MaxTransitionDurationMilliseconds => MaxTransitionDurationSeconds * 1_000;
    [YamlIgnore] public int SkipTimeChangeRangeSeconds => SkipTimeChangeRangeMinutes * 60;
}

public enum RandomTimeMode
{
    Default = 0,
    TrueRandom = 1
}
