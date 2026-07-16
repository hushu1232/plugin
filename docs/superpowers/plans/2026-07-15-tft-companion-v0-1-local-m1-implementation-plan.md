# TFT Companion v0.1-local M1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build a deterministic, in-memory FixtureOnly local companion kernel that turns bounded manual scenarios into safely degradable semantic advice and a test-only panel projection, without game, Overwolf, network, file, UI, or automation integration.

**Architecture:** Add a self-contained LocalSimulation domain to the existing Core project. A typed manual draft is frozen into a snapshot, checked against a small in-memory fixture pack, evaluated deterministically, accepted by a monotonic lifecycle coordinator, rendered only by a versioned embedded expression skill, and exposed through an in-memory projection harness. The existing Host, Overwolf runtime, renderer, storage runtime, and Alife code remain untouched.

**Tech Stack:** .NET 9, C# records/enums, BCL SHA-256, NUnit 4; no new NuGet packages or projects.

**Version boundary:** This plan implements v0.1-local/M1, not v0.1.0. It does not change TargetMachineGate: Pending / BlockedExternal and must not be presented as actual Overwolf, TFT GEP, TFT gameplay, D-drive target-machine, or Alife Panel validation.

**VCS rule:** Do not commit, push, tag, reset, clean, restore, stash, or alter unrelated dirty/untracked work. The user has not authorized VCS mutations.

---

## Locked scope and forbidden dependencies

Only create or modify files below D:\TFTCompanion\src\TftCompanion.Poc.Core\LocalSimulation\ and D:\TFTCompanion\tests\TftCompanion.Poc.Tests\, plus the existing project-boundary test. Do not change:

~~~text
D:\Alife
src\TftCompanion.Poc.Host\
overwolf\
manifest files
storage/loopback runtime files
solution/project files
~~~

The new runtime domain must not reference or use:

~~~text
Overwolf, GEP, League Client, Host, WebSocket, HTTP, loopback, networking,
screenshots, OCR, video/window capture, process access, memory access,
injection, packet capture, game/window/focus control, mouse/keyboard/ADB/hotkeys,
SQLite, file/database/cache writes, watcher services, DataAgent, LLM/RAG,
TTS, external APIs, downloads, real TFT data, or player free text.
~~~

No task starts a Host, opens a socket, writes D:\AlifeData\TFTCompanion, or creates a Console/WPF/WinUI project. Console second-screen work belongs to M2 and is out of scope.

## File map

| File | Responsibility |
|---|---|
| Create src/TftCompanion.Poc.Core/LocalSimulation/ManualScenario.cs | Bounded manual-input enums and immutable draft; no free text or game identity fields. |
| Create src/TftCompanion.Poc.Core/LocalSimulation/FrozenFixturePack.cs | In-memory, versioned, immutable local fixture definitions. |
| Create src/TftCompanion.Poc.Core/LocalSimulation/LocalDecisionSnapshot.cs | Immutable snapshot and decision/advice contracts. |
| Create src/TftCompanion.Poc.Core/LocalSimulation/LocalCompanionEngine.cs | Capability gate, deterministic candidate selection, semantic digest generation. |
| Create src/TftCompanion.Poc.Core/LocalSimulation/LocalAdviceCoordinator.cs | Single-writer current/superseded/cleared/expired lifecycle. |
| Create src/TftCompanion.Poc.Core/LocalSimulation/EmbeddedFixtureExpressionSkill.cs | The only location containing player-facing fixture text. |
| Create src/TftCompanion.Poc.Core/LocalSimulation/ManualPanelProjectionHarness.cs | In-memory current/unknown/degraded/expired projection DTO and builder. |
| Create tests/TftCompanion.Poc.Tests/LocalCompanionSimulationTests.cs | M1 deterministic, degradation, lifecycle, skill, and projection behavior tests. |
| Modify tests/TftCompanion.Poc.Tests/ProjectBoundaryTests.cs | Assert the local domain remains in Core and does not introduce Host/Alife/AspNetCore dependencies. |

## Exact public contract

All types live in TftCompanion.Poc.Core.LocalSimulation.

~~~csharp
public enum ManualTopic { LossStreakReview, RerollReview }
public enum ManualIntent { Review, PreserveLossStreak, PrepareToStabilize, ConsiderReroll }
public enum ManualRiskBand { Unknown, Low, Medium, High }
public enum ManualCopiesBand { Unknown, Few, NearThreshold, Complete }
public enum ManualUnitCostBand { Unknown, OneToThree, Four, Five }
public enum LocalFactProvenance { UserEntered, Fixture }
public enum LocalPrecisionState { ManualDirectional, Educational, Unknown, Degraded }
public enum LocalAdvicePhase { Current, Unknown, Degraded, Expired, Cleared, Superseded }

public static class FixtureExpressionSkillContract
{
    public const string Version = "render-tft-companion-advice-fixture-v1";
}

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

public sealed record LocalDecisionSnapshot(
    ManualScenarioDraft Draft,
    string FixturePackVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record LocalActionCandidate
{
    public LocalActionCandidate(
        string CandidateId,
        string MessageKey,
        string ReasonCode,
        int Priority,
        IReadOnlyDictionary<string, string> LockedSlots);

    public string CandidateId { get; }
    public string MessageKey { get; }
    public string ReasonCode { get; }
    public int Priority { get; }
    public IReadOnlyDictionary<string, string> LockedSlots { get; } // defensive FrozenDictionary copy
}

public sealed record SemanticAdvice(
    Guid AdviceId,
    Guid ManualRunId,
    long ManualRevision,
    LocalAdvicePhase Phase,
    LocalPrecisionState Precision,
    string FixturePackVersion,
    LocalActionCandidate? PrimaryAction,
    IReadOnlyList<LocalActionCandidate> SupportingActions,
    LocalActionCandidate? Observation,
    string ReasonCode,
    string SemanticDigest,
    string SkillVersion,
    DateTimeOffset ExpiresAt);
~~~

The engine must never use a wall clock implicitly. Every time value is supplied by the caller, so the same snapshot and pack produce the same advice/digest.

The only M1 fixture scenarios are:

~~~text
loss-streak-review-v1  -> ManualTopic.LossStreakReview -> manual.loss-streak.review
reroll-review-v1       -> ManualTopic.RerollReview     -> manual.reroll.review
~~~

They are educational fixtures, not current composition data, gameplay commands, or patch recommendations.

### Task 1: Add typed manual input and a frozen fixture pack

**Files:**
- Create: src/TftCompanion.Poc.Core/LocalSimulation/ManualScenario.cs
- Create: src/TftCompanion.Poc.Core/LocalSimulation/FrozenFixturePack.cs
- Create: tests/TftCompanion.Poc.Tests/LocalCompanionSimulationTests.cs

- [ ] **Step 1: Write the failing fixture-contract tests**

Create the test file with this first fixture and tests. Do not add a Host fixture or a disk fixture.

~~~csharp
using NUnit.Framework;
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
            Assert.That(pack.TryGetScenario("loss-streak-review-v1", out FixtureScenarioDefinition? lossStreak), Is.True);
            Assert.That(lossStreak!.Topic, Is.EqualTo(ManualTopic.LossStreakReview));
            Assert.That(lossStreak.MessageKey, Is.EqualTo("manual.loss-streak.review"));
            Assert.That(pack.TryGetScenario("unrecognized-live-composition", out _), Is.False);
        });
    }

    [Test]
    public void manual_draft_is_bounded_and_does_not_accept_free_text_or_game_identity_fields()
    {
        Type draftType = typeof(ManualScenarioDraft);
        string[] names = draftType.GetProperties().Select(property => property.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Not.Contain("UserNote"));
            Assert.That(names, Does.Not.Contain("MatchId"));
            Assert.That(names, Does.Not.Contain("RoundKey"));
            Assert.That(names, Does.Not.Contain("GepSequence"));
            Assert.That(names, Does.Contain("ManualRunId"));
            Assert.That(names, Does.Contain("ManualRevision"));
        });
    }
}
~~~

- [ ] **Step 2: Run test to verify it fails**

Run:

~~~powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test D:\TFTCompanion\tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~LocalCompanionSimulationTests"
~~~

Expected: compilation fails because TftCompanion.Poc.Core.LocalSimulation and its types do not exist. Record the actual error; do not claim a behavioral Red if compilation is the only observed Red.

- [ ] **Step 3: Write minimal input and fixture implementation**

Create ManualScenario.cs with the seven enums and ManualScenarioDraft record in the exact public contract. Do not add string note/comment fields, external identifiers, file paths, timestamps, or validation that reads an external source.

Create FrozenFixturePack.cs with this implementation:

~~~csharp
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
    private readonly IReadOnlyDictionary<string, FixtureScenarioDefinition> scenarios;

    private FrozenFixturePack(string version, IReadOnlyDictionary<string, FixtureScenarioDefinition> scenarios)
    {
        Version = version;
        this.scenarios = scenarios;
    }

    public string Version { get; }

    public bool TryGetScenario(string scenarioId, out FixtureScenarioDefinition? scenario)
    {
        bool found = scenarios.TryGetValue(scenarioId, out FixtureScenarioDefinition? value);
        scenario = value;
        return found;
    }

    public static FrozenFixturePack CreateV1()
    {
        Dictionary<string, FixtureScenarioDefinition> scenarios = new(StringComparer.Ordinal)
        {
            ["loss-streak-review-v1"] = new(
                "loss-streak-review-v1",
                ManualTopic.LossStreakReview,
                new[] { ManualIntent.Review, ManualIntent.PreserveLossStreak, ManualIntent.PrepareToStabilize }.ToFrozenSet(),
                "manual.loss-streak.review",
                "fixture.educational-loss-streak"),
            ["reroll-review-v1"] = new(
                "reroll-review-v1",
                ManualTopic.RerollReview,
                new[] { ManualIntent.Review, ManualIntent.ConsiderReroll }.ToFrozenSet(),
                "manual.reroll.review",
                "fixture.educational-reroll")
        };

        return new FrozenFixturePack("fixture-v1", scenarios);
    }
}
~~~

Do not expose the backing dictionary for mutation and do not add a JSON/XML/file loader.

- [ ] **Step 4: Run test to verify it passes**

Run the Step 2 command again.

Expected: the two new tests pass, while no Host, Overwolf, storage, or network process is started.

### Task 2: Add immutable snapshots, capability gating, and deterministic semantic advice

**Files:**
- Create: src/TftCompanion.Poc.Core/LocalSimulation/LocalDecisionSnapshot.cs
- Create: src/TftCompanion.Poc.Core/LocalSimulation/LocalCompanionEngine.cs
- Modify: tests/TftCompanion.Poc.Tests/LocalCompanionSimulationTests.cs

- [ ] **Step 1: Write failing determinism and degradation tests**

Append these tests, with the private CreateLossStreakDraft helper in the same test fixture. The helper must use explicit Guid and DateTimeOffset values, never Guid.NewGuid() or UtcNow.

~~~csharp
[Test]
public void identical_frozen_snapshot_and_pack_produce_identical_semantic_advice()
{
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    ManualScenarioDraft draft = CreateLossStreakDraft(revision: 1);
    LocalDecisionSnapshot snapshot = new(draft, pack.Version, now, now.AddMinutes(2));

    LocalCompanionEngine engine = new();
    SemanticAdvice first = engine.Evaluate(snapshot, pack, now);
    SemanticAdvice second = engine.Evaluate(snapshot, pack, now);

    Assert.Multiple(() =>
    {
        Assert.That(first.Phase, Is.EqualTo(LocalAdvicePhase.Current));
        Assert.That(first.SemanticDigest, Is.EqualTo(second.SemanticDigest));
        Assert.That(first.PrimaryAction!.MessageKey, Is.EqualTo("manual.loss-streak.review"));
        Assert.That(first.SupportingActions, Has.Count.LessThanOrEqualTo(2));
    });
}

[Test]
public void unknown_required_manual_fact_yields_unknown_not_current_advice()
{
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    ManualScenarioDraft draft = CreateLossStreakDraft(revision: 1) with { GoldBand = ManualRiskBand.Unknown };
    LocalDecisionSnapshot snapshot = new(draft, pack.Version, now, now.AddMinutes(2));

    SemanticAdvice advice = new LocalCompanionEngine().Evaluate(snapshot, pack, now);

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
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    LocalDecisionSnapshot snapshot = new(CreateLossStreakDraft(1), "fixture-v0", now, now.AddMinutes(2));

    SemanticAdvice advice = new LocalCompanionEngine().Evaluate(snapshot, pack, now);

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
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    ManualScenarioDraft draft = CreateLossStreakDraft(1) with { Topic = ManualTopic.RerollReview };
    LocalDecisionSnapshot snapshot = new(draft, pack.Version, now, now.AddMinutes(2));

    SemanticAdvice advice = new LocalCompanionEngine().Evaluate(snapshot, pack, now);

    Assert.Multiple(() =>
    {
        Assert.That(advice.Phase, Is.EqualTo(LocalAdvicePhase.Unknown));
        Assert.That(advice.PrimaryAction, Is.Null);
        Assert.That(advice.ReasonCode, Is.EqualTo("fixture.pack-unavailable"));
    });
}

private static ManualScenarioDraft CreateLossStreakDraft(long revision)
{
    return new ManualScenarioDraft(
        new Guid("11111111-1111-1111-1111-111111111111"),
        revision,
        "loss-streak-review-v1",
        ManualTopic.LossStreakReview,
        ManualIntent.PreserveLossStreak,
        ManualRiskBand.Medium,
        ManualRiskBand.Medium,
        ManualCopiesBand.Unknown,
        ManualUnitCostBand.Unknown,
        LocalFactProvenance.UserEntered);
}
~~~

- [ ] **Step 2: Run test to verify it fails**

Run the Task 1 test command.

Expected: compilation fails because snapshot/engine/advice types do not exist. Do not alter Task 1 types to force a false pass.

- [ ] **Step 3: Write minimal snapshot and engine implementation**

Create LocalDecisionSnapshot.cs with the three records in the exact public contract. Add these two types in the same file:

~~~csharp
public enum LocalCapabilityStatus { Eligible, Unknown, Degraded }

public sealed record LocalCapabilityResult(
    LocalCapabilityStatus Status,
    string ReasonCode,
    LocalPrecisionState Precision);
~~~

Create LocalCompanionEngine.cs with:

~~~csharp
public sealed class LocalCompanionEngine
{
    public SemanticAdvice Evaluate(LocalDecisionSnapshot snapshot, FrozenFixturePack pack, DateTimeOffset now);
}
~~~

Apply gate rules in this order:

0. Before the business gates, reject an invalid bounded manual input with Unknown, fixture.invalid-manual-input, no primary action, and LocalPrecisionState.Unknown. Invalid means an undefined enum value, an empty run/scenario/pack identity, ManualRevision <= 0, or ExpiresAt <= CreatedAt. This precondition does not reorder the five business gates for valid input.
1. now >= snapshot.ExpiresAt returns Expired, fixture.state-invalidated, no primary action, and LocalPrecisionState.Degraded.
2. snapshot.FixturePackVersion != pack.Version returns Unknown, fixture.version-mismatch, no primary action, and LocalPrecisionState.Unknown.
3. An unresolvable FixtureScenarioId, topic mismatch, or unsupported intent returns Unknown, fixture.pack-unavailable, no primary action, and LocalPrecisionState.Unknown.
4. LossStreakReview requires non-Unknown HealthBand and GoldBand. RerollReview requires non-Unknown GoldBand, CopiesBand, and UnitCostBand. Missing required values return Unknown, fixture.insufficient-facts, no primary action, and LocalPrecisionState.Unknown.
5. Otherwise return Current, LocalPrecisionState.Educational, exactly one primary candidate from the frozen definition, no more than two supporting candidates, and at most one observation.

Build the primary candidate exactly as follows:

~~~csharp
new LocalActionCandidate(
    scenario.ScenarioId + ":primary",
    scenario.MessageKey,
    scenario.ReasonCode,
    priority: 100,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["source"] = "FixtureOnly",
        ["precision"] = "Educational",
        ["topic"] = snapshot.Draft.Topic.ToString(),
        ["intent"] = snapshot.Draft.Intent.ToString()
    });
~~~

For all SemanticAdvice outputs, set SkillVersion to FixtureExpressionSkillContract.Version. Build one length-delimited, ordinal canonical string containing ManualRunId, ManualRevision, FixturePackVersion, FixtureScenarioId, Phase, ReasonCode, MessageKey, every bounded ManualScenarioDraft field, and the PrimaryAction LockedSlots sorted by key. Set AdviceId deterministically from the first 16 bytes of its SHA-256 digest and SemanticDigest to the full uppercase hexadecimal SHA-256 digest. Do not include the wall clock or mutable dictionary enumeration order in the canonical string.

The engine contains no player-facing natural-language sentences: only message keys, reason codes, slots, and fixed source/precision labels.

- [ ] **Step 4: Run test to verify it passes**

Run the Task 1 test command again.

Expected: the six Task 1/Task 2 tests pass. The test output must not start a Host or write any status document.

### Task 3: Add monotonic advice lifecycle, expression skill, and projection harness

**Files:**
- Create: src/TftCompanion.Poc.Core/LocalSimulation/LocalAdviceCoordinator.cs
- Create: src/TftCompanion.Poc.Core/LocalSimulation/EmbeddedFixtureExpressionSkill.cs
- Create: src/TftCompanion.Poc.Core/LocalSimulation/ManualPanelProjectionHarness.cs
- Modify: tests/TftCompanion.Poc.Tests/LocalCompanionSimulationTests.cs

- [ ] **Step 1: Write failing lifecycle, skill, and projection tests**

Append these tests. CreateCurrentAdvice may call LocalCompanionEngine.Evaluate with the Task 2 loss-streak draft and fixed clock.

~~~csharp
[Test]
public void newer_manual_revision_supersedes_old_revision_and_old_revision_cannot_revive()
{
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    LocalCompanionEngine engine = new();
    LocalAdviceCoordinator coordinator = new();

    SemanticAdvice revision1 = engine.Evaluate(new LocalDecisionSnapshot(CreateLossStreakDraft(1), pack.Version, now, now.AddMinutes(2)), pack, now);
    SemanticAdvice revision2 = engine.Evaluate(new LocalDecisionSnapshot(CreateLossStreakDraft(2), pack.Version, now, now.AddMinutes(2)), pack, now);

    Assert.That(coordinator.TryAccept(revision1), Is.True);
    Assert.That(coordinator.TryAccept(revision2), Is.True);
    Assert.Multiple(() =>
    {
        Assert.That(coordinator.Current!.ManualRevision, Is.EqualTo(2));
        Assert.That(coordinator.TryAccept(revision1), Is.False);
        Assert.That(coordinator.Current!.ManualRevision, Is.EqualTo(2));
    });
}

[Test]
public void clear_makes_old_projection_non_current_and_blocks_old_revision_revival()
{
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    SemanticAdvice advice = new LocalCompanionEngine().Evaluate(
        new LocalDecisionSnapshot(CreateLossStreakDraft(1), pack.Version, now, now.AddMinutes(1)), pack, now);
    LocalAdviceCoordinator coordinator = new();
    ManualPanelProjectionHarness harness = new(new EmbeddedFixtureExpressionSkill());

    Assert.That(coordinator.TryAccept(advice), Is.True);
    Assert.That(harness.Project(coordinator, now).IsCurrent, Is.True);

    Assert.That(coordinator.TryClear(advice.ManualRunId, revision: 2), Is.True);
    ManualPanelProjection afterClear = harness.Project(coordinator, now);
    Assert.Multiple(() =>
    {
        Assert.That(afterClear.IsCurrent, Is.False);
        Assert.That(afterClear.Phase, Is.EqualTo(LocalAdvicePhase.Cleared));
        Assert.That(coordinator.TryAccept(advice), Is.False);
    });
}

[Test]
public void expiry_makes_current_projection_non_current()
{
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    SemanticAdvice advice = new LocalCompanionEngine().Evaluate(
        new LocalDecisionSnapshot(CreateLossStreakDraft(1), pack.Version, now, now.AddMinutes(1)), pack, now);
    LocalAdviceCoordinator coordinator = new();
    ManualPanelProjectionHarness harness = new(new EmbeddedFixtureExpressionSkill());

    Assert.That(coordinator.TryAccept(advice), Is.True);
    ManualPanelProjection expired = harness.Project(coordinator, now.AddMinutes(1));

    Assert.Multiple(() =>
    {
        Assert.That(expired.IsCurrent, Is.False);
        Assert.That(expired.Phase, Is.EqualTo(LocalAdvicePhase.Expired));
        Assert.That(coordinator.Current, Is.Null);
    });
}

[Test]
public void invalid_skill_version_or_missing_locked_slot_returns_safe_silence()
{
    DateTimeOffset now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    FrozenFixturePack pack = FrozenFixturePack.CreateV1();
    SemanticAdvice current = new LocalCompanionEngine().Evaluate(
        new LocalDecisionSnapshot(CreateLossStreakDraft(1), pack.Version, now, now.AddMinutes(1)), pack, now);
    SemanticAdvice invalidVersion = current with { SkillVersion = "fixture-skill-v0" };
    SemanticAdvice missingSlot = current with
    {
        PrimaryAction = current.PrimaryAction! with
        {
            LockedSlots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "FixtureOnly",
                ["precision"] = "Educational"
            }
        }
    };
    EmbeddedFixtureExpressionSkill skill = new();

    Assert.Multiple(() =>
    {
        Assert.That(skill.TryRender(invalidVersion, out _), Is.False);
        Assert.That(skill.TryRender(missingSlot, out _), Is.False);
    });
}
~~~

- [ ] **Step 2: Run test to verify it fails**

Run the Task 1 test command.

Expected: compilation fails because coordinator, skill, harness, and projection types do not exist.

- [ ] **Step 3: Write minimal lifecycle coordinator**

Create LocalAdviceCoordinator.cs with this public surface:

~~~csharp
public sealed class LocalAdviceCoordinator
{
    public SemanticAdvice? Current { get; }
    public LocalAdvicePhase LastPhase { get; }
    public bool TryAccept(SemanticAdvice advice);
    public bool TryClear(Guid manualRunId, long revision);
    public SemanticAdvice? ExpireIfNeeded(DateTimeOffset now);
}
~~~

Implementation rules:

~~~text
- Accept only Phase=Current advice.
- Track a high-water revision per ManualRunId.
- Reject an incoming revision less than or equal to its run's high-water revision.
- When accepting a newer revision in the active run, mark the prior current advice superseded internally, then make the new advice Current.
- When a different run becomes current, permanently retire the previous active run; later advice from that retired run is rejected.
- TryClear succeeds only for the active run with revision strictly greater than the high-water revision. It advances the high-water revision, clears Current, and sets LastPhase=Cleared.
- ExpireIfNeeded clears a Current advice when now >= ExpiresAt and sets LastPhase=Expired.
- No method reads a clock, starts a task, writes a file, or exposes a mutable collection.
~~~

- [ ] **Step 4: Write the embedded expression skill**

Create EmbeddedFixtureExpressionSkill.cs with these public records and members:

~~~csharp
public sealed record RenderedFixtureAdvice(
    string MessageKey,
    string Text,
    string SemanticDigest,
    string SkillVersion,
    DateTimeOffset ExpiresAt);

public sealed class EmbeddedFixtureExpressionSkill
{
    public const string Version = FixtureExpressionSkillContract.Version;
    public bool TryRender(SemanticAdvice advice, out RenderedFixtureAdvice? rendered);
}
~~~

TryRender returns false and assigns null when any condition is true:

~~~text
advice.Phase is not Current
advice.SkillVersion is not Version
advice.PrimaryAction is null
MessageKey is not one of the two frozen fixture keys
any required locked slot source, precision, topic, or intent is absent or empty
~~~

The only player-facing strings in M1 live in this file and nowhere else:

~~~text
manual.loss-streak.review -> "Manual fixture review: treat the declared loss-streak plan as an educational checklist, not live game state."
manual.reroll.review      -> "Manual fixture review: check the declared resource and copy bands before treating a reroll plan as viable."
~~~

Both outcomes preserve message key, semantic digest, skill version, and expiry from input advice. Do not add fallback player-facing strings to the engine, coordinator, or projection harness.

- [ ] **Step 5: Write the projection harness**

Create ManualPanelProjectionHarness.cs with:

~~~csharp
public sealed record ManualPanelProjection(
    Guid? ManualRunId,
    long? ManualRevision,
    LocalAdvicePhase Phase,
    LocalPrecisionState Precision,
    string SourceLabel,
    string FixturePackVersion,
    string? MessageKey,
    string? RenderedText,
    string ReasonCode,
    DateTimeOffset? ExpiresAt,
    bool IsCurrent);

public sealed class ManualPanelProjectionHarness
{
    public ManualPanelProjectionHarness(EmbeddedFixtureExpressionSkill expressionSkill);
    public ManualPanelProjection Project(LocalAdviceCoordinator coordinator, DateTimeOffset now);
}
~~~

Project first calls coordinator.ExpireIfNeeded(now). If there is no current advice, return a non-current projection with Phase=coordinator.LastPhase, Precision=Degraded only for Expired/Cleared and Unknown otherwise, SourceLabel="Manual / FixtureOnly", and no rendered text. If a current advice fails TryRender, return non-current Phase=Degraded, Precision=Degraded, reason fixture.expression-unavailable, and no rendered text. If rendering succeeds, return its identity, Phase=Current, its advice precision, SourceLabel="Manual / FixtureOnly", frozen pack version, exact rendered text, and IsCurrent=true.

- [ ] **Step 6: Run test to verify it passes**

Run the Task 1 test command.

Expected: all local simulation tests pass. Report the count from the test runner and classify first-pass tests honestly as baseline Green coverage rather than inventing a Red-to-Green result.

### Task 4: Lock the project boundary and run complete verification

**Files:**
- Modify: tests/TftCompanion.Poc.Tests/ProjectBoundaryTests.cs
- Modify: tests/TftCompanion.Poc.Tests/LocalCompanionSimulationTests.cs only if a failing boundary/lifecycle assertion exposes a real defect during this task.

- [ ] **Step 1: Write the LocalSimulation assembly-boundary test**

Append this exact test to ProjectBoundaryTests:

~~~csharp
[Test]
public void local_simulation_stays_in_core_without_host_or_alife_dependencies()
{
    Assembly coreAssembly = typeof(ManualScenarioDraft).Assembly;
    string[] references = coreAssembly
        .GetReferencedAssemblies()
        .Select(reference => reference.Name ?? string.Empty)
        .ToArray();

    Assert.Multiple(() =>
    {
        Assert.That(coreAssembly.GetName().Name, Is.EqualTo("TftCompanion.Poc.Core"));
        Assert.That(references, Does.Not.Contain("TftCompanion.Poc.Host"));
        Assert.That(references, Does.Not.Contain("Microsoft.AspNetCore.App"));
        Assert.That(references, Does.Not.Contain("Alife.Platform"));
        Assert.That(references, Does.Not.Contain("Alife.Function.WebBridge"));
        Assert.That(references, Does.Not.Contain("Alife.Function.Speech"));
        Assert.That(references, Does.Not.Contain("Alife.Function.DataAgent"));
    });
}
~~~

Add using System.Reflection; and using TftCompanion.Poc.Core.LocalSimulation; if existing implicit usings do not resolve the names.

- [ ] **Step 2: Run boundary test**

Run:

~~~powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test D:\TFTCompanion\tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~ProjectBoundaryTests"
~~~

Expected: Green if Tasks 1-3 did not introduce an illegal dependency. This is baseline-Green regression coverage, not a Red-to-Green claim, unless an actual failure is observed and fixed in scope.

- [ ] **Step 3: Run source-boundary audit without modifying source**

Run from D:\TFTCompanion:

~~~powershell
rg -n -i "Overwolf|WebSocket|HttpClient|System\.Net|Sqlite|DataAgent|RAG|LLM|TTS|Speech|OCR|Screenshot|Capture|SendInput|SetForegroundWindow|mouse|keyboard|System\.IO|FileStream|StreamWriter|File\.|Directory\." src\TftCompanion.Poc.Core\LocalSimulation
~~~

Expected: no matches. If a match occurs in a comment, identifier, or implementation, remove or rename it only when it is inside new LocalSimulation files and preserves the required public contract. Do not suppress evidence and do not add prohibited runtime dependencies.

- [ ] **Step 4: Run the complete verification set**

Run from D:\TFTCompanion in this order:

~~~powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" build TftCompanion.Poc.slnx --configuration Release --no-restore --nologo
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo
npm test --prefix overwolf\tft-companion-poc
git diff --check
~~~

Expected:

~~~text
dotnet build: 0 warnings, 0 errors
dotnet test: all pre-existing tests plus new LocalSimulation tests pass
npm test: existing Overwolf regression suite remains green; no production Overwolf files changed by M1
git diff --check: exit 0; existing line-ending warnings, if any, must be reported separately from whitespace errors
~~~

- [ ] **Step 5: Produce a precise GLM handoff report without a VCS mutation**

The handoff report must state:

~~~text
HANDOFF_STATUS: PASS only if all Step 4 commands actually pass.
Track: v0.1-local/M1 only; v0.1.0 and TargetMachineGate remain unchanged.
Changed files: exact paths and one responsibility each.
Red/Green: distinguish observed Red-to-Green from baseline-Green regression coverage.
Verification: exact commands, working directories, exit codes, pass/fail counts.
Safety: no D:\Alife, no Host/Overwolf/renderer/manifest changes, no network/install/restore, no runtime storage, no prohibited APIs.
Known limits: fixture-only, educational/manual, no live TFT or panel integration.
Self-score: score out of 100 with explicit deductions.
~~~

Do not run git add, git commit, git push, git tag, git reset, git clean, git restore, or git stash.

## Plan self-review

### Spec coverage

| Design requirement | Plan coverage |
|---|---|
| Separate v0.1-local track and non-claims | Header, version boundary, locked scope, Task 4 handoff. |
| No real-game/Overwolf/Host path | Locked scope, file map, Task 4 dependency audit. |
| Bounded manual input without free text | Task 1 models and reflection test. |
| Frozen fixture-only knowledge | Task 1 pack implementation and Task 2 version gate. |
| Explicit Unknown/Degraded/safe silence | Task 2 gate order and Task 3 skill/projection behavior. |
| Deterministic candidates/digest | Task 2 fixed clock, canonical SHA-256 digest, determinism test. |
| At most one/two/one advice shape | SemanticAdvice contract and Task 2 test/implementation. |
| Single-writer replacement/clear/expiry semantics | Task 3 coordinator and lifecycle tests. |
| Dedicated expression Skill | Task 3 skill contract and no fallback-string rule. |
| PanelProjectionHarness only | Task 3 in-memory projection type; no UI project. |
| L0 evidence and full regression | Task 4 complete test/boundary/handoff steps. |

### Consistency and ambiguity check

- Every type referenced by later tasks is defined in the exact public contract or an earlier task.
- The only player-facing strings are assigned to the skill file, not the decision engine.
- No automatic current-data or live-board claim is introduced by fixture names or tests.
- M2 Console work is not included in this plan.
- No VCS mutation appears in an execution step because the user has not authorized one.
