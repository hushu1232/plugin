using System.Collections.Frozen;

namespace TftCompanion.Poc.Core.LocalSimulation;

public sealed record FixtureScenarioDefinition(
    string ScenarioId,
    ManualTopic Topic,
    IReadOnlySet<ManualIntent> AllowedIntents,
    string MessageKey,
    string ReasonCode);

public sealed class FrozenFixturePack
{
    public string Version { get; }

    private readonly IReadOnlyDictionary<string, FixtureScenarioDefinition> _scenarios;

    private FrozenFixturePack(string version, IReadOnlyDictionary<string, FixtureScenarioDefinition> scenarios)
    {
        Version = version;
        _scenarios = scenarios;
    }

    public bool TryGetScenario(string scenarioId, out FixtureScenarioDefinition? scenario)
    {
        return _scenarios.TryGetValue(scenarioId, out scenario);
    }

    public static FrozenFixturePack CreateV1()
    {
        var scenarios = new Dictionary<string, FixtureScenarioDefinition>(StringComparer.Ordinal)
        {
            ["loss-streak-review-v1"] = new FixtureScenarioDefinition(
                ScenarioId: "loss-streak-review-v1",
                Topic: ManualTopic.LossStreakReview,
                AllowedIntents: new[]
                {
                    ManualIntent.Review,
                    ManualIntent.PreserveLossStreak,
                    ManualIntent.PrepareToStabilize
                }.ToFrozenSet(),
                MessageKey: "manual.loss-streak.review",
                ReasonCode: "fixture.educational-loss-streak"),
            ["reroll-review-v1"] = new FixtureScenarioDefinition(
                ScenarioId: "reroll-review-v1",
                Topic: ManualTopic.RerollReview,
                AllowedIntents: new[]
                {
                    ManualIntent.Review,
                    ManualIntent.ConsiderReroll
                }.ToFrozenSet(),
                MessageKey: "manual.reroll.review",
                ReasonCode: "fixture.educational-reroll")
        };

        return new FrozenFixturePack("fixture-v1", scenarios);
    }
}
