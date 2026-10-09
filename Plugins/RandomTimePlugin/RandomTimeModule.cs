using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace RandomTimePlugin;

public class RandomTimeModule : AssettoServerModule<RandomTimeConfiguration>
{
    public override RandomTimeConfiguration ReferenceConfiguration => new()
    {
        SyncTimeToWeatherCycle = false,
        Mode = RandomTimeMode.Default,
        MinTimeDurationMinutes = 15,
        MaxTimeDurationMinutes = 60,
        MinTransitionDurationSeconds = 60,
        MaxTransitionDurationSeconds = 300,
        SkipTimeChangeRangeMinutes = 0,
        AllowBackwardsTimeTransitions = false,
        RandomizeInitialTime = false,
        TimeWeights = new()
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
        }
    };

    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<RandomTime>().AsSelf().As<IHostedService>().SingleInstance();
    }
}
