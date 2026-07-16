namespace TftCompanion.Poc.Core.LocalSimulation;

public sealed record RenderedFixtureAdvice(
    string MessageKey,
    string Text,
    string SemanticDigest,
    string SkillVersion,
    DateTimeOffset ExpiresAt);

public sealed class EmbeddedFixtureExpressionSkill
{
    public const string Version = FixtureExpressionSkillContract.Version;

    private static readonly IReadOnlyDictionary<string, string> FixtureTexts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["manual.loss-streak.review"] =
                "Manual fixture review: treat the declared loss-streak plan as an educational checklist, not live game state.",
            ["manual.reroll.review"] =
                "Manual fixture review: check the declared resource and copy bands before treating a reroll plan as viable."
        };

    public bool TryRender(SemanticAdvice advice, out RenderedFixtureAdvice? rendered)
    {
        rendered = null;

        if (advice is null)
        {
            return false;
        }

        if (advice.Phase != LocalAdvicePhase.Current)
        {
            return false;
        }

        if (!string.Equals(advice.SkillVersion, Version, StringComparison.Ordinal))
        {
            return false;
        }

        LocalActionCandidate? primary = advice.PrimaryAction;
        if (primary is null)
        {
            return false;
        }

        if (!TryGetRequiredSlot(primary, "source", out string source) ||
            !TryGetRequiredSlot(primary, "precision", out string precision) ||
            !TryGetRequiredSlot(primary, "topic", out string topic) ||
            !TryGetRequiredSlot(primary, "intent", out string intent))
        {
            return false;
        }

        if (!FixtureTexts.TryGetValue(primary.MessageKey, out string? text))
        {
            return false;
        }

        rendered = new RenderedFixtureAdvice(
            MessageKey: primary.MessageKey,
            Text: text,
            SemanticDigest: advice.SemanticDigest,
            SkillVersion: advice.SkillVersion,
            ExpiresAt: advice.ExpiresAt);
        return true;
    }

    private static bool TryGetRequiredSlot(LocalActionCandidate candidate, string key, out string value)
    {
        value = string.Empty;
        if (!candidate.LockedSlots.TryGetValue(key, out string? raw) || raw is null)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        value = raw;
        return true;
    }
}
