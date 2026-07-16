using NUnit.Framework;
using System;
using System.Linq;
using TftCompanion.Poc.Core.LocalSimulation;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class LocalCompanionSimulationTests
{
    [Test]
    public void frozen_fixture_pack_resolves_only_declared_educational_scenarios()
    {
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();

        Assert.Multiple(() =>
        {
            Assert.That(pack.Version, Is.EqualTo("fixture-v1"));

            bool lossStreakFound = pack.TryGetScenario("loss-streak-review-v1", out FixtureScenarioDefinition? lossStreak);
            Assert.That(lossStreakFound, Is.True);
            Assert.That(lossStreak!.Topic, Is.EqualTo(ManualTopic.LossStreakReview));
            Assert.That(lossStreak.MessageKey, Is.EqualTo("manual.loss-streak.review"));

            Assert.That(pack.TryGetScenario("unrecognized-live-composition", out _), Is.False);
        });
    }

    [Test]
    public void frozen_fixture_pack_does_not_expose_a_mutable_intent_set()
    {
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();
        Assert.That(pack.TryGetScenario("loss-streak-review-v1", out FixtureScenarioDefinition? scenario), Is.True);

        Assert.That(
            () => ((ISet<ManualIntent>)scenario!.AllowedIntents).Add(ManualIntent.ConsiderReroll),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void manual_draft_is_bounded_and_does_not_accept_free_text_or_game_identity_fields()
    {
        var propertyNames = typeof(ManualScenarioDraft)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(propertyNames.Contains("UserNote"), Is.False);
            Assert.That(propertyNames.Contains("MatchId"), Is.False);
            Assert.That(propertyNames.Contains("RoundKey"), Is.False);
            Assert.That(propertyNames.Contains("GepSequence"), Is.False);

            Assert.That(propertyNames.Contains("ManualRunId"), Is.True);
            Assert.That(propertyNames.Contains("ManualRevision"), Is.True);
        });
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid FixedRunId = new Guid("11111111-1111-1111-1111-111111111111");

    private static ManualScenarioDraft CreateBaseDraft() => new(
        ManualRunId: FixedRunId,
        ManualRevision: 1,
        FixtureScenarioId: "loss-streak-review-v1",
        Topic: ManualTopic.LossStreakReview,
        Intent: ManualIntent.PreserveLossStreak,
        HealthBand: ManualRiskBand.Medium,
        GoldBand: ManualRiskBand.Medium,
        CopiesBand: ManualCopiesBand.Unknown,
        UnitCostBand: ManualUnitCostBand.Unknown,
        Provenance: LocalFactProvenance.UserEntered);

    private static LocalDecisionSnapshot CreateBaseSnapshot() => new(
        Draft: CreateBaseDraft(),
        FixturePackVersion: "fixture-v1",
        CreatedAt: Now,
        ExpiresAt: Now.AddMinutes(2));

    [Test]
    public void identical_frozen_snapshot_and_pack_produce_identical_semantic_advice()
    {
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot();
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();
        LocalCompanionEngine engine = new LocalCompanionEngine();

        SemanticAdvice first = engine.Evaluate(snapshot, pack, Now);
        SemanticAdvice second = engine.Evaluate(snapshot, pack, Now);

        Assert.Multiple(() =>
        {
            Assert.That(first.Phase, Is.EqualTo(LocalAdvicePhase.Current));
            Assert.That(second.Phase, Is.EqualTo(LocalAdvicePhase.Current));
            Assert.That(first.SemanticDigest, Is.EqualTo(second.SemanticDigest));
            Assert.That(first.AdviceId, Is.EqualTo(second.AdviceId));
            Assert.That(first.PrimaryAction!.MessageKey, Is.EqualTo("manual.loss-streak.review"));
            Assert.That(first.SupportingActions.Count, Is.LessThanOrEqualTo(2));
        });
    }

    [Test]
    public void unknown_required_manual_fact_yields_unknown_not_current_advice()
    {
        ManualScenarioDraft draft = CreateBaseDraft() with { GoldBand = ManualRiskBand.Unknown };
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with { Draft = draft };
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();
        LocalCompanionEngine engine = new LocalCompanionEngine();

        SemanticAdvice advice = engine.Evaluate(snapshot, pack, Now);

        Assert.Multiple(() =>
        {
            Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Unknown));
            Assert.That(advice.PrimaryAction, Is.Null);
            Assert.That(advice.ReasonCode, Is.EqualTo("fixture.insufficient-facts"));
        });
    }

    [Test]
    public void pack_version_mismatch_yields_unknown_without_fallback_guess()
    {
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with { FixturePackVersion = "fixture-v0" };
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();
        LocalCompanionEngine engine = new LocalCompanionEngine();

        SemanticAdvice advice = engine.Evaluate(snapshot, pack, Now);

        Assert.Multiple(() =>
        {
            Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Unknown));
            Assert.That(advice.PrimaryAction, Is.Null);
            Assert.That(advice.ReasonCode, Is.EqualTo("fixture.version-mismatch"));
        });
    }

    [Test]
    public void fixture_topic_mismatch_yields_unknown_without_reinterpreting_manual_input()
    {
        ManualScenarioDraft draft = CreateBaseDraft() with { Topic = ManualTopic.RerollReview };
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with { Draft = draft };
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();
        LocalCompanionEngine engine = new LocalCompanionEngine();

        SemanticAdvice advice = engine.Evaluate(snapshot, pack, Now);

        Assert.Multiple(() =>
        {
            Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Unknown));
            Assert.That(advice.PrimaryAction, Is.Null);
            Assert.That(advice.ReasonCode, Is.EqualTo("fixture.pack-unavailable"));
        });
    }

    [Test]
    public void expired_snapshot_wins_over_a_fixture_pack_version_mismatch()
    {
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with
        {
            FixturePackVersion = "fixture-v0",
            CreatedAt = Now.AddMinutes(-1),
            ExpiresAt = Now
        };

        SemanticAdvice advice = new LocalCompanionEngine().Evaluate(
            snapshot,
            FrozenFixturePack.CreateV1(),
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Expired));
            Assert.That(advice.Precision, Is.EqualTo(LocalPrecisionState.Degraded));
            Assert.That(advice.PrimaryAction, Is.Null);
            Assert.That(advice.ReasonCode, Is.EqualTo("fixture.state-invalidated"));
        });
    }

    [Test]
    public void unsupported_intent_yields_unknown_without_selecting_a_fixture_candidate()
    {
        ManualScenarioDraft draft = CreateBaseDraft() with { Intent = ManualIntent.ConsiderReroll };
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with { Draft = draft };

        SemanticAdvice advice = new LocalCompanionEngine().Evaluate(
            snapshot,
            FrozenFixturePack.CreateV1(),
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Unknown));
            Assert.That(advice.PrimaryAction, Is.Null);
            Assert.That(advice.ReasonCode, Is.EqualTo("fixture.pack-unavailable"));
        });
    }

    [Test]
    public void complete_reroll_manual_facts_produce_the_reroll_fixture_candidate()
    {
        ManualScenarioDraft draft = CreateBaseDraft() with
        {
            FixtureScenarioId = "reroll-review-v1",
            Topic = ManualTopic.RerollReview,
            Intent = ManualIntent.ConsiderReroll,
            CopiesBand = ManualCopiesBand.NearThreshold,
            UnitCostBand = ManualUnitCostBand.Four
        };
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with { Draft = draft };

        SemanticAdvice advice = new LocalCompanionEngine().Evaluate(
            snapshot,
            FrozenFixturePack.CreateV1(),
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Current));
            Assert.That(advice.PrimaryAction!.MessageKey, Is.EqualTo("manual.reroll.review"));
            Assert.That(advice.ReasonCode, Is.EqualTo("fixture.educational-reroll"));
        });
    }

    [Test]
    public void missing_reroll_copy_band_yields_unknown_not_current_advice()
    {
        ManualScenarioDraft draft = CreateBaseDraft() with
        {
            FixtureScenarioId = "reroll-review-v1",
            Topic = ManualTopic.RerollReview,
            Intent = ManualIntent.ConsiderReroll,
            CopiesBand = ManualCopiesBand.Unknown,
            UnitCostBand = ManualUnitCostBand.Four
        };
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with { Draft = draft };

        SemanticAdvice advice = new LocalCompanionEngine().Evaluate(
            snapshot,
            FrozenFixturePack.CreateV1(),
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Unknown));
            Assert.That(advice.PrimaryAction, Is.Null);
            Assert.That(advice.ReasonCode, Is.EqualTo("fixture.insufficient-facts"));
        });
    }

    [Test]
    public void action_candidate_defensively_freezes_locked_slots()
    {
        Dictionary<string, string> sourceSlots = new(StringComparer.Ordinal)
        {
            ["source"] = "FixtureOnly",
            ["precision"] = "Educational",
            ["topic"] = "LossStreakReview",
            ["intent"] = "Review"
        };
        LocalActionCandidate candidate = new(
            CandidateId: "loss-streak-review-v1:primary",
            MessageKey: "manual.loss-streak.review",
            ReasonCode: "fixture.educational-loss-streak",
            Priority: 100,
            LockedSlots: sourceSlots);

        sourceSlots["intent"] = "Tampered";

        Assert.Multiple(() =>
        {
            Assert.That(candidate.LockedSlots["intent"], Is.EqualTo("Review"));
            Assert.That(
                () => ((IDictionary<string, string>)candidate.LockedSlots)["intent"] = "TamperedAgain",
                Throws.TypeOf<NotSupportedException>());
        });
    }

    [Test]
    public void different_allowed_intents_with_the_same_run_revision_and_scenario_produce_distinct_semantic_identity()
    {
        LocalDecisionSnapshot reviewSnapshot = CreateBaseSnapshot() with
        {
            Draft = CreateBaseDraft() with { Intent = ManualIntent.Review }
        };
        LocalDecisionSnapshot preserveSnapshot = CreateBaseSnapshot() with
        {
            Draft = CreateBaseDraft() with { Intent = ManualIntent.PreserveLossStreak }
        };
        LocalCompanionEngine engine = new();
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();

        SemanticAdvice review = engine.Evaluate(reviewSnapshot, pack, Now);
        SemanticAdvice preserve = engine.Evaluate(preserveSnapshot, pack, Now);

        Assert.Multiple(() =>
        {
            Assert.That(review.Phase, Is.EqualTo(LocalAdvicePhase.Current));
            Assert.That(preserve.Phase, Is.EqualTo(LocalAdvicePhase.Current));
            Assert.That(review.PrimaryAction!.MessageKey, Is.EqualTo("manual.loss-streak.review"));
            Assert.That(preserve.PrimaryAction!.MessageKey, Is.EqualTo("manual.loss-streak.review"));
            Assert.That(review.PrimaryAction.LockedSlots["intent"], Is.Not.EqualTo(preserve.PrimaryAction.LockedSlots["intent"]));
            Assert.That(review.SemanticDigest, Is.Not.EqualTo(preserve.SemanticDigest));
            Assert.That(review.AdviceId, Is.Not.EqualTo(preserve.AdviceId));
        });
    }

    [Test]
    public void invalid_bounded_manual_input_yields_unknown_without_a_fixture_candidate()
    {
        ManualScenarioDraft baseDraft = CreateBaseDraft();
        LocalDecisionSnapshot baseSnapshot = CreateBaseSnapshot();
        LocalDecisionSnapshot[] invalidSnapshots =
        [
            baseSnapshot with { Draft = baseDraft with { HealthBand = (ManualRiskBand)99 } },
            baseSnapshot with { Draft = baseDraft with { Intent = (ManualIntent)99 } },
            baseSnapshot with { Draft = baseDraft with { Provenance = (LocalFactProvenance)99 } },
            baseSnapshot with { Draft = baseDraft with { ManualRunId = Guid.Empty } },
            baseSnapshot with { Draft = baseDraft with { ManualRevision = 0 } },
            baseSnapshot with { Draft = baseDraft with { FixtureScenarioId = " " } },
            baseSnapshot with { FixturePackVersion = " " },
            baseSnapshot with { ExpiresAt = baseSnapshot.CreatedAt }
        ];

        LocalCompanionEngine engine = new();
        FrozenFixturePack pack = FrozenFixturePack.CreateV1();

        foreach (LocalDecisionSnapshot snapshot in invalidSnapshots)
        {
            SemanticAdvice advice = engine.Evaluate(snapshot, pack, Now);

            Assert.Multiple(() =>
            {
                Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Unknown));
                Assert.That(advice.Precision, Is.EqualTo(LocalPrecisionState.Unknown));
                Assert.That(advice.PrimaryAction, Is.Null);
                Assert.That(advice.ReasonCode, Is.EqualTo("fixture.invalid-manual-input"));
                Assert.That(advice.SupportingActions, Is.Empty);
                Assert.That(advice.Observation, Is.Null);
                Assert.That(advice.SkillVersion, Is.EqualTo(FixtureExpressionSkillContract.Version));
            });
        }
    }

    // --- M1-B: Coordinator lifecycle, Expression skill, Panel projection ---

    private static readonly Guid RunA = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RunB = new Guid("22222222-2222-2222-2222-222222222222");

    private static SemanticAdvice BuildCurrent(
        Guid runId,
        long revision,
        DateTimeOffset? expiresAt = null)
    {
        ManualScenarioDraft draft = CreateBaseDraft() with
        {
            ManualRunId = runId,
            ManualRevision = revision
        };
        LocalDecisionSnapshot snapshot = CreateBaseSnapshot() with
        {
            Draft = draft,
            ExpiresAt = expiresAt ?? Now.AddMinutes(2)
        };
        return new LocalCompanionEngine().Evaluate(snapshot, FrozenFixturePack.CreateV1(), Now);
    }

    [Test]
    public void coordinator_accepts_higher_revision_for_same_run_and_supersedes_lower_revision()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();

        SemanticAdvice first = BuildCurrent(RunA, revision: 1);
        Assert.That(coordinator.TryAccept(first), Is.True);
        Assert.That(coordinator.Current!.ManualRevision, Is.EqualTo(1));

        SemanticAdvice second = BuildCurrent(RunA, revision: 2);
        Assert.That(coordinator.TryAccept(second), Is.True);
        Assert.That(coordinator.Current!.ManualRevision, Is.EqualTo(2));

        Assert.That(coordinator.TryAccept(first), Is.False, "stale revision must not reactivate");
        Assert.That(coordinator.Current!.ManualRevision, Is.EqualTo(2));
    }

    [Test]
    public void coordinator_rejects_current_advice_with_an_invalid_run_identity_or_revision()
    {
        SemanticAdvice emptyRun = BuildCurrent(RunA, revision: 1) with { ManualRunId = Guid.Empty };
        SemanticAdvice nonPositiveRevision = BuildCurrent(RunA, revision: 1) with { ManualRevision = 0 };

        LocalAdviceCoordinator emptyRunCoordinator = new LocalAdviceCoordinator();
        LocalAdviceCoordinator nonPositiveRevisionCoordinator = new LocalAdviceCoordinator();

        Assert.Multiple(() =>
        {
            Assert.That(emptyRunCoordinator.TryAccept(emptyRun), Is.False);
            Assert.That(emptyRunCoordinator.Current, Is.Null);
            Assert.That(emptyRunCoordinator.LastPhase, Is.EqualTo(LocalAdvicePhase.Unknown));

            Assert.That(nonPositiveRevisionCoordinator.TryAccept(nonPositiveRevision), Is.False);
            Assert.That(nonPositiveRevisionCoordinator.Current, Is.Null);
            Assert.That(nonPositiveRevisionCoordinator.LastPhase, Is.EqualTo(LocalAdvicePhase.Unknown));
        });
    }

    [Test]
    public void coordinator_defensively_copies_supporting_actions_before_exposing_current_advice()
    {
        LocalActionCandidate additionalAction = BuildCurrent(RunB, revision: 1).PrimaryAction!;
        List<LocalActionCandidate> suppliedSupportingActions = new List<LocalActionCandidate>();
        SemanticAdvice advice = BuildCurrent(RunA, revision: 1) with
        {
            SupportingActions = suppliedSupportingActions
        };
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();

        Assert.That(coordinator.TryAccept(advice), Is.True);
        Assert.That(coordinator.Current!.SupportingActions, Is.Empty);

        suppliedSupportingActions.Add(additionalAction);
        IList<LocalActionCandidate> exposedSupportingActions =
            coordinator.Current.SupportingActions as IList<LocalActionCandidate>
            ?? throw new AssertionException("Coordinator must expose a read-only supporting-actions collection.");

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Current.SupportingActions, Is.Empty);
            Assert.That(() => exposedSupportingActions.Add(additionalAction), Throws.TypeOf<NotSupportedException>());
        });
    }

    [Test]
    public void coordinator_retires_previous_run_when_a_new_run_activates()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();

        SemanticAdvice a1 = BuildCurrent(RunA, revision: 1);
        Assert.That(coordinator.TryAccept(a1), Is.True);

        SemanticAdvice b1 = BuildCurrent(RunB, revision: 1);
        Assert.That(coordinator.TryAccept(b1), Is.True);
        Assert.That(coordinator.Current!.ManualRunId, Is.EqualTo(RunB));

        SemanticAdvice aHigher = BuildCurrent(RunA, revision: 99);
        Assert.That(coordinator.TryAccept(aHigher), Is.False, "retired run must not reactivate even with higher revision");
        Assert.That(coordinator.Current!.ManualRunId, Is.EqualTo(RunB));
    }

    [Test]
    public void coordinator_clear_requires_active_run_and_strictly_higher_revision()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();
        coordinator.TryAccept(BuildCurrent(RunA, revision: 1));

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.TryClear(RunB, revision: 2), Is.False, "non-active run cannot clear");
            Assert.That(coordinator.TryClear(RunA, revision: 1), Is.False, "clear requires strictly higher revision");
            Assert.That(coordinator.TryClear(RunA, revision: 2), Is.True);
        });

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Current, Is.Null);
            Assert.That(coordinator.LastPhase, Is.EqualTo(LocalAdvicePhase.Cleared));
        });

        Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 2)), Is.False, "old revision cannot revive after clear");
        Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 3)), Is.True, "same run higher revision can reactivate after clear");
    }

    [Test]
    public void coordinator_retires_a_cleared_run_when_a_different_run_becomes_active()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();

        Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 1)), Is.True);
        Assert.That(coordinator.TryClear(RunA, revision: 2), Is.True);
        Assert.That(coordinator.TryAccept(BuildCurrent(RunB, revision: 1)), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(
                coordinator.TryAccept(BuildCurrent(RunA, revision: 3)),
                Is.False,
                "a different active run must permanently retire the former cleared run");
            Assert.That(coordinator.Current!.ManualRunId, Is.EqualTo(RunB));
            Assert.That(coordinator.Current.ManualRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void coordinator_expire_clears_current_and_marks_expired()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();
        DateTimeOffset expiry = Now.AddMinutes(2);
        coordinator.TryAccept(BuildCurrent(RunA, revision: 1, expiresAt: expiry));

        SemanticAdvice? expired = coordinator.ExpireIfNeeded(Now);
        Assert.That(expired, Is.Null, "no expiry before ExpiresAt");
        Assert.That(coordinator.Current, Is.Not.Null);

        SemanticAdvice? expiredAfter = coordinator.ExpireIfNeeded(expiry);
        Assert.Multiple(() =>
        {
            Assert.That(expiredAfter, Is.Not.Null);
            Assert.That(expiredAfter!.Phase, Is.EqualTo(LocalAdvicePhase.Expired));
            Assert.That(coordinator.Current, Is.Null);
            Assert.That(coordinator.LastPhase, Is.EqualTo(LocalAdvicePhase.Expired));
        });

        Assert.That(coordinator.ExpireIfNeeded(expiry), Is.Null, "idempotent when already expired");
    }

    [Test]
    public void coordinator_expiry_returns_a_nonrenderable_degraded_tombstone()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();
        DateTimeOffset expiry = Now.AddMinutes(2);
        Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 1, expiresAt: expiry)), Is.True);

        SemanticAdvice expired = coordinator.ExpireIfNeeded(expiry)!;

        Assert.Multiple(() =>
        {
            Assert.That(expired.Phase, Is.EqualTo(LocalAdvicePhase.Expired));
            Assert.That(expired.Precision, Is.EqualTo(LocalPrecisionState.Degraded));
            Assert.That(expired.PrimaryAction, Is.Null);
            Assert.That(expired.SupportingActions, Is.Empty);
            Assert.That(expired.Observation, Is.Null);
            Assert.That(expired.ReasonCode, Is.EqualTo("fixture.state-invalidated"));
        });
    }

    [Test]
    public void coordinator_retires_an_expired_run_when_a_different_run_becomes_active()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();
        DateTimeOffset expiry = Now.AddMinutes(2);

        Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 1, expiresAt: expiry)), Is.True);
        Assert.That(coordinator.ExpireIfNeeded(expiry), Is.Not.Null);
        Assert.That(coordinator.TryAccept(BuildCurrent(RunB, revision: 1)), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(
                coordinator.TryAccept(BuildCurrent(RunA, revision: 2)),
                Is.False,
                "a different active run must permanently retire the former expired run");
            Assert.That(coordinator.Current!.ManualRunId, Is.EqualTo(RunB));
            Assert.That(coordinator.Current.ManualRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void restored_cleared_terminal_state_preserves_high_water_and_requires_a_newer_revision()
    {
        LocalAdviceCoordinator coordinator = new();

        Assert.That(
            coordinator.TryRestoreTerminalState(
                RunA,
                highWaterRevision: 7,
                LocalAdvicePhase.Cleared),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Current, Is.Null);
            Assert.That(coordinator.LastPhase, Is.EqualTo(LocalAdvicePhase.Cleared));
            Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 7)), Is.False);
            Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 8)), Is.True);
        });
    }

    [Test]
    public void restored_terminal_run_is_retired_after_a_different_run_becomes_current()
    {
        LocalAdviceCoordinator coordinator = new();

        Assert.That(
            coordinator.TryRestoreTerminalState(
                RunA,
                highWaterRevision: 4,
                LocalAdvicePhase.Expired),
            Is.True);

        Assert.That(coordinator.TryAccept(BuildCurrent(RunB, revision: 1)), Is.True);

        Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 5)), Is.False);
    }

    [TestCase(LocalAdvicePhase.Current)]
    [TestCase(LocalAdvicePhase.Unknown)]
    [TestCase(LocalAdvicePhase.Degraded)]
    [TestCase(LocalAdvicePhase.Superseded)]
    public void terminal_restore_rejects_nonterminal_phase(LocalAdvicePhase phase)
    {
        LocalAdviceCoordinator coordinator = new();

        Assert.That(
            coordinator.TryRestoreTerminalState(
                RunA,
                highWaterRevision: 1,
                phase),
            Is.False);
    }

    [Test]
    public void terminal_restore_does_not_displace_a_live_current_advice()
    {
        LocalAdviceCoordinator coordinator = new();
        Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 1)), Is.True);

        bool restored = coordinator.TryRestoreTerminalState(
            RunB,
            highWaterRevision: 1,
            LocalAdvicePhase.Cleared);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(coordinator.Current, Is.Not.Null);
            Assert.That(coordinator.Current!.ManualRunId, Is.EqualTo(RunA));
            Assert.That(coordinator.Current.ManualRevision, Is.EqualTo(1));
            Assert.That(coordinator.LastPhase, Is.EqualTo(LocalAdvicePhase.Current));
        });
    }

    [Test]
    public void expression_skill_renders_current_advice_with_messagekey_digest_and_skillversion()
    {
        SemanticAdvice advice = BuildCurrent(RunA, revision: 1);
        EmbeddedFixtureExpressionSkill skill = new EmbeddedFixtureExpressionSkill();

        bool ok = skill.TryRender(advice, out RenderedFixtureAdvice? rendered);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(rendered, Is.Not.Null);
            Assert.That(rendered!.MessageKey, Is.EqualTo("manual.loss-streak.review"));
            Assert.That(rendered.SemanticDigest, Is.EqualTo(advice.SemanticDigest));
            Assert.That(rendered.SkillVersion, Is.EqualTo(FixtureExpressionSkillContract.Version));
            Assert.That(rendered.ExpiresAt, Is.EqualTo(advice.ExpiresAt));
            Assert.That(string.IsNullOrWhiteSpace(rendered.Text), Is.False);
        });
    }

    [Test]
    public void expression_skill_safe_silence_on_version_mismatch_and_missing_slots()
    {
        EmbeddedFixtureExpressionSkill skill = new EmbeddedFixtureExpressionSkill();

        SemanticAdvice wrongVersion = BuildCurrent(RunA, revision: 1) with { SkillVersion = "other-v1" };
        Assert.Multiple(() =>
        {
            Assert.That(skill.TryRender(wrongVersion, out var wrongVersionRendered), Is.False);
            Assert.That(wrongVersionRendered, Is.Null);
        });

        SemanticAdvice goodAdvice = BuildCurrent(RunA, revision: 1);
        SemanticAdvice nonCurrent = goodAdvice with { Phase = LocalAdvicePhase.Unknown };
        Assert.Multiple(() =>
        {
            Assert.That(skill.TryRender(nonCurrent, out var r1), Is.False);
            Assert.That(r1, Is.Null);
        });

        SemanticAdvice noPrimary = goodAdvice with { PrimaryAction = null };
        Assert.Multiple(() =>
        {
            Assert.That(skill.TryRender(noPrimary, out var r2), Is.False);
            Assert.That(r2, Is.Null);
        });

        // Construct advice with a tampered PrimaryAction missing required slots.
        LocalActionCandidate missingSlots = new LocalActionCandidate(
            CandidateId: "loss-streak-review-v1:primary",
            MessageKey: "manual.loss-streak.review",
            ReasonCode: "fixture.educational-loss-streak",
            Priority: 100,
            LockedSlots: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "FixtureOnly",
                ["precision"] = "Educational"
                // topic + intent missing
            });
        SemanticAdvice missingSlotAdvice = goodAdvice with { PrimaryAction = missingSlots };
        Assert.Multiple(() =>
        {
            Assert.That(skill.TryRender(missingSlotAdvice, out var r3), Is.False);
            Assert.That(r3, Is.Null);
        });

        LocalActionCandidate blankSlot = new LocalActionCandidate(
            CandidateId: "loss-streak-review-v1:primary",
            MessageKey: "manual.loss-streak.review",
            ReasonCode: "fixture.educational-loss-streak",
            Priority: 100,
            LockedSlots: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "FixtureOnly",
                ["precision"] = "Educational",
                ["topic"] = "   ",
                ["intent"] = "PreserveLossStreak"
            });
        SemanticAdvice blankSlotAdvice = goodAdvice with { PrimaryAction = blankSlot };
        Assert.Multiple(() =>
        {
            Assert.That(skill.TryRender(blankSlotAdvice, out var r4), Is.False);
            Assert.That(r4, Is.Null);
        });

        LocalActionCandidate unknownMessageKey = new LocalActionCandidate(
            CandidateId: "unknown:primary",
            MessageKey: "manual.unrecognized.review",
            ReasonCode: "fixture.unrecognized",
            Priority: 100,
            LockedSlots: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "FixtureOnly",
                ["precision"] = "Educational",
                ["topic"] = "LossStreakReview",
                ["intent"] = "PreserveLossStreak"
            });
        SemanticAdvice unknownMessageKeyAdvice = goodAdvice with { PrimaryAction = unknownMessageKey };
        Assert.Multiple(() =>
        {
            Assert.That(skill.TryRender(unknownMessageKeyAdvice, out var r5), Is.False);
            Assert.That(r5, Is.Null);
        });
    }

    [Test]
    public void panel_projection_degrades_when_skill_cannot_render_current_advice()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();
        coordinator.TryAccept(BuildCurrent(RunA, revision: 1));

        EmbeddedFixtureExpressionSkill failingSkill = new EmbeddedFixtureExpressionSkill();
        // Tamper the advice on the coordinator via reflection is forbidden; instead construct a
        // standalone coordinator with an advice whose SkillVersion mismatches the skill.
        LocalAdviceCoordinator versionMismatchCoordinator = new LocalAdviceCoordinator();
        SemanticAdvice mismatched = BuildCurrent(RunA, revision: 1) with { SkillVersion = "other-v1" };
        // Coordinator only accepts Phase==Current; it does not check SkillVersion (by design).
        versionMismatchCoordinator.TryAccept(mismatched);

        ManualPanelProjectionHarness harness = new ManualPanelProjectionHarness(failingSkill);
        ManualPanelProjection projection = harness.Project(versionMismatchCoordinator, Now);

        Assert.Multiple(() =>
        {
            Assert.That(projection.Phase, Is.EqualTo(LocalAdvicePhase.Degraded));
            Assert.That(projection.Precision, Is.EqualTo(LocalPrecisionState.Degraded));
            Assert.That(projection.IsCurrent, Is.False);
            Assert.That(projection.ReasonCode, Is.EqualTo("fixture.expression-unavailable"));
            Assert.That(projection.RenderedText, Is.Null);
        });
    }

    [Test]
    public void panel_projection_reports_expired_and_cleared_without_rendered_text()
    {
        EmbeddedFixtureExpressionSkill skill = new EmbeddedFixtureExpressionSkill();
        ManualPanelProjectionHarness harness = new ManualPanelProjectionHarness(skill);

        // Expired path
        LocalAdviceCoordinator expiredCoordinator = new LocalAdviceCoordinator();
        DateTimeOffset expiry = Now.AddMinutes(2);
        expiredCoordinator.TryAccept(BuildCurrent(RunA, revision: 1, expiresAt: expiry));
        ManualPanelProjection expiredProjection = harness.Project(expiredCoordinator, expiry);

        Assert.Multiple(() =>
        {
            Assert.That(expiredProjection.IsCurrent, Is.False);
            Assert.That(expiredProjection.Phase, Is.EqualTo(LocalAdvicePhase.Expired));
            Assert.That(expiredProjection.Precision, Is.EqualTo(LocalPrecisionState.Degraded));
            Assert.That(expiredProjection.SourceLabel, Is.EqualTo("Manual / FixtureOnly"));
            Assert.That(expiredProjection.RenderedText, Is.Null);
            Assert.That(expiredProjection.MessageKey, Is.Null);
        });

        // Cleared path
        LocalAdviceCoordinator clearedCoordinator = new LocalAdviceCoordinator();
        clearedCoordinator.TryAccept(BuildCurrent(RunA, revision: 1));
        clearedCoordinator.TryClear(RunA, revision: 2);
        ManualPanelProjection clearedProjection = harness.Project(clearedCoordinator, Now);

        Assert.Multiple(() =>
        {
            Assert.That(clearedProjection.IsCurrent, Is.False);
            Assert.That(clearedProjection.Phase, Is.EqualTo(LocalAdvicePhase.Cleared));
            Assert.That(clearedProjection.Precision, Is.EqualTo(LocalPrecisionState.Degraded));
            Assert.That(clearedProjection.SourceLabel, Is.EqualTo("Manual / FixtureOnly"));
            Assert.That(clearedProjection.RenderedText, Is.Null);
        });
    }

    [Test]
    public void panel_projection_renders_current_advice_with_correct_identity_fields()
    {
        LocalAdviceCoordinator coordinator = new LocalAdviceCoordinator();
        SemanticAdvice advice = BuildCurrent(RunA, revision: 1);
        coordinator.TryAccept(advice);

        EmbeddedFixtureExpressionSkill skill = new EmbeddedFixtureExpressionSkill();
        ManualPanelProjectionHarness harness = new ManualPanelProjectionHarness(skill);
        ManualPanelProjection projection = harness.Project(coordinator, Now);

        Assert.Multiple(() =>
        {
            Assert.That(projection.IsCurrent, Is.True);
            Assert.That(projection.Phase, Is.EqualTo(LocalAdvicePhase.Current));
            Assert.That(projection.Precision, Is.EqualTo(LocalPrecisionState.Educational));
            Assert.That(projection.SourceLabel, Is.EqualTo("Manual / FixtureOnly"));
            Assert.That(projection.ManualRunId, Is.EqualTo(RunA));
            Assert.That(projection.ManualRevision, Is.EqualTo(1));
            Assert.That(projection.MessageKey, Is.EqualTo("manual.loss-streak.review"));
            Assert.That(string.IsNullOrWhiteSpace(projection.RenderedText!), Is.False);
            Assert.That(projection.ExpiresAt, Is.EqualTo(advice.ExpiresAt));
        });
    }
}
