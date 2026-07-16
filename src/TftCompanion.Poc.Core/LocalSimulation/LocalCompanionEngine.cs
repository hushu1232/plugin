using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TftCompanion.Poc.Core.LocalSimulation;

public sealed class LocalCompanionEngine
{
    public SemanticAdvice Evaluate(
        LocalDecisionSnapshot snapshot,
        FrozenFixturePack pack,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(snapshot.Draft);

        ManualScenarioDraft draft = snapshot.Draft;

        if (!HasValidManualInput(snapshot, draft))
        {
            return BuildAdvice(
                snapshot,
                LocalAdvicePhase.Unknown,
                LocalPrecisionState.Unknown,
                primaryAction: null,
                supportingActions: Array.Empty<LocalActionCandidate>(),
                observation: null,
                reasonCode: "fixture.invalid-manual-input",
                messageKey: "");
        }

        if (now >= snapshot.ExpiresAt)
        {
            return BuildAdvice(
                snapshot,
                LocalAdvicePhase.Expired,
                LocalPrecisionState.Degraded,
                primaryAction: null,
                supportingActions: Array.Empty<LocalActionCandidate>(),
                observation: null,
                reasonCode: "fixture.state-invalidated",
                messageKey: "");
        }

        if (!string.Equals(snapshot.FixturePackVersion, pack.Version, StringComparison.Ordinal))
        {
            return BuildAdvice(
                snapshot,
                LocalAdvicePhase.Unknown,
                LocalPrecisionState.Unknown,
                primaryAction: null,
                supportingActions: Array.Empty<LocalActionCandidate>(),
                observation: null,
                reasonCode: "fixture.version-mismatch",
                messageKey: "");
        }

        if (!pack.TryGetScenario(draft.FixtureScenarioId, out FixtureScenarioDefinition? scenario) ||
            scenario is null ||
            scenario.Topic != draft.Topic ||
            !scenario.AllowedIntents.Contains(draft.Intent))
        {
            return BuildAdvice(
                snapshot,
                LocalAdvicePhase.Unknown,
                LocalPrecisionState.Unknown,
                primaryAction: null,
                supportingActions: Array.Empty<LocalActionCandidate>(),
                observation: null,
                reasonCode: "fixture.pack-unavailable",
                messageKey: "");
        }

        if (!HasRequiredFacts(draft))
        {
            return BuildAdvice(
                snapshot,
                LocalAdvicePhase.Unknown,
                LocalPrecisionState.Unknown,
                primaryAction: null,
                supportingActions: Array.Empty<LocalActionCandidate>(),
                observation: null,
                reasonCode: "fixture.insufficient-facts",
                messageKey: "");
        }

        LocalActionCandidate primaryAction = new LocalActionCandidate(
            CandidateId: scenario.ScenarioId + ":primary",
            MessageKey: scenario.MessageKey,
            ReasonCode: scenario.ReasonCode,
            Priority: 100,
            LockedSlots: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "FixtureOnly",
                ["precision"] = "Educational",
                ["topic"] = draft.Topic.ToString(),
                ["intent"] = draft.Intent.ToString()
            });

        return BuildAdvice(
            snapshot,
            LocalAdvicePhase.Current,
            LocalPrecisionState.Educational,
            primaryAction: primaryAction,
            supportingActions: Array.Empty<LocalActionCandidate>(),
            observation: null,
            reasonCode: scenario.ReasonCode,
            messageKey: scenario.MessageKey);
    }

    private static bool HasRequiredFacts(ManualScenarioDraft draft)
    {
        if (draft.Topic == ManualTopic.LossStreakReview)
        {
            return draft.HealthBand != ManualRiskBand.Unknown &&
                   draft.GoldBand != ManualRiskBand.Unknown;
        }

        if (draft.Topic == ManualTopic.RerollReview)
        {
            return draft.GoldBand != ManualRiskBand.Unknown &&
                   draft.CopiesBand != ManualCopiesBand.Unknown &&
                   draft.UnitCostBand != ManualUnitCostBand.Unknown;
        }

        return false;
    }

    private static bool HasValidManualInput(LocalDecisionSnapshot snapshot, ManualScenarioDraft draft)
    {
        return draft.ManualRunId != Guid.Empty &&
               draft.ManualRevision > 0 &&
               !string.IsNullOrWhiteSpace(draft.FixtureScenarioId) &&
               !string.IsNullOrWhiteSpace(snapshot.FixturePackVersion) &&
               snapshot.ExpiresAt > snapshot.CreatedAt &&
               Enum.IsDefined(draft.Topic) &&
               Enum.IsDefined(draft.Intent) &&
               Enum.IsDefined(draft.HealthBand) &&
               Enum.IsDefined(draft.GoldBand) &&
               Enum.IsDefined(draft.CopiesBand) &&
               Enum.IsDefined(draft.UnitCostBand) &&
               Enum.IsDefined(draft.Provenance);
    }

    private static SemanticAdvice BuildAdvice(
        LocalDecisionSnapshot snapshot,
        LocalAdvicePhase phase,
        LocalPrecisionState precision,
        LocalActionCandidate? primaryAction,
        IReadOnlyList<LocalActionCandidate> supportingActions,
        LocalActionCandidate? observation,
        string reasonCode,
        string messageKey)
    {
        ManualScenarioDraft draft = snapshot.Draft;
        string digest = ComputeDigest(
            draft,
            snapshot.FixturePackVersion,
            phase,
            reasonCode,
            messageKey,
            primaryAction?.LockedSlots);
        Guid adviceId = ComputeAdviceId(digest);

        return new SemanticAdvice(
            AdviceId: adviceId,
            ManualRunId: draft.ManualRunId,
            ManualRevision: draft.ManualRevision,
            Phase: phase,
            Precision: precision,
            FixturePackVersion: snapshot.FixturePackVersion,
            PrimaryAction: primaryAction,
            SupportingActions: supportingActions,
            Observation: observation,
            ReasonCode: reasonCode,
            SemanticDigest: digest,
            SkillVersion: FixtureExpressionSkillContract.Version,
            ExpiresAt: snapshot.ExpiresAt);
    }

    private static string ComputeDigest(
        ManualScenarioDraft draft,
        string fixturePackVersion,
        LocalAdvicePhase phase,
        string reasonCode,
        string messageKey,
        IReadOnlyDictionary<string, string>? lockedSlots)
    {
        StringBuilder sb = new StringBuilder();
        AppendCanonicalField(sb, "manualRunId", draft.ManualRunId.ToString("N"));
        AppendCanonicalField(sb, "manualRevision", draft.ManualRevision.ToString(CultureInfo.InvariantCulture));
        AppendCanonicalField(sb, "fixturePackVersion", fixturePackVersion);
        AppendCanonicalField(sb, "fixtureScenarioId", draft.FixtureScenarioId);
        AppendCanonicalField(sb, "phase", phase.ToString());
        AppendCanonicalField(sb, "reasonCode", reasonCode);
        AppendCanonicalField(sb, "messageKey", messageKey);
        AppendCanonicalField(sb, "topic", draft.Topic.ToString());
        AppendCanonicalField(sb, "intent", draft.Intent.ToString());
        AppendCanonicalField(sb, "provenance", draft.Provenance.ToString());
        AppendCanonicalField(sb, "healthBand", draft.HealthBand.ToString());
        AppendCanonicalField(sb, "goldBand", draft.GoldBand.ToString());
        AppendCanonicalField(sb, "copiesBand", draft.CopiesBand.ToString());
        AppendCanonicalField(sb, "unitCostBand", draft.UnitCostBand.ToString());

        if (lockedSlots is not null)
        {
            foreach (KeyValuePair<string, string> slot in lockedSlots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                AppendCanonicalField(sb, "slot:" + slot.Key, slot.Value);
            }
        }

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static void AppendCanonicalField(StringBuilder builder, string key, string? value)
    {
        string normalizedValue = value ?? string.Empty;
        builder.Append(key)
            .Append(':')
            .Append(normalizedValue.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(normalizedValue)
            .Append('\n');
    }

    private static Guid ComputeAdviceId(string hexDigest)
    {
        byte[] bytes = Convert.FromHexString(hexDigest);
        byte[] guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }
}
