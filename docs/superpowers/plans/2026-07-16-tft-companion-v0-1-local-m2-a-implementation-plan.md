# TFT Companion v0.1-local / M2-A Second-Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a user-launched Windows WPF second-screen companion that accepts bounded manual checkpoints, displays exactly one lifecycle-safe FixtureOnly coaching card, and optionally restores the minimum safe session snapshot from the approved D: location.

**Architecture:** `TftCompanion.SecondScreen` is a `net9.0-windows` WPF application that references only `TftCompanion.Poc.Core`. A manual session controller owns M1's engine/coordinator/harness chain and exposes only a projection-plus-display-state model to the WPF ViewModel. A separate fixed-file recovery adapter validates the existing D: root and the same file handle before any read, normal write, or explicit user-requested provisioning; it never touches the v0.0.1 Host status file.

**Tech Stack:** .NET 9, WPF, C# records/enums, `System.Text.Json`, BCL SHA-256, Win32 P/Invoke, NUnit 4; no new NuGet packages and no network dependency.

**Version boundary:** This plan implements `v0.1-local / M2-A`, not `v0.1.0`. `TargetMachineGate` remains `Pending / BlockedExternal`. Passing local tests must not be described as verification of Overwolf, TFT GEP, a real TFT board, a real D: target machine, Alife Panel, Riot API, OCR, or public-store readiness.

**VCS rule:** Do not run `git add`, `git commit`, `git push`, `git tag`, `git reset`, `git clean`, `git restore`, `git stash`, `git checkout`, PR, or Release commands. The existing dirty/untracked working tree belongs to the user and Git cannot attribute an individual untracked file to a particular implementation round.

---

## 0. Locked scope and runtime rules

### In scope

```text
One user-launched, ordinary, movable/resizable WPF window.
One Quick Checkpoint form with bounded enum selections only.
One Current Advice Card produced through M1's lifecycle-safe projection chain.
One status text generated only by SecondScreenPresentationSkill.
One optional D:\AlifeData\TFTCompanion\manual-session-v1.json recovery snapshot.
In-memory NUnit tests, source-boundary tests, and user-run manual smoke steps.
```

### Explicitly out of scope

```text
Overwolf, GEP, Host, localhost, HTTP, WebSocket, browser/PWA, network/downloads,
OCR, screenshots, video/window capture, game/process/window detection, injection,
memory reads, packet capture, reverse engineering, hotkeys, mouse/keyboard control,
automation, overlay, transparent/click-through UI, Topmost behavior, tray/background
service, static knowledge packs, Riot APIs, post-game review, SQLite, advice history,
LLM/RAG/DataAgent, TTS, real TFT data, enemy board data, or Alife integration.
```

```text
- The user launches the app; it does not auto-start, auto-open, auto-focus, auto-move,
  auto-pin, monitor displays, or monitor game processes.
- The WPF DispatcherTimer exists only while the window is alive and a current advice has
  an expiry. It is not a watcher, poller, background service, or persistence trigger.
- ViewModel never consumes SemanticAdvice and never calls EmbeddedFixtureExpressionSkill.
- Only ManualPanelProjectionHarness.Project(coordinator, now) authorizes a current card.
- EmbeddedFixtureExpressionSkill remains the only author of fixture coaching body text.
- SecondScreenPresentationSkill remains the only author of state/recovery text.
- No runtime file path may fall back to C:, AppData, Temp, or a default user directory.
- D:\Alife and its source tree, including AlifePath.cs, are never modified.
```

### Fixed M2-A choices

```text
Current advice lifetime: 2 minutes (matching existing M1 fixture tests).
Start New Session: creates a new ManualRunId, HighestRevision=0, EditingCheckpoint,
and saves only if the fixed recovery file is already available.
Submit: requires an active session, matching topic, and topic-required enum facts;
it increments ManualRevision exactly once and gives advice a 2-minute expiry.
Clear: advances high-water revision exactly once, removes Current, and saves Cleared if available.
Expiry: removes the card through M1 Harness but never writes autonomously.
Provisioning: only explicit "Enable D: Recovery" may create manual-session-v1.json.
It never creates D:\AlifeData or D:\AlifeData\TFTCompanion.
```

## 1. File map

| File | Action | Sole responsibility |
|---|---|---|
| `TftCompanion.Poc.slnx` | Modify | Include the new WPF project. |
| `src/TftCompanion.SecondScreen/TftCompanion.SecondScreen.csproj` | Create | WPF project, Core-only project reference, no package references. |
| `src/TftCompanion.SecondScreen/App.xaml` / `App.xaml.cs` | Create/modify | User-launch app shell and explicit composition startup only. |
| `src/TftCompanion.SecondScreen/MainWindow.xaml` / `.cs` | Create | One two-column window and window-owned expiry timer lifecycle. |
| `src/TftCompanion.SecondScreen/Composition/SecondScreenComposition.cs` | Create/modify | Explicit construction; no DI container or service locator. |
| `src/TftCompanion.SecondScreen/Sessions/ManualSessionContracts.cs` | Create | Bounded checkpoint, session, clock, and ID contracts. |
| `src/TftCompanion.SecondScreen/Sessions/ManualSessionController.cs` | Create | M1 lifecycle integration, revision policy, recovery transitions, and refresh. |
| `src/TftCompanion.SecondScreen/Presentation/SecondScreenPresentationSkill.cs` | Create | Sole source of player-visible non-advice status/recovery text. |
| `src/TftCompanion.SecondScreen/ViewModels/RelayCommand.cs` | Create | Minimal command adapter only. |
| `src/TftCompanion.SecondScreen/ViewModels/SecondScreenViewModel.cs` | Create | Projection/display state binding only; no advice rendering. |
| `src/TftCompanion.SecondScreen/Recovery/ManualSessionRecoveryContracts.cs` | Create | Fixed snapshot schema, statuses, and narrow file-operation contracts. |
| `src/TftCompanion.SecondScreen/Recovery/ManualSessionRecoveryCodec.cs` | Create | Strict canonical UTF-8 JSON and SHA-256 validation. |
| `src/TftCompanion.SecondScreen/Recovery/ManualSessionRecoveryStore.cs` | Create | Open-existing persistence/recovery and explicit-provisioning policy. |
| `src/TftCompanion.SecondScreen/Recovery/WindowsManualSessionRecoveryFileSystem.cs` | Create | Win32 fixed-path same-handle D: validation and I/O. |
| `src/TftCompanion.Poc.Core/LocalSimulation/LocalAdviceCoordinator.cs` | Modify | Safe terminal-state rehydration only. |
| `tests/TftCompanion.Poc.Tests/TftCompanion.Poc.Tests.csproj` | Modify | Target `net9.0-windows`, reference WPF project, preserve packages. |
| `tests/TftCompanion.Poc.Tests/LocalCompanionSimulationTests.cs` | Modify | M1 terminal-state rehydration coverage. |
| `tests/TftCompanion.Poc.Tests/SecondScreenProjectBoundaryTests.cs` | Create | Assembly/source boundaries. |
| `tests/TftCompanion.Poc.Tests/ManualSessionControllerTests.cs` | Create | Session/revision/expiry/recovery behavior. |
| `tests/TftCompanion.Poc.Tests/SecondScreenPresentationSkillTests.cs` | Create | Text authority and state behavior. |
| `tests/TftCompanion.Poc.Tests/ManualSessionRecoveryCodecTests.cs` | Create | Schema/digest/enum/size/forbidden-field validation. |
| `tests/TftCompanion.Poc.Tests/ManualSessionRecoveryStoreTests.cs` | Create | Provisioning/open-existing/no-fallback behavior. |
| `tests/TftCompanion.Poc.Tests/SecondScreenViewModelTests.cs` | Create | ViewModel projection-only and bounded form behavior. |
| `tests/TftCompanion.Poc.Tests/TestSupport/FakeManualSessionRecoveryFileSystem.cs` | Create | Pure in-memory recovery I/O fake. |
| `tests/TftCompanion.Poc.Tests/TestSupport/FakeSecondScreenClock.cs` | Create | Deterministic clock. |
| `tests/TftCompanion.Poc.Tests/TestSupport/SequenceManualRunIdGenerator.cs` | Create | Deterministic ManualRunIds. |

## 2. Exact contracts

### 2.1 M1 terminal rehydration extension

Add exactly this public member to `LocalAdviceCoordinator`:

```csharp
public bool TryRestoreTerminalState(
    Guid manualRunId,
    long highWaterRevision,
    LocalAdvicePhase phase);
```

It accepts only `Cleared` or `Expired`; rejects empty IDs, non-positive revisions, retired runs, nonterminal phases, and lower-than-known high water. On success: active lineage becomes the supplied run, high water is exactly restored, `Current` is `null`, and `LastPhase` is supplied. It reads no clock and performs no I/O.

### 2.2 Session and presentation contracts

```csharp
namespace TftCompanion.SecondScreen.Sessions;

public static class ManualSessionPolicy
{
    public static readonly TimeSpan CurrentAdviceLifetime = TimeSpan.FromMinutes(2);
}

public enum ManualSessionPhase
{
    NoSession,
    EditingCheckpoint,
    CurrentAdvice,
    Cleared,
    Expired
}

public enum ManualSessionNotice
{
    None,
    Started,
    Submitted,
    IncompleteCheckpoint,
    TopicChangeRequiresNewSession,
    NoActiveSession,
    Cleared,
    Expired,
    RecoveryEnabled,
    RecoveryUnavailable,
    RecoveryRejected
}

public sealed record ManualCheckpoint(
    ManualTopic Topic,
    ManualIntent Intent,
    ManualRiskBand HealthBand,
    ManualRiskBand GoldBand,
    ManualCopiesBand CopiesBand,
    ManualUnitCostBand UnitCostBand);

public interface ISecondScreenClock { DateTimeOffset UtcNow { get; } }
public interface IManualRunIdGenerator { Guid Create(); }

public sealed record SecondScreenSessionState(
    ManualPanelProjection Projection,
    ManualSessionPhase SessionPhase,
    ManualRecoveryStatus RecoveryStatus,
    ManualSessionNotice Notice);
```

```csharp
namespace TftCompanion.SecondScreen.Presentation;

public sealed record SecondScreenPresentation(
    bool IsCurrentAdviceVisible,
    string? AdviceText,
    string StatusText,
    LocalPrecisionState Precision,
    DateTimeOffset? ExpiresAt,
    string SourceLabel,
    string FixturePackVersion);

public sealed class SecondScreenPresentationSkill
{
    public SecondScreenPresentation Present(SecondScreenSessionState state);
}
```

`AdviceText` is only a pass-through of `state.Projection.RenderedText` when the card is current. The presentation skill must never generate or alter coaching body text.

### 2.3 Recovery contracts

```csharp
namespace TftCompanion.SecondScreen.Recovery;

public static class ManualSessionRecoveryContract
{
    public const string SchemaVersion = "manual-session-v1";
    public const string FixedFileName = "manual-session-v1.json";
    public const int MaximumSnapshotBytes = 8_192;
}

public enum ManualRecoveryStatus
{
    MemoryOnlyDegraded,
    RecoveryAvailable,
    RecoveryRejected
}

public sealed record ManualSessionRecoveryPayload(
    string SchemaVersion,
    long SnapshotGeneration,
    Guid ManualRunId,
    long HighestRevision,
    ManualSessionPhase SessionPhase,
    string FixtureScenarioId,
    ManualTopic Topic,
    ManualIntent Intent,
    ManualRiskBand HealthBand,
    ManualRiskBand GoldBand,
    ManualCopiesBand CopiesBand,
    ManualUnitCostBand UnitCostBand,
    LocalFactProvenance Provenance,
    string FixturePackVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ManualSessionRecoveryDocument(
    ManualSessionRecoveryPayload Payload,
    string CanonicalPayloadDigest);

public sealed record ManualRecoveryFileResult(
    bool Success,
    ManualRecoveryStatus Status,
    string FailureCode,
    byte[]? Utf8Document);

public interface IManualSessionRecoveryFileSystem
{
    ManualRecoveryFileResult TryReadExisting(int maximumBytes);
    ManualRecoveryFileResult TryWriteExisting(ReadOnlyMemory<byte> utf8);
    ManualRecoveryFileResult TryProvisionFixedFile();
}

public sealed record ManualRecoveryLoadResult(
    ManualRecoveryStatus Status,
    ManualSessionRecoveryPayload? Snapshot,
    string FailureCode);

public sealed record ManualRecoverySaveResult(
    ManualRecoveryStatus Status,
    string FailureCode);
```

The payload intentionally contains no `RenderedText`, `SemanticAdvice`, free-text note, match/round identity, GEP field, screenshot/OCR field, window data, or network field.

### 2.4 Controller surface

```csharp
public sealed class ManualSessionController
{
    public ManualSessionController(
        ManualSessionRecoveryStore recoveryStore,
        ISecondScreenClock clock,
        IManualRunIdGenerator runIdGenerator);

    public SecondScreenSessionState Restore();
    public SecondScreenSessionState StartNewSession(ManualTopic topic);
    public SecondScreenSessionState Submit(ManualCheckpoint checkpoint);
    public SecondScreenSessionState ClearCurrentAdvice();
    public SecondScreenSessionState EnableDDriveRecovery();
    public SecondScreenSessionState Refresh();
}
```

The controller is the only M2-A type allowed to call `LocalCompanionEngine.Evaluate`, mutate `LocalAdviceCoordinator`, call `ManualPanelProjectionHarness.Project`, or call store save/provision methods.

## 3. TDD execution tasks

### Task 1: Add the Windows project shell and lock its dependency boundary

**Files:**

- Create: `src/TftCompanion.SecondScreen/TftCompanion.SecondScreen.csproj`
- Create: `src/TftCompanion.SecondScreen/App.xaml`
- Create: `src/TftCompanion.SecondScreen/App.xaml.cs`
- Create: `src/TftCompanion.SecondScreen/Composition/SecondScreenComposition.cs`
- Modify: `TftCompanion.Poc.slnx`
- Modify: `tests/TftCompanion.Poc.Tests/TftCompanion.Poc.Tests.csproj`
- Create: `tests/TftCompanion.Poc.Tests/SecondScreenProjectBoundaryTests.cs`

- [ ] **Step 1: Create only the descriptor necessary for a red boundary test**

Create the WPF project with no `PackageReference`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>TftCompanion.SecondScreen</AssemblyName>
    <RootNamespace>TftCompanion.SecondScreen</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TftCompanion.Poc.Core\TftCompanion.Poc.Core.csproj" />
  </ItemGroup>
</Project>
```

Change the existing test project target to `net9.0-windows`; retain all existing package versions and add this reference:

```xml
<ProjectReference Include="..\..\src\TftCompanion.SecondScreen\TftCompanion.SecondScreen.csproj" />
```

Add the project under existing `/src/` in `TftCompanion.Poc.slnx`; do not replace the solution or alter Host/Core paths.

- [ ] **Step 2: Write the failing assembly-boundary test**

```csharp
using System.Reflection;
using NUnit.Framework;
using TftCompanion.SecondScreen.Composition;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class SecondScreenProjectBoundaryTests
{
    [Test]
    public void second_screen_assembly_references_core_but_not_host_overwolf_or_alife()
    {
        Assembly assembly = typeof(SecondScreenComposition).Assembly;
        string[] references = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetName().Name, Is.EqualTo("TftCompanion.SecondScreen"));
            Assert.That(references, Does.Contain("TftCompanion.Poc.Core"));
            Assert.That(references, Does.Not.Contain("TftCompanion.Poc.Host"));
            Assert.That(references, Does.Not.Contain("Alife.Platform"));
            Assert.That(references, Does.Not.Contain("Alife.Function.WebBridge"));
            Assert.That(references, Does.Not.Contain("Alife.Function.Speech"));
            Assert.That(references, Does.Not.Contain("Alife.Function.DataAgent"));
        });
    }
}
```

- [ ] **Step 3: Run the focused test and record the true Red**

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~SecondScreenProjectBoundaryTests"
```

Expected: compile Red because `SecondScreenComposition` does not exist. Do not describe a compile error as a behavioral Red.

- [ ] **Step 4: Add minimal composition-only green code**

```csharp
namespace TftCompanion.SecondScreen.Composition;

public static class SecondScreenComposition
{
    public static string AssemblyBoundaryMarker => "TftCompanion.SecondScreen";
}
```

Create `App.xaml` with no startup side effect and `App.xaml.cs` as an empty partial `Application` class (with `using System.Windows;`). Do not create a window, timer, tray icon, registry value, process detector, or background task in this task.

- [ ] **Step 5: Re-run the focused test**

Run Step 3 again. Expected: `1 passed / 0 failed`; compilation must not launch a WPF window.

### Task 2: Add only the M1 terminal-state rehydration primitive needed by verified recovery

**Files:**

- Modify: `src/TftCompanion.Poc.Core/LocalSimulation/LocalAdviceCoordinator.cs`
- Modify: `tests/TftCompanion.Poc.Tests/LocalCompanionSimulationTests.cs`

- [ ] **Step 1: Write failing terminal-rehydration tests**

Append these tests, using existing deterministic `RunA`, `RunB`, and `BuildCurrent` helpers:

```csharp
[Test]
public void restored_cleared_terminal_state_preserves_high_water_and_requires_a_newer_revision()
{
    LocalAdviceCoordinator coordinator = new();
    Assert.That(coordinator.TryRestoreTerminalState(RunA, 7, LocalAdvicePhase.Cleared), Is.True);

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
    Assert.That(coordinator.TryRestoreTerminalState(RunA, 4, LocalAdvicePhase.Expired), Is.True);
    Assert.That(coordinator.TryAccept(BuildCurrent(RunB, revision: 1)), Is.True);
    Assert.That(coordinator.TryAccept(BuildCurrent(RunA, revision: 5)), Is.False);
}

[TestCase(LocalAdvicePhase.Current)]
[TestCase(LocalAdvicePhase.Unknown)]
[TestCase(LocalAdvicePhase.Degraded)]
[TestCase(LocalAdvicePhase.Superseded)]
public void terminal_restore_rejects_nonterminal_phase(LocalAdvicePhase phase)
{
    Assert.That(new LocalAdviceCoordinator().TryRestoreTerminalState(RunA, 1, phase), Is.False);
}
```

- [ ] **Step 2: Run selected M1 tests for Red**

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~LocalCompanionSimulationTests"
```

Expected: compile Red because `TryRestoreTerminalState` is absent.

- [ ] **Step 3: Implement the narrow restore method**

```csharp
public bool TryRestoreTerminalState(
    Guid manualRunId,
    long highWaterRevision,
    LocalAdvicePhase phase)
{
    if (manualRunId == Guid.Empty ||
        highWaterRevision <= 0 ||
        phase is not (LocalAdvicePhase.Cleared or LocalAdvicePhase.Expired) ||
        _retiredRuns.Contains(manualRunId))
        return false;

    if (_highWaterByRun.TryGetValue(manualRunId, out long knownHighWater) &&
        highWaterRevision < knownHighWater)
        return false;

    if (_activeRunId is Guid activeRunId && activeRunId != manualRunId)
        _retiredRuns.Add(activeRunId);

    _highWaterByRun[manualRunId] = highWaterRevision;
    _activeRunId = manualRunId;
    Current = null;
    LastPhase = phase;
    return true;
}
```

It accepts no `SemanticAdvice`, never sets `Current`, never creates text, never writes a snapshot, and does not change `TryAccept`, `TryClear`, or expiry behavior.

- [ ] **Step 4: Re-run M1 and boundary tests**

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~LocalCompanionSimulationTests|FullyQualifiedName~ProjectBoundaryTests"
```

Expected: selected tests pass. A pre-existing M1 regression is a stop condition.

### Task 3: Define the bounded, canonical recovery snapshot codec before any D: I/O

**Files:**

- Create: `src/TftCompanion.SecondScreen/Recovery/ManualSessionRecoveryContracts.cs`
- Create: `src/TftCompanion.SecondScreen/Recovery/ManualSessionRecoveryCodec.cs`
- Create: `tests/TftCompanion.Poc.Tests/ManualSessionRecoveryCodecTests.cs`

- [ ] **Step 1: Write failing codec tests using only in-memory UTF-8 bytes**

Use this fixed payload in the test fixture:

```csharp
private static readonly ManualSessionRecoveryPayload ValidCurrent = new(
    SchemaVersion: ManualSessionRecoveryContract.SchemaVersion,
    SnapshotGeneration: 11,
    ManualRunId: new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
    HighestRevision: 4,
    SessionPhase: ManualSessionPhase.CurrentAdvice,
    FixtureScenarioId: "loss-streak-review-v1",
    Topic: ManualTopic.LossStreakReview,
    Intent: ManualIntent.PreserveLossStreak,
    HealthBand: ManualRiskBand.Medium,
    GoldBand: ManualRiskBand.High,
    CopiesBand: ManualCopiesBand.Unknown,
    UnitCostBand: ManualUnitCostBand.Unknown,
    Provenance: LocalFactProvenance.UserEntered,
    FixturePackVersion: "fixture-v1",
    CreatedAt: new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero),
    ExpiresAt: new DateTimeOffset(2026, 7, 16, 10, 2, 0, TimeSpan.Zero));
```

Start with this round-trip/determinism test:

```csharp
[Test]
public void canonical_encode_round_trips_a_valid_snapshot_without_rendered_text()
{
    ManualSessionRecoveryCodec codec = new();
    byte[] first = codec.Encode(ValidCurrent);
    byte[] second = codec.Encode(ValidCurrent);

    Assert.That(first, Is.EqualTo(second));
    Assert.That(Encoding.UTF8.GetString(first), Does.Not.Contain("RenderedText"));
    Assert.That(codec.TryDecode(first, out ManualSessionRecoveryPayload? decoded, out string failure), Is.True);
    Assert.Multiple(() =>
    {
        Assert.That(failure, Is.EqualTo("NONE"));
        Assert.That(decoded, Is.EqualTo(ValidCurrent));
    });
}
```

Add individual, syntactically valid mutation tests that assert `false`, a null payload, and a named non-`NONE` failure for each of these inputs:

```text
digest mismatch; unknown root/payload property; duplicate root/payload property;
SnapshotGeneration <= 0; undefined enum; Guid.Empty CurrentAdvice run;
CurrentAdvice HighestRevision <= 0; invalid scenario/topic pair; ExpiresAt <= CreatedAt
for CurrentAdvice; malformed JSON; invalid UTF-8; and > MaximumSnapshotBytes.
```

Each mutation must use `JsonDocument`/`Utf8JsonWriter` or a fixed test literal so it changes exactly one condition. Do not test a broken JSON string when the assertion is supposed to prove a digest mismatch.

- [ ] **Step 2: Run codec tests to verify Red**

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~ManualSessionRecoveryCodecTests"
```

Expected: compile Red because contracts and codec are absent.

- [ ] **Step 3: Implement strict contracts and canonical codec**

Create `ManualSessionRecoveryContracts.cs` with the exact contracts in section 2.3. Create `Sessions/ManualSessionContracts.cs` now with the exact session contracts in section 2.2 so `ManualSessionPhase` is available to the recovery records.

Implement this codec surface:

```csharp
public sealed class ManualSessionRecoveryCodec
{
    public byte[] Encode(ManualSessionRecoveryPayload payload);
    public bool TryDecode(
        ReadOnlySpan<byte> utf8,
        out ManualSessionRecoveryPayload? payload,
        out string failureCode);
}
```

`Encode` emits exactly two root properties, in this order:

```json
{"payload":{...fixed payload order...},"canonicalPayloadDigest":"UPPERCASE_SHA256"}
```

The exact payload order is:

```text
schemaVersion, snapshotGeneration, manualRunId, highestRevision, sessionPhase,
fixtureScenarioId, topic, intent, healthBand, goldBand, copiesBand, unitCostBand,
provenance, fixturePackVersion, createdAt, expiresAt
```

Use `Utf8JsonWriter`; write enums as ordinal enum-name strings, GUIDs as normalized `N` strings, and timestamps in invariant UTC round-trip form. Digest the exact canonical payload-object bytes using SHA-256 and emit upper-case hex.

`TryDecode` must execute this exact sequence:

```text
1. Reject null/empty or >8,192 bytes before parsing.
2. Parse strict UTF-8 JSON; reject malformed input.
3. Require exactly payload and canonicalPayloadDigest at root; reject duplicate or extra fields.
4. Require every listed payload property exactly once; reject duplicate or extra fields.
5. Parse enum names only with Enum.TryParse and Enum.IsDefined.
6. Validate schema version, provenance=UserEntered, scenario/topic relationship, phase-specific
   identity/revision rules, and current snapshot time ordering.
7. Re-encode the parsed payload with the same writer, compare 64-character uppercase SHA-256
   digest using CryptographicOperations.FixedTimeEquals, and reject any mismatch.
8. On failure return no partial payload.
```

Phase validation is fixed:

```text
NoSession: Guid.Empty, HighestRevision=0, Intent=Review, all bands Unknown.
EditingCheckpoint: nonempty run, HighestRevision=0, Intent=Review, all bands Unknown.
CurrentAdvice/Cleared/Expired: nonempty run, HighestRevision>0, valid topic fixture ID.
CurrentAdvice: ExpiresAt > CreatedAt and every topic-required band is non-Unknown.
Cleared/Expired: preserve the last bounded checkpoint and require ExpiresAt >= CreatedAt.
```

The `NoSession` sentinel is exact: `FixtureScenarioId=string.Empty`, `Topic=LossStreakReview` (ignored because phase is NoSession), `FixturePackVersion="fixture-v1"`, `Provenance=UserEntered`, and `ExpiresAt=CreatedAt`. The `EditingCheckpoint` snapshot uses the active topic's fixed scenario ID, `Intent=Review`, unknown bands, `Provenance=UserEntered`, and `ExpiresAt=CreatedAt`. The controller never sends either sentinel draft to M1 Evaluate. On a successful load it seeds its in-memory `snapshotGeneration` from the payload; every successful state-changing save attempt (Start, Submit, Clear, explicit provisioning) increments it before constructing the next payload.

No method deserializes `SemanticAdvice`, `RenderedFixtureAdvice`, `ManualPanelProjection`, dictionaries, free text, or arbitrary extension data.

- [ ] **Step 4: Re-run codec tests**

Run Step 2 again. Expected: all cases pass in memory and no file handle is opened.

### Task 4: Add a fixed-file D: recovery store with explicit provisioning only

**Files:**

- Create: `src/TftCompanion.SecondScreen/Recovery/ManualSessionRecoveryStore.cs`
- Create: `src/TftCompanion.SecondScreen/Recovery/WindowsManualSessionRecoveryFileSystem.cs`
- Create: `tests/TftCompanion.Poc.Tests/TestSupport/FakeManualSessionRecoveryFileSystem.cs`
- Create: `tests/TftCompanion.Poc.Tests/ManualSessionRecoveryStoreTests.cs`

- [ ] **Step 1: Write failing store tests against an in-memory fake**

`FakeManualSessionRecoveryFileSystem` implements the section 2.3 interface, records operation names and requested paths, returns configured byte results, and never touches `System.IO` or D:. Write tests that prove:

```text
ordinary save calls only WriteExisting and cannot call ProvisionFixedFile;
explicit enable calls ProvisionFixedFile once then WriteExisting, creates only manual-session-v1.json;
missing root/file produces MemoryOnlyDegraded;
corrupt/oversized file produces RecoveryRejected with null Snapshot;
valid document round-trips;
write failure leaves the store result degraded; and no C:/AppData/Temp path appears in fake observations.
```

Use these strict assertions in the ordinary-save and explicit-provision tests:

```csharp
Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "WriteExisting" }));
Assert.That(fileSystem.Operations, Does.Not.Contain("ProvisionFixedFile"));

Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ProvisionFixedFile", "WriteExisting" }));
Assert.That(fileSystem.CreatedPaths, Is.EqualTo(new[] { "manual-session-v1.json" }));
```

- [ ] **Step 2: Run store tests to verify Red**

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~ManualSessionRecoveryStoreTests"
```

Expected: compile Red because store and adapter types are absent.

- [ ] **Step 3: Implement the recovery store policy**

```csharp
public sealed class ManualSessionRecoveryStore
{
    public ManualSessionRecoveryStore(
        IManualSessionRecoveryFileSystem fileSystem,
        ManualSessionRecoveryCodec codec);

    public ManualRecoveryLoadResult TryLoad();
    public ManualRecoverySaveResult TrySave(ManualSessionRecoveryPayload snapshot);
    public ManualRecoverySaveResult TryEnableProvisioning(ManualSessionRecoveryPayload snapshot);
}
```

Required behavior:

```text
TryLoad: TryReadExisting(8192) -> codec.TryDecode -> RecoveryAvailable only on full success.
         Missing/unavailable storage is MemoryOnlyDegraded; corrupt/untrusted bytes are RecoveryRejected.
TrySave: codec.Encode -> TryWriteExisting only. It must never provision a missing file.
         A write failure returns MemoryOnlyDegraded and cannot mutate caller memory.
TryEnableProvisioning: TryProvisionFixedFile exactly once -> codec.Encode -> TryWriteExisting.
         Provision/write failure returns MemoryOnlyDegraded; success is RecoveryAvailable.
```

No method creates a directory, uses a secondary filename, stores a prior advice list, or retries a failed operation at another location.

- [ ] **Step 4: Implement the production fixed-file filesystem adapter**

Create `WindowsManualSessionRecoveryFileSystem` with no path constructor and these fixed constants:

```csharp
private const string DVolumeRoot = @"D:\";
private const string ParentPath = @"D:\AlifeData";
private const string RootPath = @"D:\AlifeData\TFTCompanion";
private const string RecoveryPath = @"D:\AlifeData\TFTCompanion\manual-session-v1.json";
private const string ParentFinalPath = @"\\?\D:\AlifeData";
private const string RootFinalPath = @"\\?\D:\AlifeData\TFTCompanion";
private const string RecoveryFinalPath = @"\\?\D:\AlifeData\TFTCompanion\manual-session-v1.json";

private const uint CreateNew = 1;
private const uint OpenExisting = 3;
private const uint FileFlagWriteThrough = 0x80000000;
private const uint FileFlagBackupSemantics = 0x02000000;
private const uint FileFlagOpenReparsePoint = 0x00200000;
```

Every operation follows this algorithm:

```text
1. Get D: volume GUID using GetVolumeNameForVolumeMountPointW("D:\").
2. Open both existing directories using OPEN_EXISTING with BACKUP_SEMANTICS and OPEN_REPARSE_POINT.
3. On each actual handle inspect GetFileInformationByHandle and final paths from both
   GetFinalPathNameByHandleW(VolumeNameGuid) and GetFinalPathNameByHandleW(VolumeNameDos).
4. Require directory=true, reparse=false, exact fixed canonical path, and actual D: volume GUID.
5. Ordinary read/write opens RecoveryPath only with OPEN_EXISTING + OPEN_REPARSE_POINT.
6. Explicit provision validates existing directories then uses CREATE_NEW only on RecoveryPath;
   if file already exists it uses OPEN_EXISTING instead. It never creates another filename or temp file.
7. On the actual file handle require not-directory, not-reparse, NumberOfLinks==1, exact
   RecoveryFinalPath, and matching volume GUID.
8. Read: check file size <=8,192 before allocation. Write: reject >8,192, SetLength(0), write
   exact bytes, then Flush(flushToDisk:true), all through the same validated handle.
9. Convert all expected errors into named failure codes; never throw an unhandled path/fallback error to UI.
```

Use `SafeFileHandle`, `FileStream`, `CreateFileW`, `GetFinalPathNameByHandleW`, `GetFileInformationByHandle`, `GetFileSizeEx`, and `GetVolumeNameForVolumeMountPointW`. Keep all P/Invoke/struct/private helpers in this one class. Do not alter the Host's `WindowsStorageFileSystem`, `IStorageFileSystem`, `StorageRootPolicy`, or `poc-status.json` contract.

- [ ] **Step 5: Add source guards and re-run store tests**

Add a test-only source reader and assert the adapter contains/does not contain these facts:

```csharp
Assert.Multiple(() =>
{
    Assert.That(source, Does.Contain(@"manual-session-v1.json"));
    Assert.That(source, Does.Not.Contain("poc-status.json"));
    Assert.That(source, Does.Not.Contain("Directory.CreateDirectory"));
    Assert.That(source, Does.Not.Contain("AppData"));
    Assert.That(source, Does.Not.Contain("Temp"));
    Assert.That(source, Does.Not.Contain(@"C:\"));
    Assert.That(source, Does.Contain("FileFlagOpenReparsePoint"));
    Assert.That(source, Does.Contain("NumberOfLinks != 1"));
    Assert.That(source, Does.Contain("GetFinalPathNameByHandleW"));
    Assert.That(source, Does.Contain("CreateNew"));
    Assert.That(source, Does.Contain("OpenExisting"));
});
```

Run Step 2 again. Expected: all tests pass through fakes and source checks only; no automated test provisions or writes the real D: drive.

### Task 5: Integrate bounded manual sessions, M1 lifecycle projection, and state-text ownership

**Files:**

- Create: `src/TftCompanion.SecondScreen/Sessions/ManualSessionController.cs`
- Create: `src/TftCompanion.SecondScreen/Presentation/SecondScreenPresentationSkill.cs`
- Create: `tests/TftCompanion.Poc.Tests/TestSupport/FakeSecondScreenClock.cs`
- Create: `tests/TftCompanion.Poc.Tests/TestSupport/SequenceManualRunIdGenerator.cs`
- Create: `tests/TftCompanion.Poc.Tests/ManualSessionControllerTests.cs`
- Create: `tests/TftCompanion.Poc.Tests/SecondScreenPresentationSkillTests.cs`

- [ ] **Step 1: Write failing session-controller tests**

Use the fake recovery filesystem, deterministic clock, and sequential IDs. Add these strict tests:

```csharp
[Test]
public void start_submit_clear_and_higher_resubmit_keep_one_run_and_monotonic_revisions()
{
    ManualSessionController controller = CreateController(out _, out _);

    SecondScreenSessionState started = controller.StartNewSession(ManualTopic.LossStreakReview);
    SecondScreenSessionState first = controller.Submit(LossStreakCheckpoint());
    SecondScreenSessionState cleared = controller.ClearCurrentAdvice();
    SecondScreenSessionState second = controller.Submit(
        LossStreakCheckpoint() with { Intent = ManualIntent.PrepareToStabilize });

    Assert.Multiple(() =>
    {
        Assert.That(started.SessionPhase, Is.EqualTo(ManualSessionPhase.EditingCheckpoint));
        Assert.That(first.Projection.IsCurrent, Is.True);
        Assert.That(first.Projection.ManualRevision, Is.EqualTo(1));
        Assert.That(cleared.Projection.IsCurrent, Is.False);
        Assert.That(cleared.Projection.Phase, Is.EqualTo(LocalAdvicePhase.Cleared));
        Assert.That(second.Projection.IsCurrent, Is.True);
        Assert.That(second.Projection.ManualRevision, Is.EqualTo(3));
        Assert.That(second.Projection.ManualRunId, Is.EqualTo(first.Projection.ManualRunId));
    });
}

[Test]
public void topic_change_requires_new_run_and_old_run_cannot_reappear_after_new_session()
{
    ManualSessionController controller = CreateController(out _, out _);
    SecondScreenSessionState firstStart = controller.StartNewSession(ManualTopic.LossStreakReview);
    SecondScreenSessionState firstAdvice = controller.Submit(LossStreakCheckpoint());
    SecondScreenSessionState rejected = controller.Submit(RerollCheckpoint());
    SecondScreenSessionState secondStart = controller.StartNewSession(ManualTopic.RerollReview);
    SecondScreenSessionState secondAdvice = controller.Submit(RerollCheckpoint());

    Assert.Multiple(() =>
    {
        Assert.That(rejected.Notice, Is.EqualTo(ManualSessionNotice.TopicChangeRequiresNewSession));
        Assert.That(secondStart.Projection.IsCurrent, Is.False);
        Assert.That(secondStart.Projection.RenderedText, Is.Null);
        Assert.That(secondStart.Projection.ManualRunId, Is.Not.EqualTo(firstStart.Projection.ManualRunId));
        Assert.That(secondAdvice.Projection.ManualRunId, Is.Not.EqualTo(firstAdvice.Projection.ManualRunId));
        Assert.That(secondAdvice.Projection.IsCurrent, Is.True);
    });
}

[Test]
public void incomplete_checkpoint_never_creates_current_advice()
{
    ManualSessionController controller = CreateController(out _, out _);
    controller.StartNewSession(ManualTopic.RerollReview);
    SecondScreenSessionState state = controller.Submit(
        RerollCheckpoint() with { CopiesBand = ManualCopiesBand.Unknown });

    Assert.Multiple(() =>
    {
        Assert.That(state.Notice, Is.EqualTo(ManualSessionNotice.IncompleteCheckpoint));
        Assert.That(state.Projection.IsCurrent, Is.False);
        Assert.That(state.Projection.RenderedText, Is.Null);
    });
}

[Test]
public void refresh_at_expiry_removes_current_card_without_an_automatic_write()
{
    ManualSessionController controller = CreateController(out FakeSecondScreenClock clock, out FakeManualSessionRecoveryFileSystem fileSystem);
    controller.EnableDDriveRecovery();
    controller.StartNewSession(ManualTopic.LossStreakReview);
    SecondScreenSessionState current = controller.Submit(LossStreakCheckpoint());
    int writesBeforeExpiry = fileSystem.Operations.Count(operation => operation == "WriteExisting");
    clock.UtcNow = current.Projection.ExpiresAt!.Value;
    SecondScreenSessionState expired = controller.Refresh();

    Assert.Multiple(() =>
    {
        Assert.That(expired.SessionPhase, Is.EqualTo(ManualSessionPhase.Expired));
        Assert.That(expired.Projection.IsCurrent, Is.False);
        Assert.That(expired.Projection.RenderedText, Is.Null);
        Assert.That(fileSystem.Operations.Count(operation => operation == "WriteExisting"), Is.EqualTo(writesBeforeExpiry));
    });
}
```

Add exact tests for Submit with no session; Clear with no current card; D-write failure preserving the in-memory current card but reporting `MemoryOnlyDegraded`; valid Current snapshot restore re-evaluating and displaying through Harness; valid Cleared snapshot restoring Cleared through Harness; expired saved Current restoring an expired/no-card state; invalid saved data never displaying old text; and a recovered old run remaining rejected after a new run becomes current.

- [ ] **Step 2: Run controller/presentation tests for Red**

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~ManualSessionControllerTests|FullyQualifiedName~SecondScreenPresentationSkillTests"
```

Expected: compile Red because controller/session/presentation types do not exist.

- [ ] **Step 3: Implement deterministic controller policy**

Add:

```csharp
public sealed class SystemSecondScreenClock : ISecondScreenClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class SystemManualRunIdGenerator : IManualRunIdGenerator
{
    public Guid Create() => Guid.NewGuid();
}
```

`ManualSessionController` creates `FrozenFixturePack.CreateV1()`, `LocalCompanionEngine`, `LocalAdviceCoordinator`, `EmbeddedFixtureExpressionSkill`, and `ManualPanelProjectionHarness` internally. Its private state is only active run ID/topic, highest revision, last bounded checkpoint, session phase, snapshot generation, coordinator, and recovery status.

Topic mapping is exact:

```csharp
private static string FixtureScenarioFor(ManualTopic topic) => topic switch
{
    ManualTopic.LossStreakReview => "loss-streak-review-v1",
    ManualTopic.RerollReview => "reroll-review-v1",
    _ => throw new ArgumentOutOfRangeException(nameof(topic))
};
```

Implement transitions exactly:

```text
Restore: TryLoad once; never write. NoSession/Editing restore controller lineage only.
CurrentAdvice reconstructs a draft, Evaluate(pack, now), and TryAccept only if Current.
An expired result restores Expired terminal state; Cleared/Expired payload restores matching terminal
state; every result projects through Harness. Rejected/unknown input shows no old text.

StartNewSession: generated nonempty ID; revision 0; Intent=Review; all bands Unknown; EditingCheckpoint.
If a previous run has a Current card, first call TryClear(previousRun, previousHighestRevision+1) so the old
card cannot remain visible while the new session is being edited. Then increment snapshot generation and ordinary
TrySave; a missing file is not created.

Submit: reject without mutation for no session, different topic, or required Unknown band. Use revision
highest+1, UserEntered provenance, fixed scenario ID, CreatedAt=clock.UtcNow, and ExpiresAt=CreatedAt+2min.
Accept only M1 Current. On success update high water/checkpoint/phase and ordinary TrySave.

Clear: only active current card; TryClear(activeRun, highest+1); update high water and Cleared; ordinary
TrySave. Storage failure never rolls back memory.

Refresh: Harness.Project(coordinator, clock.UtcNow); record Expired when projected. Never TrySave.

EnableDDriveRecovery: build bounded NoSession/Editing/Current/Cleared/Expired payload and call only
TryEnableProvisioning. It cannot create parent/root or another file name.
```

Every return uses `ManualPanelProjectionHarness.Project`; never pass a historical `SemanticAdvice` directly to the expression skill.

- [ ] **Step 4: Implement the only M2-A state text skill**

`SecondScreenPresentationSkill.cs` is the only M2-A file containing these non-advice player-facing strings:

```text
"Start a manual fixture session to begin."
"Complete the bounded checkpoint fields before submitting."
"The current advice was cleared."
"The current advice expired and is no longer shown."
"D: recovery is available."
"This session is in memory only and will not be restored after closing."
"The saved recovery snapshot was rejected; no prior advice was restored."
```

`Present` maps session phase/notice/recovery status to one state string. For a current projection it passes `RenderedText`, expiry, source, pack version, and precision unchanged. Otherwise it returns `AdviceText=null`. It never interpolates or shows `ReasonCode`, calls no expression skill, and holds no history.

- [ ] **Step 5: Re-run focused tests**

Run Step 2 again. Expected: controller/presentation tests pass. First-pass tests are baseline-Green unless an actual behavioral Red was observed.

### Task 6: Build the WPF second-screen UI without adding a live-data or background path

**Files:**

- Create: `src/TftCompanion.SecondScreen/ViewModels/RelayCommand.cs`
- Create: `src/TftCompanion.SecondScreen/ViewModels/SecondScreenViewModel.cs`
- Create: `src/TftCompanion.SecondScreen/MainWindow.xaml`
- Create: `src/TftCompanion.SecondScreen/MainWindow.xaml.cs`
- Modify: `src/TftCompanion.SecondScreen/App.xaml.cs`
- Modify: `src/TftCompanion.SecondScreen/Composition/SecondScreenComposition.cs`
- Create: `tests/TftCompanion.Poc.Tests/SecondScreenViewModelTests.cs`
- Modify: `tests/TftCompanion.Poc.Tests/SecondScreenProjectBoundaryTests.cs`

- [ ] **Step 1: Write failing ViewModel/UI-boundary tests**

Exercise a real controller with fakes through the ViewModel:

```csharp
[Test]
public void view_model_displays_projection_presentation_without_direct_advice_rendering()
{
    SecondScreenViewModel viewModel = CreateViewModel();
    viewModel.SelectedTopic = ManualTopic.LossStreakReview;
    viewModel.StartNewSessionCommand.Execute(null);
    viewModel.SelectedIntent = ManualIntent.PreserveLossStreak;
    viewModel.SelectedHealthBand = ManualRiskBand.Medium;
    viewModel.SelectedGoldBand = ManualRiskBand.High;
    viewModel.SubmitCheckpointCommand.Execute(null);

    Assert.Multiple(() =>
    {
        Assert.That(viewModel.IsCurrentAdviceVisible, Is.True);
        Assert.That(viewModel.AdviceText, Is.Not.Null.And.Not.Empty);
        Assert.That(viewModel.StatusText, Is.Not.Empty);
        Assert.That(viewModel.CurrentAdviceExpiresAt, Is.Not.Null);
    });
}
```

Add static source/XAML assertions:

```csharp
Assert.That(viewModelSource, Does.Not.Contain("EmbeddedFixtureExpressionSkill"));
Assert.That(viewModelSource, Does.Not.Contain("TryRender("));
Assert.That(viewModelSource, Does.Not.Contain("SemanticAdvice"));
Assert.That(mainWindowXaml, Does.Not.Contain("TextBox"));
Assert.That(mainWindowXaml, Does.Not.Contain("Topmost"));
Assert.That(mainWindowXaml, Does.Not.Contain("AllowsTransparency"));
Assert.That(mainWindowSource, Does.Contain("Activated"));
Assert.That(mainWindowSource, Does.Contain("DispatcherTimer"));
Assert.That(mainWindowSource, Does.Contain("Stop()"));
Assert.That(mainWindowSource, Does.Not.Contain("SetForegroundWindow"));
```

Run a production-source audit over `src/TftCompanion.SecondScreen` for:

```text
Overwolf|GEP|WebSocket|HttpClient|System.Net|SQLite|DataAgent|RAG|LLM|TTS|OCR|
Screenshot|Capture|SendInput|SetForegroundWindow|GetForegroundWindow|Process.GetProcesses|
FileSystemWatcher|Registry|NotifyIcon|Topmost|AllowsTransparency
```

- [ ] **Step 2: Run focused UI tests for Red**

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo --filter "FullyQualifiedName~SecondScreenViewModelTests|FullyQualifiedName~SecondScreenProjectBoundaryTests"
```

Expected: compile Red because ViewModel, command, and window code do not exist.

- [ ] **Step 3: Implement projection-only ViewModel and command adapter**

```csharp
public sealed class RelayCommand : ICommand
{
    private readonly Action execute;
    private readonly Func<bool>? canExecute;
    public RelayCommand(Action execute, Func<bool>? canExecute = null);
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged();
}
```

`SecondScreenViewModel` implements `INotifyPropertyChanged`, contains no player-visible coaching/status literals, and exposes only:

```csharp
public IReadOnlyList<ManualTopic> Topics { get; }
public IReadOnlyList<ManualIntent> Intents { get; }
public IReadOnlyList<ManualRiskBand> RiskBands { get; }
public IReadOnlyList<ManualCopiesBand> CopiesBands { get; }
public IReadOnlyList<ManualUnitCostBand> UnitCostBands { get; }
public ManualTopic SelectedTopic { get; set; }
public ManualIntent SelectedIntent { get; set; }
public ManualRiskBand SelectedHealthBand { get; set; }
public ManualRiskBand SelectedGoldBand { get; set; }
public ManualCopiesBand SelectedCopiesBand { get; set; }
public ManualUnitCostBand SelectedUnitCostBand { get; set; }
public bool IsCurrentAdviceVisible { get; private set; }
public string? AdviceText { get; private set; }
public string StatusText { get; private set; }
public DateTimeOffset? CurrentAdviceExpiresAt { get; private set; }
public ICommand StartNewSessionCommand { get; }
public ICommand SubmitCheckpointCommand { get; }
public ICommand ClearCurrentAdviceCommand { get; }
public ICommand EnableDDriveRecoveryCommand { get; }
public void RestoreAndRefresh();
public void RefreshProjection();
```

Each command makes exactly one controller call then passes the returned `SecondScreenSessionState` to `SecondScreenPresentationSkill.Present`. `RestoreAndRefresh` calls `controller.Restore()` exactly once for the user-launched app startup path; `RefreshProjection` calls only `controller.Refresh()`. The ViewModel copies only fields from `SecondScreenPresentation`; it does not inspect `ReasonCode` and has no `SemanticAdvice` parameter/property/field.

- [ ] **Step 4: Implement the single ordinary WPF window**

Create a two-column `MainWindow.xaml` beginning with:

```xml
<Window x:Class="TftCompanion.SecondScreen.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:core="clr-namespace:TftCompanion.Poc.Core.LocalSimulation;assembly=TftCompanion.Poc.Core"
        Title="TFT Companion · Manual / FixtureOnly"
        Width="980" Height="620" MinWidth="760" MinHeight="480">
    <Grid Margin="20">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
    </Grid>
</Window>
```

Complete it with `ComboBox` controls only for six enum fields, a `DataTrigger` showing HealthBand only for `LossStreakReview`, and DataTriggers showing CopiesBand/UnitCostBand only for `RerollReview`. The right side binds exactly one advice `TextBlock` to `AdviceText` and its visibility to `IsCurrentAdviceVisible`, plus four fixed buttons: Start New Session, Submit Checkpoint, Clear Current Advice, Enable D: Recovery. No `TextBox`, `ListBox`, history, popup, tray, `Topmost`, transparent/click-through behavior, or automatic window movement is allowed.

`MainWindow.xaml.cs` owns one window-scoped `DispatcherTimer`:

```csharp
private readonly DispatcherTimer expiryTimer = new();

private void RefreshAndSchedule()
{
    viewModel.RefreshProjection();
    expiryTimer.Stop();
    if (viewModel.IsCurrentAdviceVisible && viewModel.CurrentAdviceExpiresAt is DateTimeOffset expiresAt)
    {
        TimeSpan due = expiresAt - DateTimeOffset.UtcNow;
        expiryTimer.Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1);
        expiryTimer.Start();
    }
}
```

Tick stops the timer before `RefreshAndSchedule()`. `Activated` calls `RefreshAndSchedule()`; `Closed` stops it and detaches handlers. Neither handler writes recovery data. `SecondScreenComposition` explicitly constructs `WindowsManualSessionRecoveryFileSystem`, codec, store, controller, skill, and ViewModel. `App.OnStartup` calls `viewModel.RestoreAndRefresh()` exactly once, then shows MainWindow only because the user manually launched the executable.

- [ ] **Step 5: Re-run focused UI/boundary tests**

Run Step 2 again. Expected: selected tests pass without showing a window, creating a D: directory, or starting Host/Overwolf/TFT.

### Task 7: Run boundary audits, complete regression evidence, and hand off without VCS mutations

**Files:**

- Modify only when fresh verification proves a scoped defect in Tasks 1–6.
- Do not modify: `D:\Alife`, `src/TftCompanion.Poc.Host`, `overwolf`, manifests, `package.json`, v0.0.1 bridge/renderer files, or `poc-status.json` storage files.

- [ ] **Step 1: Run explicit source-boundary audits**

Run each command separately from `D:\TFTCompanion`. A no-match `rg` result exits `1`; report that as the expected no-match result, not as a test failure.

```powershell
rg -n -i "Overwolf|GEP|WebSocket|HttpClient|System\.Net|SQLite|DataAgent|RAG|LLM|TTS|OCR|Screenshot|Capture|SendInput|SetForegroundWindow|GetForegroundWindow|Process\.GetProcesses|FileSystemWatcher|Registry|NotifyIcon" src\TftCompanion.SecondScreen
```

```powershell
rg -n -i "poc-status\.json|Directory\.CreateDirectory|AppData|Temp|C:\\" src\TftCompanion.SecondScreen\Recovery
```

```powershell
rg -n "EmbeddedFixtureExpressionSkill|TryRender\(|SemanticAdvice|ReasonCode" src\TftCompanion.SecondScreen\ViewModels
```

Expected: no prohibited runtime source matches. If a required test contains a word from the list, restrict the audit to `src` as shown; do not hide evidence or rename an approved Core contract merely to satisfy a grep.

- [ ] **Step 2: Run the complete automated verification set**

Run from `D:\TFTCompanion` in this order. Do not run `restore`, `install`, Host, Overwolf, TFT, a browser, a network service, or a real D: provisioning operation.

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" build TftCompanion.Poc.slnx --configuration Release --no-restore --nologo
```

```powershell
& "$env:USERPROFILE\dotnet9\dotnet.exe" test tests\TftCompanion.Poc.Tests\TftCompanion.Poc.Tests.csproj --configuration Release --no-restore --nologo
```

```powershell
npm test --prefix overwolf\tft-companion-poc
```

```powershell
git diff --check
```

Expected evidence:

```text
Build: 0 warnings / 0 errors.
.NET test: all existing v0.0.1/M1 tests plus M2-A tests pass.
npm test: existing Overwolf regressions remain green; M2-A does not alter production Overwolf files.
git diff --check: exit 0; report any existing LF/CRLF warnings separately from actual whitespace errors.
```

- [ ] **Step 3: Perform only user-controlled manual WPF smoke checks**

After the automated gate passes, ask the user before launching the WPF executable. Do not open it automatically. With no game, Host, Overwolf, simulator, network service, capture, or OCR running, the user manually verifies:

```text
1. Launch the app and drag the ordinary window to a second display.
2. Start LossStreakReview, submit complete fields, and see one current educational card.
3. Submit a second same-run checkpoint, clear it, and submit a higher revision.
4. Start RerollReview and verify it uses a new run rather than reviving the old one.
5. Wait for current-card expiry and verify the card disappears.
6. Click Enable D: Recovery only if D:\AlifeData\TFTCompanion was pre-provisioned by an explicit external deployment step.
7. Close/reopen to verify a valid current snapshot is recomputed; verify bad/expired input never displays old text.
8. With root absent or write denied, verify the in-memory card remains usable but recovery status is memory-only.
```

The smoke is local UI evidence only. It does not change TargetMachineGate or verify v0.0.1/Overwolf/TFT integration.

- [ ] **Step 4: Produce the implementation handoff report**

Only if every actually run automated command passes, report:

```text
HANDOFF_STATUS: PASS (M2-A local WPF/fixture scope only)
TargetMachineGate: Pending / BlockedExternal
Changed files: exact paths and one responsibility per file
Red/Green: distinguish compile Red, behavioral Red, and baseline-Green coverage
Verification: exact command, working directory, exit code, warning/error/pass/fail counts
Storage: no real target-machine claim; fake/static evidence only unless user manually ran scoped smoke
Safety: no D:\Alife modification, network/install/restore, Host/Overwolf/TFT/OCR/automation, or VCS mutation
Known limits: FixtureOnly / Manual / Educational; no live game data, static pack, post-game API, or OCR
Self-score: /100 with explicit deductions
```

Never call a handoff successful based only on an agent report; the implementer must read fresh command output and report any manual-pending gate plainly.

## 4. Plan self-review

### Spec coverage

| Approved M2-A requirement | Plan coverage |
|---|---|
| WPF second-screen, no Overlay/browser/background behavior | Tasks 1 and 6; Task 7 audit/smoke |
| Bounded Quick Checkpoint and dynamic topic fields | Task 5 validation; Task 6 XAML/DataTriggers/ViewModel |
| One lifecycle-safe current card | Task 5 routes every state through M1 Harness; Task 6 has one binding |
| No direct stale SemanticAdvice rendering | Tasks 5–6 controller/ViewModel source gates |
| Dedicated state-text authority | Task 5 `SecondScreenPresentationSkill` |
| Same-run revisions, clear, expiry, old-run retirement | Tasks 2 and 5 tests/transition table |
| 2-minute expiry and activation refresh | Task 5 policy/expiry test; Task 6 one-shot DispatcherTimer |
| Fixed D: snapshot, no `poc-status.json` reuse | Tasks 3–4 contracts/adapter/source guard |
| Explicit provisioning, no root creation/fallback | Task 4 fake tests and Win32 algorithm |
| Same-handle path/volume/reparse/hardlink validation | Task 4 adapter requirements/source assertions |
| Restore only after validation plus re-evaluation | Tasks 3 and 5 restore rules/tests |
| No automatic expiry write or history persistence | Task 5 test/controller rules |
| Existing M1/v0.0.1 regression evidence | Task 7 full build/.NET/npm verification |
| TargetMachineGate remains pending | Header and Task 7 handoff language |

### Consistency check

```text
- ManualSessionPhase is deliberately distinct from M1 LocalAdvicePhase: it models persisted M2 session state;
  M1 Harness remains the only current-card authority.
- Clear uses revision 2 after a first submit at revision 1, so same-run submit after Clear is revision 3.
- Start New Session has revision 0 only in its Editing snapshot; it never sends revision 0 to M1 Evaluate.
- Timer reads/projections only; writes occur after Start, successful Submit, Clear, or explicit provisioning.
- The 2-minute duration is explicit, reviewable, and not delegated to implementation guesswork.
- manual-session-v1.json is the sole M2-A file: no temporary file, journal, database, cache, history, or fallback path.
- No task contains a Git mutation because the user prohibited VCS writes.
```

### Required plan self-check before approval

Run from `D:\TFTCompanion`:

```powershell
rg -n -i "TO(DO)|T(BD)|FIX(ME)|place(holder)|implement[ ]later|add[ ]appropriate[ ]error[ ]handling|handle[ ]edge[ ]cases" docs\superpowers\plans\2026-07-16-tft-companion-v0-1-local-m2-a-implementation-plan.md
```

Expected: no matches. If a match appears, replace it with an exact behavior, a named failure code, or a concrete test before presenting the plan for implementation approval.
