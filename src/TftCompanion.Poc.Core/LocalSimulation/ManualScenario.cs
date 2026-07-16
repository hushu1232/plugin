namespace TftCompanion.Poc.Core.LocalSimulation;

public enum ManualTopic { LossStreakReview, RerollReview }
public enum ManualIntent { Review, PreserveLossStreak, PrepareToStabilize, ConsiderReroll }
public enum ManualRiskBand { Unknown, Low, Medium, High }
public enum ManualCopiesBand { Unknown, Few, NearThreshold, Complete }
public enum ManualUnitCostBand { Unknown, OneToThree, Four, Five }
public enum LocalFactProvenance { UserEntered, Fixture }
public enum LocalPrecisionState { ManualDirectional, Educational, Unknown, Degraded }
public enum LocalAdvicePhase { Current, Unknown, Degraded, Expired, Cleared, Superseded }

public sealed record ManualScenarioDraft(
    Guid ManualRunId,
    long ManualRevision,
    string FixtureScenarioId,
    ManualTopic Topic,
    ManualIntent Intent,
    ManualRiskBand HealthBand,
    ManualRiskBand GoldBand,
    ManualCopiesBand CopiesBand,
    ManualUnitCostBand UnitCostBand,
    LocalFactProvenance Provenance);
