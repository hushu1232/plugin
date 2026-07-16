# TFT Companion v0.1-local: FixtureOnly Local Companion Simulation

**Status:** Approved design; implementation not yet authorized  
**Date:** 2026-07-15  
**Track:** `v0.1-local` (parallel local engineering track)  
**Scope:** FixtureOnly Local Companion Simulation, followed by an optional manual Console second-screen surface

## 1. Purpose and version boundary

The public/real-game TFT path remains blocked at the target-machine gate:

```text
TargetMachineGate: Pending / BlockedExternal
v0.1.0: not released for implementation
```

The blocking condition is the Overwolf Developer Console submission flow: in multiple clean browsers, activating the visually enabled Submit control produces neither a request nor a visible error. This document does not attempt to bypass Overwolf account, developer-client, whitelisting, unpublished-app loading, or game-access controls.

`v0.1-local` is therefore a separate, local-only engineering track. It is not a replacement for `v0.1.0`, does not consume or satisfy G1/G2, is not a public release candidate, and must not be described as real Overwolf, real TFT GEP, real game, real D-drive, or Alife Panel validation.

Its aim is to validate a small, deterministic coaching-delivery kernel without collecting game data or affecting a game:

```text
manual fixture selection
  -> capability gate
  -> immutable decision snapshot
  -> frozen fixture knowledge
  -> deterministic semantic advice
  -> advice lifecycle
  -> versioned expression skill
  -> in-memory panel projection harness
```

## 2. Non-negotiable safety boundary

The local track is read-only with respect to the game and other processes. It must not add or use:

```text
- Overwolf, TFT GEP, League Client, game process integration, Host startup, WebSocket, HTTP, loopback IPC, or a local web server
- screenshots, OCR, video, window capture, memory reading, injection, packet capture, process scraping, or opponent-board data
- mouse, keyboard, ADB, hotkeys, focus, window movement, window following, transparency, mouse pass-through, overlays, or game automation
- external gameplay APIs, downloads, providers, watcher services, networking, SQLite, cache/database writes, or runtime files
- LLM, Grok, RAG, embeddings, DataAgent runtime, TTS, QQ Speech, or voice playback
- real version data, real composition statistics, real win rates, real player/account data, or real opponent information
```

The only permitted interaction is a user selecting bounded values inside a future Companion-owned Console surface. Such selection must not be sent, injected, forwarded, or coupled to a game, emulator, Overwolf, or another process.

M1 contains no runtime file writing at all. It does not use `D:\AlifeData\TFTCompanion`, and it must never introduce C-drive/AppData/Temp fallback behavior.

`D:\Alife`, including `D:\Alife\sources\Alife\Alife.Platform\AlifePath.cs`, remains out of scope and unmodified.

## 3. Current engineering reality

The repository currently contains a v0.0.1 loopback/transport safety PoC. Its local state machine covers session, freshness, sequence gaps, render identity, leases, canonical pairing, and D-drive status-file safety.

It does **not** contain v0.1 decision or presentation domains. There are no implemented `DecisionSnapshot`, `CapabilityMatrix`, `AdviceCoordinator`, `RenderedAdvice`, `PanelProjection`, `PanelProjectionHarness`, FastPath, or fixture-coach types.

The existing `IngressMessage` has only minimal typed presence facts (`sessionId`, `sequence`, `matchObserved`, `roundObserved`, `isAuthoritativeSnapshot`). These are not sufficient for real TFT coaching conclusions such as composition, economy, reroll, items, augments, board positions, opponent status, unit counts, or pool calculations. The local track must not reuse this type as a manual-input surrogate and must not modify `PocHostFactory`, the Overwolf bridge, renderer, manifest, or v0.0.1 transport tests.

## 4. Considered approaches

### A. Core-only fixture simulation and projection harness

A pure Core + NUnit implementation with no user-facing executable. It validates decision, lifecycle, expression, and projection contracts in memory.

**Strengths:** smallest safety surface; no UI or new process; easiest to prove deterministic and dependency-free.  
**Limitation:** it is an engineering harness, not yet a manual companion surface.

### B. Core fixture simulation followed by a normal Console second-screen surface

Build A first, then add a normal `Microsoft.NET.Sdk` Console program that lets the user choose limited manual scenarios and displays a card. The Console has no relationship with the game window.

**Strengths:** forms a safe user-controlled loop without Overwolf or a desktop overlay.  
**Limitation:** adds input/UX lifecycle after the core is proven.

### C. Desktop WPF/WinUI panel now

Build a separate desktop side panel immediately.

**Strengths:** richer presentation.  
**Limitations:** adds a new UI host, installation, window lifecycle, threading, and visual testing before decision semantics are stable.

### Decision

Adopt A as `v0.1-local/M1`. Treat B as `v0.1-local/M2`, only after M1 has passed its local acceptance gate and a separate implementation decision. Do not begin C in this track.

## 5. M1 architecture

### 5.1 Data flow

```text
ManualScenarioDraft (bounded, UserEntered/Fixture provenance)
  -> LocalCapabilityGate
  -> LocalDecisionSnapshot (immutable)
  -> FrozenFixturePack (in-memory, versioned, read-only)
  -> LocalCompanionEngine (deterministic)
  -> SemanticAdvice (no player-facing natural language)
  -> LocalAdviceCoordinator (single writer, revision/expiry semantics)
  -> EmbeddedFixtureExpressionSkill
       render-tft-companion-advice-fixture-v1
  -> ManualPanelProjectionHarness (in-memory DTO)
```

`ManualPanelProjectionHarness` is a test/development port. It is not an Alife Panel, Overwolf renderer, overlay, browser UI, standalone desktop window, or IPC transport.

### 5.2 Manual scenario contract

The initial contract contains only enumerated values. It has no free text and no fabricated game-session identifiers.

```text
manualRunId             independent local identifier
manualRevision          strictly increasing within a run
fixtureScenarioId       bounded fixture identifier
topic                   LossStreakReview | RerollReview
intent                  Review | PreserveLossStreak | PrepareToStabilize | ConsiderReroll
healthBand              Unknown | Low | Medium | High
goldBand                Unknown | Low | Medium | High
copiesBand              Unknown | Few | NearThreshold | Complete
unitCostBand            Unknown | OneToThree | Four | Five
fixturePackVersion      fixed frozen pack version
provenance              UserEntered or Fixture
precision               ManualDirectional / Educational / Unknown / Degraded
```

It must never contain or claim `matchId`, `roundKey`, GEP sequence, runtime patch state, game board coordinates, player state, opponent state, or live game verification.

### 5.3 Capability and safe degradation

The capability gate is explicit, local, and conservative:

| Condition | Result |
|---|---|
| required bounded input is Unknown | `Unknown`; no Current advice |
| fixture scenario or pack is absent | `Unknown`; `fixture.pack-unavailable` |
| fixture version does not match snapshot | `Unknown`; `fixture.version-mismatch` |
| a snapshot is stale, superseded, cleared, or expired | `Degraded`/`Expired`; no current advice |
| all required fixture fields exist | `Current`, but explicitly `FixtureOnly` and `ManualDirectional`/`Educational` |

The engine must not guess or silently promote user-entered facts to game-verified or precise facts.

### 5.4 Fixture knowledge and deterministic advice

M1 contains a very small frozen pack, in code, with no reader, downloader, cache, database, or network request. The pack is for pipeline validation, not current TFT knowledge.

The first engine output is semantic only. It may emit keys such as:

```text
fixture.insufficient-facts
fixture.state-invalidated
fixture.pack-unavailable
manual.loss-streak.review
manual.reroll.review
```

It must not emit real game claims such as a specific current composition, win rate, item recommendation, board coordinate, pool count, enemy-board observation, real-time reroll instruction, or patch judgment.

For an eligible snapshot, the engine may return at most:

```text
one primary action
two supporting actions
one observation
```

Candidate ordering, semantic digest, and outcome must be deterministic for the same frozen snapshot and fixture pack.

### 5.5 Advice lifecycle

`LocalAdviceCoordinator` is the only writer of current advice state. Its minimum states are:

```text
Proposed -> Current -> Superseded | Expired | Cleared
```

Rules:

1. A new `manualRevision` produces a new advice revision; it does not mutate an old advice object.
2. A newer revision supersedes the previous current advice.
3. Clear and TTL expiry remove current status.
4. A stale callback or old revision may be observed by a caller but can never restore current status.
5. An invalid snapshot creates `Unknown`/`Degraded`/safe silence rather than a replacement factual claim.

### 5.6 Expression Skill and projection

The expression boundary is an embedded, versioned deterministic skill named:

```text
render-tft-companion-advice-fixture-v1
```

Business code may pass only:

```text
messageKey
reasonCode
lockedSlots
precisionState
semanticDigest
skillVersion
expiresAt
```

Only the skill renders player-facing text. If the skill is missing, version-incompatible, expired, or passed incomplete slots, it returns safe silence. There is no fallback string in the decision engine.

The resulting projection exposes only:

```text
manualRunId
manualRevision
projection/advice identity
current/unknown/degraded/expired state
source label (Manual / FixtureOnly)
precision label
fixture version
semantic message key and rendered text (when valid)
reason code and expiry
```

## 6. Planned M1 source boundary

M1 reuses existing `TftCompanion.Poc.Core` and `TftCompanion.Poc.Tests`; it does not add a project or package.

```text
src/TftCompanion.Poc.Core/LocalSimulation/
  ManualScenario.cs
  LocalDecisionSnapshot.cs
  FrozenFixturePack.cs
  LocalCompanionEngine.cs
  LocalAdviceCoordinator.cs
  EmbeddedFixtureExpressionSkill.cs
  ManualPanelProjectionHarness.cs

tests/TftCompanion.Poc.Tests/LocalSimulation/
  LocalCompanionSimulationTests.cs
```

The exact file grouping may be adjusted during implementation to maintain small single-purpose types, but it must remain within this Core/Test boundary. There is no M1 change to `PocHostFactory.cs`, `overwolf/`, `D:\Alife`, manifests, or the storage/loopback runtime.

## 7. L0 acceptance gate

`L0 - FixtureOnly Manual Companion Gate` is independent of G1/G2. It passes only if tests show all of the following:

1. Same `ManualScenarioDraft` plus same frozen pack version yields the same candidate ordering, semantic digest, and projection.
2. Missing required inputs, missing fixture packs, and incompatible versions result in `Unknown`/safe silence, never a Current precise-looking advice.
3. A new manual revision supersedes the old revision; stale revision callbacks cannot revive it.
4. Clear and TTL expiry make the previous projection non-current.
5. Invalid semantic slots or Skill versions produce safe silence; business code does not contain player-facing fallback advice sentences.
6. The local domain has no runtime dependency on Host, Overwolf, renderer, networking, file I/O, SQLite, DataAgent, LLM/RAG, TTS, OCR, capture, window control, or input APIs.
7. Tests run without starting a Host or listening on a port and without D-drive writes.

Passing L0 permits only this statement:

```text
v0.1-local LocalBehaviorGreen: YES
TargetMachineGate: Pending / BlockedExternal
v0.1.0: still not released for implementation
```

## 8. Deferred M2 and later work

M2 may add a normal Console-based second-screen interface that collects the same bounded values and displays `ManualPanelProjection`. It must use the identical Core contracts and must not read or control a game.

M2 is separately approved only after M1 review. WPF/WinUI, an Alife Panel adapter, real TFT/Overwolf integration, voice, third-party knowledge, real version packs, live coaching, and any overlay remain later design tasks with their own gates.

## 9. Explicit non-claims

This design must not be interpreted as evidence that any of the following works:

```text
real Overwolf integration
real TFT GEP integration
real game overlay
real TFT game coaching
real D-drive behavior in the target environment
Alife Panel integration
public/plugin-market release readiness
```
