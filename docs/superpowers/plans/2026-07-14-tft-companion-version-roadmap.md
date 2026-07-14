# TFT Companion Version Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:writing-plans` to create a separate, detailed implementation plan for an approved version before writing code. This roadmap contains version gates and estimates only; it is not authorization to implement any version.

**Goal:** Deliver the read-only TFT companion in independently testable versions without allowing the long-term specification to inflate the first self-use release.

**Architecture:** Preserve the approved safety contracts—read-only operation, D-drive-only plugin writes, deterministic FastPath, stale-content invalidation, Panel-first delivery and High-confidence-only Overlay—but introduce them in a narrow vertical slice. Each version adds one user-visible or risk-reducing closed loop; external risk gates can stop or safely degrade the roadmap without pulling later systems forward.

**Tech Stack:** Existing Alife .NET 9 codebase as reference only; a new isolated TFT Companion runtime; Overwolf TFT GEP/Background Bridge; loopback IPC; local structured Packs; D:\AlifeData\TFTCompanion storage; optional independent TTS Provider; deterministic local tests and target-machine custom-game smoke tests.

---

## 1. Planning status and non-negotiable boundaries

This roadmap is approved for discussion after the final design specification and its §20.1 scope guard. It deliberately does **not** create code, a worktree, a service, a Provider account, a network request, or a Git commit.

Every version retains all of the following:

- No game input, no ADB, no process-memory access, no injection, no packet capture and no protocol bypass.
- Overlay remains permanently mouse-through; all controls remain outside the game window.
- All plugin-controlled writes resolve under `D:\AlifeData\TFTCompanion\`; D-drive failure never falls back to C drive, AppData or Temp.
- FastPath never waits for a network request, remote LLM, RAG, SQLite or DataAgent.
- DataAgent never analyzes the current game, ranks recommendations or owns Advice / Voice / Overlay state.
- Missing, stale, incompatible or low-confidence facts degrade safely; they never produce a fake precise recommendation.
- An old runtime/session/revision/attempt/view generation can never revive old Panel-current state, Voice or Overlay.
- Third-party guide sources receive only an explicitly enabled static Pack-download request; they never receive a current GameSnapshot, account/match identifier, user question, local query, recommendation result or telemetry-derived identifier, and no guide-source request starts during a match.

`Disabled` means the corresponding call path does not exist in the active version; it is not merely a hidden UI switch.

## 2. Estimation model

| Term | Meaning |
|---|---|
| Net person-day | Six focused engineering hours by one experienced developer; excludes waiting for accounts, platform review, target-game availability and user review. |
| Calendar range | One developer working near full-time, including target-machine setup, custom-game verification, diagnosis and a small review buffer. |
| External calendar risk | TTS account/endpoint availability, content-source authorization/license confirmation, platform approval and unavailable game-test windows are gate pauses, not deterministic calendar commitments or engineering person-days. |
| Gate failure | A failed external compatibility gate pauses the next feature version; it does not authorize a quick workaround that violates the approved design. |

The estimates assume a supported Windows target machine, an available TFT custom-game test environment, and no Sidecar pivot. If work is limited to two or three focused hours per day, calendar time should be multiplied by approximately 1.8–2.5.

## 3. Dependency flow

```text
v0.0.1 Runtime Compatibility PoC
          │ pass
          ▼
v0.1.0 Panel-first Local Coach
          │ pass
          ▼
v0.2.0 Safe High-confidence Overlay
          │ pass
          ▼
v0.3.0 First Self-use Release (optional single-chain Voice)
          │ stable
          ▼
v0.4.0 Single-source Local Knowledge Maintenance
```

No arrow grants permission to pull in a later version early. A version may use only the capabilities explicitly listed in its own scope.

## 4. Version roadmap

### v0.0.1 — Runtime Compatibility PoC

**Purpose:** Prove that the approved Overwolf transport and storage assumptions are actually available on the target machine before any game-coach feature is built.

**Scope:**

- Verify the private D-drive root, final-volume/path validation, and `MemoryOnlyDegraded` behavior with no C-drive fallback.
- Verify Overwolf Background Bridge plus loopback `/ingest` and `/render` channels, Origin/CSP/manifest constraints, pairing material, minimum semantic messages, `Show` / `HideAll`, and real Renderer receipts.
- In a custom game, verify that supported GEP callbacks and `getInfo()` can establish only the minimum match/session/round/freshness/gap facts needed by later versions.
- Keep raw GEP payloads in memory; persist only bounded, sanitized PoC status under the private D-drive root.

**Explicitly out of scope:** Strategy, Panel advice, board positioning, TTS, external Provider credentials, DataAgent, RAG, LLM, retry/resume sophistication, Sidecar implementation and long-term recovery.

**Go / no-go evidence:**

1. A target-machine run proves that both channels exchange the approved minimum messages and that a Renderer reconstruction or disconnect causes `HideAll` before any later render.
2. A supported GEP snapshot can produce reliable session/round/freshness capability facts, or the missing facts are explicitly disabled by CapabilityMatrix.
3. D-drive failure produces only memory-safe degradation and no plugin-controlled C-drive/AppData/Temp writes.

**Failure rule:** Do not replace a failed loopback PoC with HTTP polling, file polling or SQLite polling. A Sidecar + Named Pipe investigation is a separate re-review, estimated at an additional 5–10 net person-days.

**Estimate:** 6–10 net person-days; 2–3 calendar weeks.

### v0.1.0 — Panel-first Local Coach

**Purpose:** Deliver the first independently useful product loop without requiring Overlay or Voice.

**Scope:**

- Read-only GEP normalization, minimum session/round/freshness/gap handling and capability gates.
- A version-frozen local RulesPack plus a deliberately limited structured CompPack, loaded before a DecisionWindow and exposed through an in-memory `KnowledgeFactView`.
- A limited deterministic strategy set with only facts actually supported by the PoC; unsupported facts yield `Unknown` or no advice.
- `DecisionSnapshot` → local `render-tft-companion-advice` FastPath template layer → `AdviceCoordinator` → PanelProjection.
- Current/expired/unknown visual state in the Panel; session changes, gaps and supersedes remove the old advice from current state.
- Small synthetic regression set for stale Advice, gap degradation, no network in FastPath, no game input and D-drive failure.

**Explicitly out of scope:** Precise board Overlay, game Voice, dynamic third-party knowledge download, DataAgent runtime lookup/save, RAG/embedding, RichPath/Grok, detailed evidence ledger, replay platform and automatic update.

**Release gate:** A custom-game smoke run can show a current local advice or an explicit safe degradation, never an unsupported precise conclusion. Disconnect, session change and stale snapshots cannot leave an old Panel conclusion marked current.

**Estimate:** 7–11 net person-days; 2–3 calendar weeks.

### v0.2.0 — Safe High-confidence Overlay

**Purpose:** Add visual board guidance only when it is safer than leaving the advice in Panel / Edge Dock form.

**Scope:**

- A permanently mouse-through Overlay with Panel-only controls.
- A narrow supported window/DPI/scale profile and a `ViewportTransformTracker` that publishes High / Medium / Low confidence.
- Render only existing v0.1.0 Advice; this version does not add strategy categories.
- At High confidence, show the minimal exact board indication allowed by the current advice; at Medium/Low, hide the exact layer and retain Panel / Edge Dock guidance.
- On scale/window/DPI/ROI change, gap, session change, supersede, expiry or Bridge loss: invalidate the old lease and hide the exact layer before any reacquisition.
- Validate `Accepted ≠ OverlayShown ≠ OverlayDelivered` and reject late `viewGeneration`, revision or lease receipts.

**Explicitly out of scope:** Continuous OCR, screenshot/video persistence, full-resolution/skin/monitor compatibility guarantees, automatic spectator-board analysis, complex manual calibration workflows, Voice, LLM and visual replay infrastructure.

**Release gate:** In the declared supported display configuration, the Overlay never receives mouse input; low confidence or a changed view generation never leaves a board-cell arrow visible.

**Estimate:** 8–13 net person-days; 2–4 calendar weeks.

**Safe fallback:** If this gate is not passed, the product remains a valid Panel / Edge Dock coach. Do not add OCR, input simulation or a risky calibration shortcut to force exact Overlay support.

### v0.3.0 — First Self-use Release

**Purpose:** Complete the §20.1 first-use loop by adding an optional, isolated game Voice channel and a focused stability qualification pass.

**Scope:**

- Preserve v0.1.0 Panel and v0.2.0 safe Overlay as independently usable channels.
- One explicitly configured `IGameCoachTtsProvider`; one Active Voice and at most one Pending Voice; no speculative prefetch.
- When Voice is enabled, TTS sends only a verified short `VoiceSegment` and bounded configuration; it never receives a user question, identity, raw game fact, QQ content or diagnostic payload.
- Voice obeys session/round/scope/revision/deadline checks, expires instead of playing late, and never falls back to QQ SpeechService.
- `Mute`, `SkipCurrentAdvice` and the master Voice-off state take final priority without changing Panel/Overlay state.
- A small release qualification suite: stale callback rejection, gap/supersede invalidation, D-drive write failure, no C-drive fallback, no game input, guide-source zero-upload, TTS allow-list proof, High-only Overlay, and Voice failure not blocking other channels.
- First custom-game self-use smoke record using only sanitized state/results.

**Explicitly out of scope:** Multiple Providers, hedging, provider fallback, cost model, advanced voice timing profiles, device migration/recovery, RichPath/Grok, RAG, query-level evidence, complete replay/fault laboratory, Sidecar and maintenance UI.

**Release gate:** The product is usable with Voice fully Disabled. If Voice is configured, its timeout, cancellation, session change and Mute/Skip paths cannot play an obsolete segment or block Panel/Overlay.

**Estimate:** 8–14 net person-days; 2–4 calendar weeks. Third-party TTS account approval, billing or endpoint availability is external waiting time and is not counted as engineering effort.

### v0.4.0 — Single-source Local Knowledge Maintenance

**Purpose:** Add one authorized local knowledge-source maintenance loop without putting DataAgent, network activity or a general RAG system in the live decision path.

**Scope:**

- One authorized source or manual import, allowed only after startup, in the lobby or by explicit user refresh; manual offline import remains the privacy-preserving fallback.
- A source request's URL, body, header, cookie, telemetry, tag and request timing never carry current game state, identity, local query or recommendation result; no source refresh starts during a match.
- `TftDataAgent` performs typed local save/validation/publish operations and publishes only a verified next `knowledgeSnapshotId` for future DecisionWindows.
- Atomic `staging → validation → publish / rejected` handling, with Pack/Manifest/hash/patch/license validation.
- Minimal persistent maintenance evidence: Pack identity, Manifest/hash, patch, validation result/reason, published/rejected state and active snapshot identity.
- Source deletion clears local raw/normalized/index content within the D-drive policy and retains only the minimum allowed tombstone metadata.

**Explicitly out of scope:** Multiple sources, online current-game queries, automatic mid-game refresh, embedding/rerank/free-text RAG, RichPath retrieval, `PinnedEvidenceBundle`, hash-chain/DAG, projection rebuild/compaction, maintenance dashboard and LLM enablement.

**Release gate:** A new pack cannot alter an active DecisionSnapshot; incomplete, incompatible or unlicensed content never becomes active; D-drive failure never claims successful publication or writes outside the private root. If source authorization is unavailable, only the manual offline-import path may be accepted; unlicensed scraping is never a substitute.

**Estimate:** 6–10 net person-days; 2–3 calendar weeks. Source authorization, license confirmation, source-account creation or content-delivery waiting is an external calendar risk and is not counted as engineering effort; it may pause this version without expanding its implementation scope.

## 5. Time summary

| Target | Included versions | Net person-days | Full-time single-developer calendar range |
|---|---|---:|---:|
| Technical go/no-go | v0.0.1 | 6–10 | 2–3 weeks |
| First useful Panel coach | v0.0.1 + v0.1.0 | 13–21 | 4–6 weeks |
| Visual local coach | through v0.2.0 | 21–34 | 6–10 weeks |
| First self-use release | through v0.3.0 | 29–48 | 8–14 weeks |
| Local knowledge maintenance | through v0.4.0 | 35–58 | 10–17 weeks |

The table is the sequential sum of the declared per-version engineering calendar ranges. Its upper bound includes normal compatibility diagnosis, display/DPI variance, target-game retesting and review buffer within the approved route. External gate waits—such as TTS account availability or content-source authorization—are intentionally not converted into a false fixed date; a failed v0.0.1 transport gate or a change to the approved read-only/privacy boundaries requires a new roadmap review.

## 6. Deferred candidate: v0.5.0 and later

No date is committed for v0.5.0. RichPath/Grok, remote LLM, advanced RAG, multiple knowledge sources, advanced evidence ledger, full replay platform, multi-Provider Voice, performance laboratory, Sidecar/Named Pipe and maintenance/export systems each require their own user-approved PoC and revised privacy/cost estimate. They are not an automatic reward for completing v0.4.0.

## 7. Detailed planning gate

After the user confirms this roadmap, the next planning artifact is a standalone detailed implementation plan for **v0.0.1 only**. It must enumerate exact files, tests, target-machine proof commands and failure exits. It must not pre-create v0.1.0–v0.4.0 code, services, schemas or credentials.
