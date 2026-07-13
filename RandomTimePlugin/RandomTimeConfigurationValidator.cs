using AssettoServer.Server.Configuration;
using FluentValidation;

namespace RandomTimePlugin;

public class RandomTimeConfigurationValidator : AbstractValidator<RandomTimeConfiguration>
{
    public RandomTimeConfigurationValidator()
    {
        RuleFor(cfg => cfg.MinTimeDurationMinutes).LessThanOrEqualTo(cfg => cfg.MaxTimeDurationMinutes);
        RuleFor(cfg => cfg.MinTransitionDurationSeconds).LessThanOrEqualTo(cfg => cfg.MaxTransitionDurationSeconds);
        RuleFor(cfg => cfg.SkipTimeChangeRangeMinutes).GreaterThanOrEqualTo(0);

        When(cfg => cfg.Mode == RandomTimeMode.Default, () =>
        {
            RuleFor(cfg => cfg.TimeWeights)
                .NotEmpty()
                .DependentRules(() =>
                {
                    RuleFor(cfg => cfg.TimeWeights)
                        .Must(tw => tw.Values.Any(v => v > 0))
                        .WithMessage("At least one entry in TimeWeights must have a weight greater than 0");
                    RuleForEach(cfg => cfg.TimeWeights)
                        .ChildRules(tw =>
                        {
                            tw.RuleFor(tw => tw.Key).Matches(@"^(?:[01]?[0-9]|2[0-3]):(?:[0-5][0-9])$");
                            tw.RuleFor(tw => tw.Value).GreaterThanOrEqualTo(0);
                        });
                });
        });
    }
}
