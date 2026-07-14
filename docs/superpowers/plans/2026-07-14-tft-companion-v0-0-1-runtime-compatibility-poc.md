# TFT Companion v0.0.1 Runtime Compatibility PoC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 在目标 Windows PC 的自定义局中，证明独立、只读的 TFT Companion Host 可以安全地使用 Overwolf Background Bridge 的两条 loopback WebSocket 通道、最小 GEP 事实和真实 Renderer 回执，同时在 D 盘不可用时绝不由插件回退写入 C 盘、AppData 或 Temp。

**Architecture:** 新建完全独立的 D:\FOXD\tft-companion 工程，不引用 AlifePath、WebBridge、QQ、SpeechService、DataAgent 或现有桌宠运行时。一个 .NET 9 Host 只监听 127.0.0.1，Background Bridge 分别连接 /ingest 和 /render；Bridge 仅在内存中把 GEP 转为最小语义事实，Overlay Renderer 只显示无业务文本的 PoC 标记并报告实际 Show/Hidden 回执。Host 只持有有界的 session、round、freshness、gap、connection epoch 与 render lease 状态；唯一持久化物是经过白名单序列化、大小受限的 PoC 状态文件。

**Tech Stack:** .NET SDK 9.0.314、ASP.NET Core/Kestrel、System.Net.WebSockets、System.Text.Json、NUnit 4、Node 内置 test runner、Overwolf Native Runtime 的本地 HTML/ES module bundle、Windows Kernel32 final-path APIs。

---

## 0. Frozen scope, prerequisites, and non-negotiable decisions

### 0.1 What this version proves

This plan implements only these outcomes:

1. The only plugin-controlled persistent root is D:\AlifeData\TFTCompanion.
2. A root that resolves outside the expected D volume, a D-drive access failure, or a status-file write failure enters a safe in-memory storage health state. It never chooses another root.
3. Host accepts two independent WebSocket links on 127.0.0.1 only:

   ~~~text
   /ingest : Background Bridge -> Host
   /render : Background Bridge <-> Host, with Renderer receipts forwarded by Background Bridge
   ~~~

4. Each channel requires a typed Hello/Welcome handshake, matching Origin, short local pairing proof, route/role match, protocol/schema version, message-kind allowlist, byte limit, and rate limit.
5. The minimal semantic GEP flow establishes only:

   ~~~text
   match/session observed
   round observed
   freshness
   gap / resync
   GEP capability state
   ~~~

6. A new or reconnected render channel receives HideAll as its first Host command. A visible marker cannot return until a current snapshot is received after that channel's new lease and the Renderer has acknowledged Hidden.
7. The target-machine run records sanitized status and a human-readable go/no-go result without raw GEP JSON, screenshots, player identifiers, real names, tokens, origins, URLs, or console payload dumps.

### 0.2 Explicitly not implemented

Do not add any of the following while executing this plan:

~~~text
strategy, advice, Panel advice, RulesPack, CompPack, board location,
hex coordinates, arrows, OCR, screenshots, window capture, TTS,
voice queues, LLM, RAG, external provider credentials, DataAgent,
SQLite business storage, HTTP polling, file polling, SQLite polling,
full resend/resume, Sidecar, Named Pipe, input simulation, ADB,
memory reading, injection, packet capture, game focus, or game controls.
~~~

The only visual artifact is a fixed, non-interactive 8 by 8 pixel PoC marker. It is not an advice, position suggestion, or user interaction surface.

### 0.3 Deliberate PoC constants

The following constants are intentionally small and must be defined once in ProtocolConstants:

| Name | Value | Reason |
|---|---:|---|
| GameId | 21570 | TFT Overwolf ID used by the approved design |
| ProtocolVersion | 1 | First frozen wire contract |
| SchemaVersion | 1 | First frozen payload schema |
| Loopback address | 127.0.0.1 | IPv4 loopback only; no 0.0.0.0 and no IPv6 in this PoC |
| Default port | 32173 | Deterministic development port; collision is a clear startup failure |
| Maximum text frame | 65536 bytes | Enough for a minimal snapshot but excludes raw payload dumping |
| Ingress mailbox capacity | 128 envelopes | Bounded in-memory handling; overflow produces resync |
| Render mailbox capacity | 16 commands | Separate from ingress so HideAll is not queued behind a burst |
| Freshness TTL | 3 seconds | Conservative PoC freshness indicator, measured by Host time only |
| Status document maximum | 8192 UTF-8 bytes | Bounded sanitized status snapshot |
| Status write interval | 1 second | One overwriteable file, not an event log |
| Pairing token entropy | 32 random bytes | Minimum development pairing material |

The actual Overwolf Origin and exact supported GEP feature list are not guessed. Both are required fields in an ignored, developer-created local configuration file. If either is absent, the Bridge refuses to start and the Host refuses the affected handshake. This is a fail-closed compatibility gate, not a future feature placeholder.

### 0.4 External gates before target-machine execution

The implementer may write and unit-test the local code without an Overwolf account. The actual target-machine manual validation requires that the user already has legitimate access to the Overwolf development/runtime environment. Do not create an account, submit an app, enable monetization, or request a platform exception as part of this plan.

Immediately before building the Overwolf manifest, check the current official pages below and record the runtime version used in the result document:

~~~text
https://dev.overwolf.com/ow-native/reference/manifest/manifest-json
https://dev.overwolf.com/ow-native/reference/manifest/validate-your-manifest-json
https://dev.overwolf.com/ow-native/live-game-data-gep/supported-games/teamfight-tactics
https://dev.overwolf.com/ow-native/live-game-data-gep/live-game-data-gep-intro
https://dev.overwolf.com/ow-native/reference/web/websocket
~~~

The official GEP overview currently documents setRequiredFeatures(), onNewEvents, getInfo(), and onInfoUpdates2. The exact manifest schema, allowed Origin behavior, CSP requirement, feature strings, and window properties must be validated against the current official documents before the target-machine step. If the platform rejects loopback WebSocket, Origin, CSP, or manifest behavior, do not substitute another transport.

### 0.5 Fixed file map

All paths below are relative to D:\FOXD unless an absolute data root is stated.

| Path | Responsibility |
|---|---|
| tft-companion/global.json | Pins the known .NET 9 SDK |
| tft-companion/Directory.Build.props | D-drive workspace build/test output and compiler policy |
| tft-companion/TftCompanion.Poc.slnx | Only the three PoC projects |
| tft-companion/src/TftCompanion.Poc.Core/ | Storage policy, wire contract, semantic state, render lease |
| tft-companion/src/TftCompanion.Poc.Host/ | Kestrel host, loopback boundary, two independent connections |
| tft-companion/tests/TftCompanion.Poc.Tests/ | NUnit unit and loopback integration tests |
| tft-companion/overwolf/tft-companion-poc/ | Native Background Bridge and renderer-only bundle |
| tft-companion/tests/overwolf/ | Dependency-free Node tests and static boundary checks |
| docs/tft-companion/v0.0.1-target-machine-runbook.md | Exact manual target-machine procedure |
| docs/tft-companion/v0.0.1-go-no-go-record.md | Sanitized evidence record format and decision rule |

The Host is the only process allowed to call StorageRootPolicy. The Bridge and Renderer do not use overwolf.io, localStorage, IndexedDB, fetch, XMLHttpRequest, WebSocket URLs containing a token, or any file API.

## 1. Execution-wide rules

1. Use C:\Users\hu shu\.dotnet\dotnet.exe for every build and test command. The system-wide dotnet is .NET 8 and must not be used for this .NET 9 solution.
2. Do not place the new project below alife-service. It must remain D:\FOXD\tft-companion.
3. Do not import, project-reference, instantiate, or scan for a fallback to AlifePath, ConfigurationSystem, StorageSystem, WebBridgeService, DataAgent, QChat, SpeechService, VisionService, PetProcess, OneBot, desktop-control, or any game input API.
4. Do not use an unbounded queue, raw JSON file, request log, browser console payload dump, screenshot, video, player ID, player name, account ID, or pseudo match ID in any persistence or diagnostic text.
5. Do not expose an HTTP health endpoint in v0.0.1. The two WebSocket routes are the complete network surface.
6. No task below is authorization to start the Host, load an Overwolf app, install a dependency, create an account, change a firewall rule, or execute a custom game. Those are future execution actions and must be approved at that time.

## 2. Task 1: Create the isolated, reproducible PoC solution shell

**Files:**

- Create: tft-companion/global.json
- Create: tft-companion/Directory.Build.props
- Create: tft-companion/.gitignore
- Create: tft-companion/TftCompanion.Poc.slnx
- Create: tft-companion/src/TftCompanion.Poc.Core/TftCompanion.Poc.Core.csproj
- Create: tft-companion/src/TftCompanion.Poc.Host/TftCompanion.Poc.Host.csproj
- Create: tft-companion/tests/TftCompanion.Poc.Tests/TftCompanion.Poc.Tests.csproj
- Create: tft-companion/tests/TftCompanion.Poc.Tests/ProjectBoundaryTests.cs

- [ ] **Step 1: Create the solution and project configuration before adding any runtime code**

Create the files with exactly these contents.

~~~json
{
  "sdk": {
    "version": "9.0.314",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
~~~

~~~xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <Deterministic>true</Deterministic>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)artifacts\obj\$(MSBuildProjectName)\</BaseIntermediateOutputPath>
    <BaseOutputPath>$(MSBuildThisFileDirectory)artifacts\bin\$(MSBuildProjectName)\</BaseOutputPath>
  </PropertyGroup>
</Project>
~~~

~~~text
/artifacts/
/overwolf/tft-companion-poc/dev-settings.local.js
/overwolf/tft-companion-poc/dev-settings.local.json
*.user
~~~

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>TftCompanion.Poc.Core</AssemblyName>
    <RootNamespace>TftCompanion.Poc.Core</RootNamespace>
  </PropertyGroup>
</Project>
~~~

~~~xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <AssemblyName>TftCompanion.Poc.Host</AssemblyName>
    <RootNamespace>TftCompanion.Poc.Host</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TftCompanion.Poc.Core\TftCompanion.Poc.Core.csproj" />
  </ItemGroup>
</Project>
~~~

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TftCompanion.Poc.Core\TftCompanion.Poc.Core.csproj" />
    <ProjectReference Include="..\..\src\TftCompanion.Poc.Host\TftCompanion.Poc.Host.csproj" />
  </ItemGroup>
</Project>
~~~

~~~xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/TftCompanion.Poc.Core/TftCompanion.Poc.Core.csproj" />
    <Project Path="src/TftCompanion.Poc.Host/TftCompanion.Poc.Host.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/TftCompanion.Poc.Tests/TftCompanion.Poc.Tests.csproj" />
  </Folder>
</Solution>
~~~

- [ ] **Step 2: Write the first failing project-isolation test**

Put this test in ProjectBoundaryTests.cs before production source is added.

~~~csharp
using NUnit.Framework;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class ProjectBoundaryTests
{
    [Test]
    public void poc_assemblies_are_present_and_do_not_reference_alife()
    {
        Type coreType = Type.GetType(
            "TftCompanion.Poc.Core.Protocol.ProtocolConstants, TftCompanion.Poc.Core",
            throwOnError: false)!;

        Assert.That(coreType, Is.Not.Null, "ProtocolConstants has not been created.");

        string[] references = coreType.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.That(references, Does.Not.Contain("Alife.Platform"));
        Assert.That(references, Does.Not.Contain("Alife.Function.WebBridge"));
        Assert.That(references, Does.Not.Contain("Alife.Function.Speech"));
        Assert.That(references, Does.Not.Contain("Alife.Function.DataAgent"));
    }
}
~~~

- [ ] **Step 3: Run the test and confirm the intended red state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter FullyQualifiedName~ProjectBoundaryTests
~~~

Expected: build failure or one failing test explaining that TftCompanion.Poc.Core.Protocol.ProtocolConstants has not been created. Do not add an Alife project reference to make the test compile.

- [ ] **Step 4: Add only the minimal ProtocolConstants production source**

Create src/TftCompanion.Poc.Core/Protocol/ProtocolConstants.cs.

~~~csharp
namespace TftCompanion.Poc.Core.Protocol;

public static class ProtocolConstants
{
    public const int GameId = 21570;
    public const int ProtocolVersion = 1;
    public const int SchemaVersion = 1;
    public const int DefaultPort = 32173;
    public const int MaximumTextFrameBytes = 65_536;
    public const int IngressMailboxCapacity = 128;
    public const int RenderMailboxCapacity = 16;
    public const int FreshnessTtlSeconds = 3;
    public const int MaximumStatusDocumentBytes = 8_192;
    public const int StatusWriteIntervalSeconds = 1;
    public const string LoopbackAddress = "127.0.0.1";
    public const string CanonicalStorageRoot = @"D:\AlifeData\TFTCompanion";
}
~~~

- [ ] **Step 5: Restore, build, and rerun the isolation test**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' restore .\TftCompanion.Poc.slnx
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build .\TftCompanion.Poc.slnx --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter FullyQualifiedName~ProjectBoundaryTests
~~~

Expected: build succeeds and ProjectBoundaryTests passes. Build artifacts remain below D:\FOXD\tft-companion\artifacts.

- [ ] **Step 6: Commit the isolated shell**

~~~powershell
git add tft-companion docs/superpowers/plans/2026-07-14-tft-companion-v0-0-1-runtime-compatibility-poc.md
git commit -m "chore(tft-poc): create isolated runtime compatibility solution"
~~~

## 3. Task 2: Implement the D-drive-only StorageRootPolicy and sanitized PoC status store

**Files:**

- Create: tft-companion/src/TftCompanion.Poc.Core/Storage/StorageHealth.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Storage/StorageContracts.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Storage/WindowsStorageFileSystem.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Storage/StorageRootPolicy.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Storage/SanitizedPocStatus.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/TestSupport/FakeStorageFileSystem.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/StorageRootPolicyTests.cs

### Required storage contract

The policy has no public method accepting an arbitrary relative path. v0.0.1 needs exactly one optional file called poc-status.json directly under the canonical root. A generic file service would unnecessarily enlarge the path-escape attack surface.

~~~csharp
public enum StorageHealth
{
    Available,
    MemoryOnlyDegraded,
    PersistUnavailable,
    IntegrityDegraded
}

public sealed record FinalPath(string VolumeGuid, string CanonicalPath);

public interface IStorageFileSystem
{
    StorageRootProbe ProbeAndEnsureCanonicalRoot();
    bool TryWriteCanonicalRootFile(
        string fileName,
        ReadOnlyMemory<byte> utf8,
        out string failureCode);
}

public sealed record StorageRootProbe(
    bool Success,
    FinalPath? ResolvedRoot,
    StorageHealth FailureHealth,
    string FailureCode);
~~~

WindowsStorageFileSystem must perform this exact conservative algorithm:

1. Open D:\ as a directory handle with FILE_FLAG_BACKUP_SEMANTICS and obtain its volume GUID.
2. For D:\AlifeData and D:\AlifeData\TFTCompanion, inspect every existing segment before descending. If an existing segment has FileAttributes.ReparsePoint, reject it rather than following it.
3. Only after its direct parent has been proven to be a normal directory on D:\ may the next segment be created.
4. Open the final root normally, obtain GetFinalPathNameByHandleW and its volume GUID, and require that it is on the same D volume and equals the normalized canonical root.
5. Before overwriting poc-status.json, reject an existing reparse-point file, resolve its final path, and require it to be exactly one child of the resolved root on the same volume.
6. On every error, return a named failure code and do not attempt a different path.

This deliberately rejects a junction even if it happens to point back into D:\AlifeData\TFTCompanion. The stricter policy keeps the PoC small while still proving final-volume validation and preventing a C-drive escape.

- [ ] **Step 1: Write failing storage tests using a recording fake file system**

Create FakeStorageFileSystem.cs and StorageRootPolicyTests.cs with these test cases. The fake must record every requested write path and every UTF-8 document; it must never touch the real file system.

~~~csharp
using NUnit.Framework;
using TftCompanion.Poc.Core.Storage;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class StorageRootPolicyTests
{
    [Test]
    public void unavailable_d_root_enters_memory_only_without_any_write()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.Unavailable("D_ROOT_UNAVAILABLE");
        StorageRootPolicy policy = new(fileSystem);

        StorageHealth health = policy.Initialize();
        bool persisted = policy.TryPersist(new SanitizedPocStatus(
            RuntimeEpoch: "runtime-1",
            BridgeOnline: false,
            RenderOnline: false,
            MatchObserved: false,
            RoundObserved: false,
            Freshness: "Unknown",
            GapState: "None",
            LastErrorCode: "NONE"));

        Assert.Multiple(() =>
        {
            Assert.That(health, Is.EqualTo(StorageHealth.MemoryOnlyDegraded));
            Assert.That(persisted, Is.False);
            Assert.That(fileSystem.WriteAttempts, Is.Empty);
        });
    }

    [Test]
    public void resolved_root_outside_d_enters_integrity_degraded_without_fallback()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.IntegrityFailure(
            new FinalPath(@"\\?\Volume{c-drive}\", @"\\?\C:\escaped\TFTCompanion"));
        StorageRootPolicy policy = new(fileSystem);

        StorageHealth health = policy.Initialize();
        bool persisted = policy.TryPersist(SanitizedPocStatus.Empty("runtime-2"));

        Assert.Multiple(() =>
        {
            Assert.That(health, Is.EqualTo(StorageHealth.IntegrityDegraded));
            Assert.That(persisted, Is.False);
            Assert.That(fileSystem.WriteAttempts, Is.Empty);
        });
    }

    [Test]
    public void write_failure_after_validation_enters_persist_unavailable_and_never_retries_elsewhere()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.ValidRootThenWriteFailure("STATUS_WRITE_DENIED");
        StorageRootPolicy policy = new(fileSystem);

        Assert.That(policy.Initialize(), Is.EqualTo(StorageHealth.Available));
        Assert.That(policy.TryPersist(SanitizedPocStatus.Empty("runtime-3")), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(policy.Health, Is.EqualTo(StorageHealth.PersistUnavailable));
            Assert.That(fileSystem.WriteAttempts, Has.Count.EqualTo(1));
            Assert.That(fileSystem.WriteAttempts[0], Is.EqualTo("poc-status.json"));
        });
    }

    [Test]
    public void successful_status_is_bounded_and_contains_no_raw_payload_or_identity_fields()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.ValidRoot();
        StorageRootPolicy policy = new(fileSystem);
        SanitizedPocStatus status = new(
            RuntimeEpoch: "runtime-4",
            BridgeOnline: true,
            RenderOnline: true,
            MatchObserved: true,
            RoundObserved: true,
            Freshness: "Fresh",
            GapState: "None",
            LastErrorCode: "NONE");

        Assert.That(policy.Initialize(), Is.EqualTo(StorageHealth.Available));
        Assert.That(policy.TryPersist(status), Is.True);

        string json = fileSystem.LastUtf8Document;
        Assert.Multiple(() =>
        {
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(8_192));
            Assert.That(json, Does.Not.Contain("payload"));
            Assert.That(json, Does.Not.Contain("pseudo_match"));
            Assert.That(json, Does.Not.Contain("summoner"));
            Assert.That(json, Does.Not.Contain("token"));
            Assert.That(json, Does.Not.Contain("origin"));
        });
    }
}
~~~

- [ ] **Step 2: Run the storage tests and confirm the intended red state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter FullyQualifiedName~StorageRootPolicyTests
~~~

Expected: compilation fails because TftCompanion.Poc.Core.Storage types do not exist.

- [ ] **Step 3: Implement the closed storage contract and Windows adapter**

Create the public storage types exactly as follows.

~~~csharp
namespace TftCompanion.Poc.Core.Storage;

public enum StorageHealth
{
    Available,
    MemoryOnlyDegraded,
    PersistUnavailable,
    IntegrityDegraded
}

public sealed record FinalPath(string VolumeGuid, string CanonicalPath);

public sealed record StorageRootProbe(
    bool Success,
    FinalPath? ResolvedRoot,
    StorageHealth FailureHealth,
    string FailureCode);

public interface IStorageFileSystem
{
    StorageRootProbe ProbeAndEnsureCanonicalRoot();

    bool TryWriteCanonicalRootFile(
        string fileName,
        ReadOnlyMemory<byte> utf8,
        out string failureCode);
}
~~~

~~~csharp
using System.Text;
using System.Text.Json;
using TftCompanion.Poc.Core.Protocol;

namespace TftCompanion.Poc.Core.Storage;

public sealed record SanitizedPocStatus(
    string RuntimeEpoch,
    bool BridgeOnline,
    bool RenderOnline,
    bool MatchObserved,
    bool RoundObserved,
    string Freshness,
    string GapState,
    string LastErrorCode)
{
    public static SanitizedPocStatus Empty(string runtimeEpoch) => new(
        runtimeEpoch,
        BridgeOnline: false,
        RenderOnline: false,
        MatchObserved: false,
        RoundObserved: false,
        Freshness: "Unknown",
        GapState: "None",
        LastErrorCode: "NONE");
}

public sealed class StorageRootPolicy
{
    private readonly IStorageFileSystem fileSystem;

    public StorageRootPolicy(IStorageFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public StorageHealth Health { get; private set; } = StorageHealth.MemoryOnlyDegraded;

    public StorageHealth Initialize()
    {
        StorageRootProbe probe = fileSystem.ProbeAndEnsureCanonicalRoot();
        Health = probe.Success ? StorageHealth.Available : probe.FailureHealth;
        return Health;
    }

    public bool TryPersist(SanitizedPocStatus status)
    {
        if (Health != StorageHealth.Available)
            return false;

        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(status);
        if (utf8.Length > ProtocolConstants.MaximumStatusDocumentBytes)
        {
            Health = StorageHealth.PersistUnavailable;
            return false;
        }

        if (fileSystem.TryWriteCanonicalRootFile("poc-status.json", utf8, out _))
            return true;

        Health = StorageHealth.PersistUnavailable;
        return false;
    }
}
~~~

Implement WindowsStorageFileSystem with P/Invoke declarations for CreateFileW, GetFinalPathNameByHandleW, GetVolumeNameForVolumeMountPointW, and a SafeFileHandle. The implementation must contain these non-negotiable checks:

~~~csharp
private static readonly string[] CanonicalSegments =
[
    @"D:\AlifeData",
    @"D:\AlifeData\TFTCompanion"
];

private static bool IsCanonicalDPath(FinalPath finalPath) =>
    finalPath.CanonicalPath.Equals(
        @"\\?\D:\AlifeData\TFTCompanion",
        StringComparison.OrdinalIgnoreCase);

private static bool IsReparsePoint(string path) =>
    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

private static bool IsExpectedStatusFileName(string fileName) =>
    string.Equals(fileName, "poc-status.json", StringComparison.Ordinal);
~~~

Use FILE_FLAG_BACKUP_SEMANTICS when opening a directory. Do not use Path.GetFullPath, a string prefix comparison, Directory.CreateDirectory on an unchecked parent, or a generic relative-path helper as the final authority. The adapter may use Directory.CreateDirectory only after the immediate parent has been opened and proven to be a normal directory on the D volume.

Create FakeStorageFileSystem so that ValidRoot returns a final root of:

~~~text
Volume GUID: \\?\Volume{d-drive}\
Canonical path: \\?\D:\AlifeData\TFTCompanion
~~~

The fake's TryWriteCanonicalRootFile must reject every file name except poc-status.json and record the one permitted attempt.

- [ ] **Step 4: Run storage tests and the full .NET suite**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter FullyQualifiedName~StorageRootPolicyTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore
~~~

Expected: all StorageRootPolicyTests pass; no test creates D:\AlifeData\TFTCompanion or any C-drive path because every test uses FakeStorageFileSystem.

- [ ] **Step 5: Commit the storage boundary**

~~~powershell
git add tft-companion/src/TftCompanion.Poc.Core tft-companion/tests/TftCompanion.Poc.Tests
git commit -m "feat(tft-poc): enforce d-drive-only sanitized storage"
~~~

## 4. Task 3: Freeze the minimum versioned protocol and pairing boundary

**Files:**

- Create: tft-companion/src/TftCompanion.Poc.Core/Protocol/WireContracts.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Protocol/ProtocolCodec.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Protocol/ProtocolValidator.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Security/PairingConfiguration.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Security/HandshakeAuthorizer.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/TestSupport/TestWire.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/ProtocolValidatorTests.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/HandshakeAuthorizerTests.cs

### Required wire model

The Envelope contains only typed semantic payloads. Its payload is never persisted.

~~~csharp
public enum ChannelKind
{
    Ingest,
    Render
}

public enum PeerRole
{
    IngestBridge,
    RenderBridge
}

public enum MessageKind
{
    Hello,
    Welcome,
    GepEvent,
    InfoUpdate,
    StateSnapshot,
    Capability,
    Health,
    Ack,
    GapDetected,
    ResyncRequired,
    OverlayCommand,
    OverlayReceipt,
    Rejected
}

public enum OverlayOperation
{
    ShowPocMarker,
    HideAll
}

public enum OverlayReceiptKind
{
    Accepted,
    OverlayShown,
    Hidden,
    Failed
}

public sealed record WireEnvelope(
    int ProtocolVersion,
    int SchemaVersion,
    MessageKind MessageKind,
    Guid MessageId,
    Guid? CorrelationId,
    Guid? CausationId,
    string PayloadHash,
    string BridgeInstanceId,
    long TransportEpoch,
    string StreamId,
    long StreamSequence,
    JsonElement Payload);

public sealed record HelloPayload(
    PeerRole Role,
    ChannelKind Channel,
    string BridgeInstanceId,
    long TransportEpoch,
    string StreamId,
    long LastAck,
    int GameId,
    string RequestedFeatureHash,
    IReadOnlyList<string> CapabilitySet,
    string PairingProof);

public sealed record WelcomePayload(
    string ServerInstanceId,
    long ConnectionEpoch,
    IReadOnlyList<string> AllowedScopes,
    string ChannelBindingId,
    bool ResumeAccepted,
    bool ResyncRequired,
    long CurrentSessionEpoch);

public sealed record ProtocolValidationResult(bool IsValid, string Code)
{
    public static ProtocolValidationResult Accepted { get; } = new(true, "VALID");

    public static ProtocolValidationResult Reject(string code) => new(false, code);
}

public sealed record PairingConfiguration(
    string AllowedOrigin,
    string PairingToken);

public sealed record HandshakeInput(
    string Origin,
    string RequestedPath,
    bool HasQueryString,
    string RemoteAddress);

public sealed record HandshakeResult(
    bool IsAuthorized,
    string Code,
    IReadOnlyList<string> AllowedScopes)
{
    public static HandshakeResult Reject(string code) => new(false, code, []);
}
~~~

For v0.0.1, the only semantic inbound payloads after Hello are:

~~~csharp
public sealed record PocStateSnapshot(
    bool MatchObserved,
    bool RoundObserved,
    bool IsAuthoritativeSnapshot);

public sealed record PocCapability(
    bool FeatureRegistrationSucceeded,
    bool GetInfoSucceeded,
    bool NewEventsSeen,
    bool InfoUpdatesSeen);

public sealed record OverlayReceipt(
    OverlayReceiptKind ReceiptKind,
    string RuntimeEpoch,
    string ServerInstanceId,
    long ConnectionEpoch,
    Guid SessionId,
    long SessionEpoch,
    string RenderLeaseId,
    long RenderGeneration,
    Guid OperationId);
~~~

No payload type contains pseudoMatchId, a player name, a player ID, raw events, a screenshot, a board, an item, a unit, a token, a URL, an HTML fragment, JavaScript, or user input.

TestWire.cs must be the sole shared synthetic-message helper. It creates JsonElement values with JsonSerializer.SerializeToElement and sets PayloadHash through ProtocolCodec.ComputePayloadHash. Its public methods are exactly:

~~~csharp
public static class TestWire
{
    public static WireEnvelope Hello(
        PeerRole role,
        ChannelKind channel,
        string pairingProof,
        long sequence = 0);

    public static WireEnvelope StateSnapshot(
        long sequence,
        bool matchObserved,
        bool roundObserved,
        bool isAuthoritativeSnapshot = true);

    public static WireEnvelope InfoUpdate(long sequence);
}
~~~

Every helper uses BridgeInstanceId bridge-test, TransportEpoch 1, and a stream ID derived from its ChannelKind. The helper never accepts or produces a pseudo match identifier.

Implement TestWire with this complete synthetic-only body:

~~~csharp
using System.Text.Json;
using TftCompanion.Poc.Core.Protocol;

namespace TftCompanion.Poc.Tests.TestSupport;

public static class TestWire
{
    public static WireEnvelope Hello(
        PeerRole role,
        ChannelKind channel,
        string pairingProof,
        long sequence = 0)
    {
        HelloPayload payload = new(
            role,
            channel,
            BridgeInstanceId: "bridge-test",
            TransportEpoch: 1,
            StreamId: StreamIdFor(channel),
            LastAck: 0,
            GameId: ProtocolConstants.GameId,
            RequestedFeatureHash: "test-feature-hash",
            CapabilitySet: [],
            PairingProof: pairingProof);

        return Build(MessageKind.Hello, channel, sequence, payload);
    }

    public static WireEnvelope StateSnapshot(
        long sequence,
        bool matchObserved,
        bool roundObserved,
        bool isAuthoritativeSnapshot = true) =>
        Build(
            MessageKind.StateSnapshot,
            ChannelKind.Ingest,
            sequence,
            new PocStateSnapshot(
                matchObserved,
                roundObserved,
                isAuthoritativeSnapshot));

    public static WireEnvelope InfoUpdate(long sequence) =>
        Build(
            MessageKind.InfoUpdate,
            ChannelKind.Ingest,
            sequence,
            new PocStateSnapshot(
                MatchObserved: false,
                RoundObserved: false,
                IsAuthoritativeSnapshot: false));

    private static WireEnvelope Build<TPayload>(
        MessageKind kind,
        ChannelKind channel,
        long sequence,
        TPayload payload)
    {
        JsonElement element = JsonSerializer.SerializeToElement(payload);
        return new WireEnvelope(
            ProtocolConstants.ProtocolVersion,
            ProtocolConstants.SchemaVersion,
            kind,
            Guid.NewGuid(),
            CorrelationId: null,
            CausationId: null,
            ProtocolCodec.ComputePayloadHash(element),
            BridgeInstanceId: "bridge-test",
            TransportEpoch: 1,
            StreamId: StreamIdFor(channel),
            StreamSequence: sequence,
            Payload: element);
    }

    private static string StreamIdFor(ChannelKind channel) =>
        channel == ChannelKind.Ingest ? "ingest-test" : "render-test";
}
~~~

- [ ] **Step 1: Write failing protocol and handshake tests**

Create ProtocolValidatorTests.cs with a helper that serializes a valid Hello envelope and then mutates exactly one property per test.

~~~csharp
[Test]
public void valid_hello_for_ingest_is_accepted()
{
    WireEnvelope envelope = TestWire.Hello(
        role: PeerRole.IngestBridge,
        channel: ChannelKind.Ingest,
        pairingProof: "pairing-token");

    ProtocolValidationResult result = ProtocolValidator.Validate(envelope);

    Assert.That(result.IsValid, Is.True);
}

[TestCase(0)]
[TestCase(2)]
public void unsupported_protocol_is_rejected(int version)
{
    WireEnvelope envelope = TestWire.Hello(
        role: PeerRole.IngestBridge,
        channel: ChannelKind.Ingest,
        pairingProof: "pairing-token") with { ProtocolVersion = version };

    ProtocolValidationResult result = ProtocolValidator.Validate(envelope);

    Assert.That(result.Code, Is.EqualTo("UNSUPPORTED_PROTOCOL"));
}

[Test]
public void payload_hash_mismatch_is_rejected()
{
    WireEnvelope envelope = TestWire.Hello(
        role: PeerRole.IngestBridge,
        channel: ChannelKind.Ingest,
        pairingProof: "pairing-token") with { PayloadHash = "00" };

    ProtocolValidationResult result = ProtocolValidator.Validate(envelope);

    Assert.That(result.Code, Is.EqualTo("PAYLOAD_HASH_MISMATCH"));
}
~~~

Create HandshakeAuthorizerTests.cs with these four boundary cases.

~~~csharp
[Test]
public void correct_origin_route_role_and_pairing_proof_are_authorized()
{
    HandshakeAuthorizer authorizer = new(new PairingConfiguration(
        AllowedOrigin: "overwolf-extension://poc-origin",
        PairingToken: "pairing-token"));

    HandshakeResult result = authorizer.Authorize(
        new HandshakeInput(
            Origin: "overwolf-extension://poc-origin",
            RequestedPath: "/ingest",
            HasQueryString: false,
            RemoteAddress: "127.0.0.1"),
        TestWire.Hello(PeerRole.IngestBridge, ChannelKind.Ingest, "pairing-token"));

    Assert.That(result.Code, Is.EqualTo("AUTHORIZED"));
}

[TestCase("https://ordinary-web-page")]
[TestCase("")]
public void unexpected_origin_is_rejected(string origin)
{
    HandshakeAuthorizer authorizer = new(new PairingConfiguration(
        AllowedOrigin: "overwolf-extension://poc-origin",
        PairingToken: "pairing-token"));

    HandshakeResult result = authorizer.Authorize(
        new HandshakeInput(origin, "/ingest", false, "127.0.0.1"),
        TestWire.Hello(PeerRole.IngestBridge, ChannelKind.Ingest, "pairing-token"));

    Assert.That(result.Code, Is.EqualTo("ORIGIN_REJECTED"));
}

[Test]
public void token_in_a_query_string_is_rejected_even_when_the_proof_matches()
{
    HandshakeAuthorizer authorizer = new(new PairingConfiguration(
        AllowedOrigin: "overwolf-extension://poc-origin",
        PairingToken: "pairing-token"));

    HandshakeResult result = authorizer.Authorize(
        new HandshakeInput("overwolf-extension://poc-origin", "/ingest", true, "127.0.0.1"),
        TestWire.Hello(PeerRole.IngestBridge, ChannelKind.Ingest, "pairing-token"));

    Assert.That(result.Code, Is.EqualTo("QUERY_STRING_REJECTED"));
}

[Test]
public void render_role_cannot_bind_to_ingest_route()
{
    HandshakeAuthorizer authorizer = new(new PairingConfiguration(
        AllowedOrigin: "overwolf-extension://poc-origin",
        PairingToken: "pairing-token"));

    HandshakeResult result = authorizer.Authorize(
        new HandshakeInput("overwolf-extension://poc-origin", "/ingest", false, "127.0.0.1"),
        TestWire.Hello(PeerRole.RenderBridge, ChannelKind.Render, "pairing-token"));

    Assert.That(result.Code, Is.EqualTo("ROLE_ROUTE_REJECTED"));
}

[Test]
public void wrong_pairing_proof_is_rejected_without_echoing_the_proof()
{
    HandshakeAuthorizer authorizer = new(new PairingConfiguration(
        AllowedOrigin: "overwolf-extension://poc-origin",
        PairingToken: "pairing-token"));

    HandshakeResult result = authorizer.Authorize(
        new HandshakeInput("overwolf-extension://poc-origin", "/ingest", false, "127.0.0.1"),
        TestWire.Hello(PeerRole.IngestBridge, ChannelKind.Ingest, "different-token"));

    Assert.Multiple(() =>
    {
        Assert.That(result.Code, Is.EqualTo("PAIRING_REJECTED"));
        Assert.That(result.Code, Does.Not.Contain("different-token"));
    });
}
~~~

- [ ] **Step 2: Run the new tests and confirm the intended red state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter "FullyQualifiedName~ProtocolValidatorTests|FullyQualifiedName~HandshakeAuthorizerTests"
~~~

Expected: compilation fails because the wire and security types do not exist.

- [ ] **Step 3: Implement strict codec, route/role rules, and constant-time pairing comparison**

Implement ProtocolValidator with these rules:

1. ProtocolVersion and SchemaVersion must equal ProtocolConstants values.
2. MessageKind must be a defined enum value.
3. BridgeInstanceId and StreamId must be nonempty and no longer than 128 characters.
4. TransportEpoch and StreamSequence must be non-negative.
5. PayloadHash must be the lowercase SHA-256 hex digest of Payload.GetRawText() encoded as UTF-8.
6. A parse failure, unknown property, binary frame, unknown message kind, malformed GUID, or hash mismatch produces a named rejection code; it never throws into the connection loop.

Use a JsonSerializerOptions instance with PropertyNameCaseInsensitive set to false and UnmappedMemberHandling set to JsonUnmappedMemberHandling.Disallow. No polymorphic object payloads are permitted.

ProtocolCodec must expose these exact members, so the Host and TestWire use one hash/parse implementation:

~~~csharp
public static class ProtocolCodec
{
    public static string ComputePayloadHash(JsonElement payload);

    public static bool TryDeserialize(
        ReadOnlySpan<byte> utf8,
        out WireEnvelope? envelope,
        out string rejectionCode);

    public static byte[] Serialize(WireEnvelope envelope);
}
~~~

ComputePayloadHash uses SHA256.HashData over Encoding.UTF8.GetBytes(payload.GetRawText()) and Convert.ToHexString(...).ToLowerInvariant(). TryDeserialize must return false with MALFORMED_ENVELOPE rather than throw if JSON is malformed, an unknown property is present, a required property is missing, or a string cannot bind to the closed enum/Guid contract.

Implement HandshakeAuthorizer exactly around this route map:

~~~csharp
private static bool IsRoleAllowedOnRoute(PeerRole role, ChannelKind channel) =>
    (role, channel) switch
    {
        (PeerRole.IngestBridge, ChannelKind.Ingest) => true,
        (PeerRole.RenderBridge, ChannelKind.Render) => true,
        _ => false
    };

private static string[] AllowedScopes(PeerRole role) =>
    role switch
    {
        PeerRole.IngestBridge =>
        [
            "PublishGepEvent",
            "PublishSnapshot",
            "ReportHealth"
        ],
        PeerRole.RenderBridge =>
        [
            "ShowDeclarativeOverlay",
            "UpdateDeclarativeOverlay",
            "HideDeclarativeOverlay",
            "ReportReceipt"
        ],
        _ => []
    };
~~~

Compare UTF-8 pairing proof bytes with CryptographicOperations.FixedTimeEquals. The input token is held only in PairingConfiguration and must not be included in a record ToString(), exception, logger call, response payload, URL, status record, or diagnostic file.

HandshakeAuthorizer has this exact public method:

~~~csharp
public HandshakeResult Authorize(
    HandshakeInput input,
    WireEnvelope helloEnvelope);
~~~

It first calls ProtocolValidator.Validate, requires MessageKind.Hello, deserializes HelloPayload with the same closed JSON options, then performs every route, Origin, loopback, query, game ID, version, and constant-time proof check. A successful result has IsAuthorized true, Code AUTHORIZED, and the route-specific scopes shown later in this task.

HandshakeInput must reject:

~~~text
remote address other than 127.0.0.1
missing or different Origin
any query string
invalid game id
wrong role/channel pair
invalid protocol/schema
wrong pairing proof
~~~

The Host will validate the TCP remote address. The pure authorizer still receives it as a string so the test can prove the same policy.

- [ ] **Step 4: Run focused and full tests**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter "FullyQualifiedName~ProtocolValidatorTests|FullyQualifiedName~HandshakeAuthorizerTests"
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore
~~~

Expected: focused tests and the earlier storage tests pass. The tests contain synthetic pairing text only; no real local token is created.

- [ ] **Step 5: Commit the frozen wire/security boundary**

~~~powershell
git add tft-companion/src/TftCompanion.Poc.Core/Protocol tft-companion/src/TftCompanion.Poc.Core/Security tft-companion/tests/TftCompanion.Poc.Tests
git commit -m "feat(tft-poc): add versioned loopback pairing protocol"
~~~

## 5. Task 4: Add minimal session, freshness, gap, and render-lease state machines

**Files:**

- Create: tft-companion/src/TftCompanion.Poc.Core/State/PocFreshness.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/State/PocSessionProjection.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/State/PocSessionReducer.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Render/RenderContracts.cs
- Create: tft-companion/src/TftCompanion.Poc.Core/Render/PocRenderLeaseCoordinator.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/PocSessionReducerTests.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/PocRenderLeaseCoordinatorTests.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/TestSupport/FakeTimeProvider.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/TestSupport/TestState.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/TestSupport/TestRender.cs

### State ownership for the PoC

~~~text
PocSessionReducer owns:
  generated session ID, session epoch, snapshot revision,
  match observed, round observed, freshness, gap/resync state.

PocRenderLeaseCoordinator owns:
  render connection epoch, lease ID, generation, baseline Hidden receipt,
  snapshot revision required after a new render lease.

The Renderer owns:
  only whether its local fixed marker is actually attached or removed.
~~~

The Bridge cannot create a session ID, clear a gap using an event, declare an overlay delivered, or restore a prior marker after its render socket closes.

The three test helpers have these exact, deliberately narrow responsibilities:

~~~csharp
public sealed class FakeTimeProvider : TimeProvider
{
    public FakeTimeProvider(DateTimeOffset utcNow);

    public override DateTimeOffset GetUtcNow();

    public void Advance(TimeSpan elapsed);
}

public static class TestState
{
    public static PocSessionProjection CurrentSnapshot(long revision);
}

public static class TestRender
{
    public static OverlayReceipt HiddenFor(OverlayCommand command);
}
~~~

TestState.CurrentSnapshot returns a fresh, non-empty fixed test Guid, SessionEpoch 1, MatchObserved true, RoundObserved true, Freshness Fresh, GapState None, and the supplied SnapshotRevision. TestRender.HiddenFor copies RuntimeEpoch, ServerInstanceId, ConnectionEpoch, Scope.SessionId, Scope.SessionEpoch, RenderLeaseId, RenderGeneration, and OperationId from the supplied command and sets ReceiptKind to Hidden. No helper manufactures an OverlayShown receipt.

Implement the helpers with these complete bodies:

~~~csharp
namespace TftCompanion.Poc.Tests.TestSupport;

public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        this.utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan elapsed)
    {
        utcNow = utcNow.Add(elapsed);
    }
}
~~~

~~~csharp
using TftCompanion.Poc.Core.State;

namespace TftCompanion.Poc.Tests.TestSupport;

public static class TestState
{
    public static PocSessionProjection CurrentSnapshot(long revision) => new(
        SessionId: new Guid("11111111-1111-1111-1111-111111111111"),
        SessionEpoch: 1,
        SnapshotRevision: revision,
        MatchObserved: true,
        RoundObserved: true,
        Freshness: PocFreshness.Fresh,
        GapState: PocGapState.None);
}
~~~

~~~csharp
using TftCompanion.Poc.Core.Protocol;
using TftCompanion.Poc.Core.Render;

namespace TftCompanion.Poc.Tests.TestSupport;

public static class TestRender
{
    public static OverlayReceipt HiddenFor(OverlayCommand command) => new(
        OverlayReceiptKind.Hidden,
        command.RuntimeEpoch,
        command.ServerInstanceId,
        command.ConnectionEpoch,
        command.Scope.SessionId,
        command.Scope.SessionEpoch,
        command.RenderLeaseId,
        command.RenderGeneration,
        command.OperationId);
}
~~~

- [ ] **Step 1: Write failing reducer and lease tests**

Create PocSessionReducerTests.cs.

~~~csharp
[Test]
public void sequence_gap_requires_an_authoritative_snapshot_before_fresh_state_returns()
{
    FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-07-14T00:00:00Z"));
    PocSessionReducer reducer = new(clock);

    reducer.Apply(TestWire.StateSnapshot(sequence: 1, matchObserved: true, roundObserved: true));
    reducer.Apply(TestWire.InfoUpdate(sequence: 3));

    PocSessionProjection duringGap = reducer.Current;
    Assert.Multiple(() =>
    {
        Assert.That(duringGap.GapState, Is.EqualTo(PocGapState.ResyncRequired));
        Assert.That(duringGap.Freshness, Is.EqualTo(PocFreshness.Unknown));
        Assert.That(duringGap.CanRenderProbe, Is.False);
    });

    reducer.Apply(TestWire.StateSnapshot(sequence: 4, matchObserved: true, roundObserved: true));

    Assert.Multiple(() =>
    {
        Assert.That(reducer.Current.GapState, Is.EqualTo(PocGapState.None));
        Assert.That(reducer.Current.Freshness, Is.EqualTo(PocFreshness.Fresh));
        Assert.That(reducer.Current.CanRenderProbe, Is.True);
    });
}

[Test]
public void freshness_is_host_time_based_and_becomes_stale_after_three_seconds()
{
    FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-07-14T00:00:00Z"));
    PocSessionReducer reducer = new(clock);

    reducer.Apply(TestWire.StateSnapshot(sequence: 1, matchObserved: true, roundObserved: true));
    clock.Advance(TimeSpan.FromSeconds(3.001));

    Assert.That(reducer.Current.Freshness, Is.EqualTo(PocFreshness.Stale));
    Assert.That(reducer.Current.CanRenderProbe, Is.False);
}

[Test]
public void false_match_snapshot_ends_the_in_memory_session_without_persisting_an_identity()
{
    PocSessionReducer reducer = new(new FakeTimeProvider(DateTimeOffset.UtcNow));

    reducer.Apply(TestWire.StateSnapshot(sequence: 1, matchObserved: true, roundObserved: true));
    Guid oldSessionId = reducer.Current.SessionId;
    reducer.Apply(TestWire.StateSnapshot(sequence: 2, matchObserved: false, roundObserved: false));

    Assert.Multiple(() =>
    {
        Assert.That(reducer.Current.SessionId, Is.EqualTo(Guid.Empty));
        Assert.That(reducer.Current.SessionEpoch, Is.GreaterThan(0));
        Assert.That(reducer.Current.MatchObserved, Is.False);
        Assert.That(oldSessionId, Is.Not.EqualTo(Guid.Empty));
    });
}

[Test]
public void ingest_disconnect_requires_resync_and_immediately_invalidates_render_eligibility()
{
    PocSessionReducer reducer = new(new FakeTimeProvider(DateTimeOffset.UtcNow));

    reducer.Apply(TestWire.StateSnapshot(sequence: 1, matchObserved: true, roundObserved: true));
    reducer.MarkResyncRequired();

    Assert.Multiple(() =>
    {
        Assert.That(reducer.Current.GapState, Is.EqualTo(PocGapState.ResyncRequired));
        Assert.That(reducer.Current.Freshness, Is.EqualTo(PocFreshness.Unknown));
        Assert.That(reducer.Current.CanRenderProbe, Is.False);
    });
}
~~~

Create PocRenderLeaseCoordinatorTests.cs.

~~~csharp
[Test]
public void newly_opened_render_connection_starts_with_hide_all_and_never_show()
{
    PocRenderLeaseCoordinator coordinator = new("runtime-1", "server-1");

    OverlayCommand first = coordinator.OpenRenderConnection(connectionEpoch: 7);

    Assert.Multiple(() =>
    {
        Assert.That(first.Operation, Is.EqualTo(OverlayOperation.HideAll));
        Assert.That(first.RenderGeneration, Is.EqualTo(0));
        Assert.That(coordinator.TryReconcile(TestState.CurrentSnapshot(revision: 1)), Is.Null);
    });
}

[Test]
public void matching_hidden_receipt_and_current_snapshot_allow_one_new_marker()
{
    PocRenderLeaseCoordinator coordinator = new("runtime-1", "server-1");
    OverlayCommand hide = coordinator.OpenRenderConnection(connectionEpoch: 7);

    Assert.That(coordinator.AcceptReceipt(TestRender.HiddenFor(hide)), Is.True);

    OverlayCommand? show = coordinator.TryReconcile(TestState.CurrentSnapshot(revision: 1));

    Assert.Multiple(() =>
    {
        Assert.That(show, Is.Not.Null);
        Assert.That(show!.Operation, Is.EqualTo(OverlayOperation.ShowPocMarker));
        Assert.That(show.RenderGeneration, Is.EqualTo(1));
    });
}

[Test]
public void reconnect_never_revives_old_marker_until_a_snapshot_arrives_after_the_new_lease()
{
    PocRenderLeaseCoordinator coordinator = new("runtime-1", "server-1");
    OverlayCommand firstHide = coordinator.OpenRenderConnection(connectionEpoch: 7);
    coordinator.AcceptReceipt(TestRender.HiddenFor(firstHide));
    OverlayCommand firstShow = coordinator.TryReconcile(TestState.CurrentSnapshot(revision: 1))!;

    OverlayCommand secondHide = coordinator.OpenRenderConnection(connectionEpoch: 8);
    coordinator.AcceptReceipt(TestRender.HiddenFor(secondHide));

    Assert.Multiple(() =>
    {
        Assert.That(firstShow.RenderLeaseId, Is.Not.EqualTo(secondHide.RenderLeaseId));
        Assert.That(coordinator.TryReconcile(TestState.CurrentSnapshot(revision: 1)), Is.Null);
        Assert.That(coordinator.TryReconcile(TestState.CurrentSnapshot(revision: 2))!.Operation,
            Is.EqualTo(OverlayOperation.ShowPocMarker));
    });
}

[Test]
public void receipt_from_old_lease_is_ignored()
{
    PocRenderLeaseCoordinator coordinator = new("runtime-1", "server-1");
    OverlayCommand oldHide = coordinator.OpenRenderConnection(connectionEpoch: 7);
    OverlayCommand currentHide = coordinator.OpenRenderConnection(connectionEpoch: 8);

    Assert.Multiple(() =>
    {
        Assert.That(coordinator.AcceptReceipt(TestRender.HiddenFor(oldHide)), Is.False);
        Assert.That(coordinator.AcceptReceipt(TestRender.HiddenFor(currentHide)), Is.True);
    });
}
~~~

- [ ] **Step 2: Run the state tests and confirm the intended red state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter "FullyQualifiedName~PocSessionReducerTests|FullyQualifiedName~PocRenderLeaseCoordinatorTests"
~~~

Expected: compilation fails because PocSessionReducer, PocRenderLeaseCoordinator, and their contracts have not been implemented.

- [ ] **Step 3: Implement the minimal state contracts**

Use these exact semantics.

~~~csharp
namespace TftCompanion.Poc.Core.State;

public enum PocFreshness
{
    Unknown,
    Fresh,
    Stale
}

public enum PocGapState
{
    None,
    ResyncRequired
}

public sealed record PocSessionProjection(
    Guid SessionId,
    long SessionEpoch,
    long SnapshotRevision,
    bool MatchObserved,
    bool RoundObserved,
    PocFreshness Freshness,
    PocGapState GapState)
{
    public bool CanRenderProbe =>
        SessionId != Guid.Empty &&
        MatchObserved &&
        RoundObserved &&
        Freshness == PocFreshness.Fresh &&
        GapState == PocGapState.None;
}
~~~

PocSessionReducer must:

1. Keep one expected stream sequence per tuple of BridgeInstanceId, TransportEpoch, and StreamId.
2. Treat a missing sequence as gap. It emits a resync-needed result and sets GapState to ResyncRequired.
3. Expose MarkResyncRequired() for an ingress link close, mailbox overflow, schema failure, or Bridge restart. It sets the same state as a sequence gap.
4. Allow only a valid StateSnapshot with IsAuthoritativeSnapshot true to clear the gap.
5. Generate a new Guid session ID only when an authoritative snapshot first reports MatchObserved true after no current session. This Guid is host-local and never persisted in SanitizedPocStatus.
6. Increment SessionEpoch whenever it starts or clears a session.
7. Measure freshness from TimeProvider.GetUtcNow() at Host ingest; never compare a JavaScript timestamp with .NET time.
8. Expose Unknown when a gap exists, Fresh at or below the TTL, and Stale after the TTL.

Use this state transition result:

~~~csharp
public sealed record PocReducerResult(
    PocSessionProjection Projection,
    bool AckRequired,
    bool ResyncRequired);
~~~

Create render contracts with a Global scope that is allowed only for HideAll:

~~~csharp
namespace TftCompanion.Poc.Core.Render;

public sealed record RenderScope(
    Guid SessionId,
    long SessionEpoch,
    string RoundKey)
{
    public static RenderScope Global { get; } = new(Guid.Empty, 0, "global");

    public bool IsGlobal => SessionId == Guid.Empty && SessionEpoch == 0 && RoundKey == "global";
}

public sealed record OverlayCommand(
    OverlayOperation Operation,
    string RuntimeEpoch,
    string ServerInstanceId,
    long ConnectionEpoch,
    RenderScope Scope,
    string RenderLeaseId,
    long RenderGeneration,
    Guid OperationId,
    long RequiredSnapshotRevision,
    int RemainingTtlMilliseconds);
~~~

PocRenderLeaseCoordinator must use these rules:

~~~text
OpenRenderConnection(connectionEpoch)
  creates a new unpredictable RenderLeaseId
  remembers the current maximum snapshot revision as the required revision
  clears baseline-hidden and shown flags
  returns HideAll at generation 0 with RenderScope.Global

AcceptReceipt(receipt)
  returns false unless runtime, server, connection epoch, lease ID,
  generation, and operation ID equal the current command
  only a matching Hidden receipt opens the baseline-hidden gate

TryReconcile(projection)
  returns null unless baseline-hidden is true,
  projection.CanRenderProbe is true,
  and projection.SnapshotRevision is strictly greater than the required revision
  otherwise returns exactly one ShowPocMarker at generation 1

OpenRenderConnection after a prior connection
  replaces all prior lease state and never resends the old Show command
~~~

ShowPocMarker uses a 3000 millisecond TTL. HideAll uses a zero TTL and is not delayed by the freshness timer.

- [ ] **Step 4: Run focused and full tests**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter "FullyQualifiedName~PocSessionReducerTests|FullyQualifiedName~PocRenderLeaseCoordinatorTests"
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore
~~~

Expected: gap, freshness, reconnect, and stale receipt cases all pass; no test relies on a raw GEP payload.

- [ ] **Step 5: Commit the in-memory lifecycle boundary**

~~~powershell
git add tft-companion/src/TftCompanion.Poc.Core/State tft-companion/src/TftCompanion.Poc.Core/Render tft-companion/tests/TftCompanion.Poc.Tests
git commit -m "feat(tft-poc): gate marker rendering on fresh resynced state"
~~~

## 6. Task 5: Build the loopback-only Host with physically independent ingest and render paths

**Files:**

- Create: tft-companion/src/TftCompanion.Poc.Host/Program.cs
- Create: tft-companion/src/TftCompanion.Poc.Host/Host/PocHostOptions.cs
- Create: tft-companion/src/TftCompanion.Poc.Host/Host/PocHostFactory.cs
- Create: tft-companion/src/TftCompanion.Poc.Host/Host/WebSocketFrameReader.cs
- Create: tft-companion/src/TftCompanion.Poc.Host/Host/PocConnectionHandler.cs
- Create: tft-companion/src/TftCompanion.Poc.Host/Host/PocRuntime.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/PocHostIntegrationTests.cs
- Create: tft-companion/tests/TftCompanion.Poc.Tests/TestSupport/PocHostHarness.cs

### Host startup contract

The Host reads exactly these environment variables:

~~~text
TFT_COMPANION_POC_ALLOWED_ORIGIN
TFT_COMPANION_POC_PAIRING_TOKEN
~~~

It accepts an optional --port argument. It has no default Origin and no default pairing token. A missing, blank, or malformed configuration exits with a sanitized failure code before opening a socket. The optional --simulate-storage-unavailable argument is a test-only deterministic storage seam: it forces StorageHealth.MemoryOnlyDegraded and does not create or inspect any file.

The Host binds only:

~~~text
http://127.0.0.1:<port>
~~~

It does not call ListenAnyIP, ListenLocalhost, URLS, UseUrls, a wildcard host, IPv6, or an HTTP fallback route.

PocHostOptions and the factory surface are fixed as follows:

~~~csharp
public sealed record PocHostOptions(
    int Port,
    PairingConfiguration Pairing,
    bool SimulateStorageUnavailable)
{
    public static bool TryParse(
        string[] args,
        IReadOnlyDictionary<string, string?> environment,
        out PocHostOptions? options,
        out string failureCode);
}

public static class PocHostFactory
{
    public static WebApplication Create(
        PocHostOptions options,
        IStorageFileSystem storageFileSystem,
        TimeProvider timeProvider);
}
~~~

TryParse accepts only --port followed by an integer in the inclusive range 1024 through 65535 and the standalone --simulate-storage-unavailable. It rejects duplicate arguments, unknown arguments, missing Origin, missing token, a blank value, or a value containing a newline. It never substitutes a default Origin/token. The Program entry point calls TryParse with Environment.GetEnvironmentVariables(), prints only TFTPOC_CONFIG_REJECTED plus the failure code on failure, and returns a nonzero exit code.

PocHostHarness.cs is test-only and has these exact members:

~~~csharp
public sealed class PocHostHarness : IAsyncDisposable
{
    public static Task<PocHostHarness> StartAsync();

    public Task<ClientWebSocket> ConnectAsync(string path, PeerRole role);

    public Task<ClientWebSocket> ConnectRawAsync(string path, string origin);

    public Task SendHelloAsync(
        ClientWebSocket socket,
        PeerRole role,
        ChannelKind channel);

    public Task<WelcomePayload> ReceiveWelcomeAsync(ClientWebSocket socket);

    public Task<string> ReceiveTextAsync(ClientWebSocket socket);

    public Task<OverlayCommand> ReceiveOverlayCommandAsync(ClientWebSocket socket);

    public Task<OverlayCommand?> TryReceiveOverlayCommandAsync(
        ClientWebSocket socket,
        TimeSpan timeout);

    public Task SendHiddenReceiptAsync(
        ClientWebSocket socket,
        OverlayCommand command);

    public Task SendAuthoritativeSnapshotAsync(
        ClientWebSocket socket,
        long sequence);

    public Task SendBinaryFrameAsync(
        ClientWebSocket socket,
        byte[] bytes);

    public ValueTask DisposeAsync();
}
~~~

The harness chooses an available IPv4 loopback port by binding a temporary TcpListener to IPAddress.Loopback port 0, records the assigned port, stops the temporary listener, and starts the Host immediately. It sets exactly one Origin header through ClientWebSocketOptions.SetRequestHeader("Origin", origin). ConnectAsync opens the WebSocket and sends the correctly matched Hello but intentionally does not consume Welcome; tests explicitly receive Welcome before expecting a later route command. ConnectRawAsync opens the WebSocket only. It uses the fake available storage adapter and never launches a child process.

- [ ] **Step 1: Write failing loopback integration tests**

Create PocHostIntegrationTests.cs around a real in-process Kestrel host and ClientWebSocket. The test harness must select an unused IPv4 loopback port, start the Host with a fake available storage adapter, and stop it in DisposeAsync.

~~~csharp
[Test]
public async Task both_routes_require_separate_handshakes_and_return_distinct_connection_epochs()
{
    await using PocHostHarness host = await PocHostHarness.StartAsync();

    await using ClientWebSocket ingest = await host.ConnectAsync("/ingest", PeerRole.IngestBridge);
    WelcomePayload ingestWelcome = await host.ReceiveWelcomeAsync(ingest);

    await using ClientWebSocket render = await host.ConnectAsync("/render", PeerRole.RenderBridge);
    WelcomePayload renderWelcome = await host.ReceiveWelcomeAsync(render);

    Assert.Multiple(() =>
    {
        Assert.That(ingestWelcome.ConnectionEpoch, Is.Not.EqualTo(renderWelcome.ConnectionEpoch));
        Assert.That(ingestWelcome.AllowedScopes, Does.Contain("PublishSnapshot"));
        Assert.That(renderWelcome.AllowedScopes, Does.Contain("ReportReceipt"));
    });
}

[Test]
public async Task wrong_origin_is_rejected_before_websocket_upgrade()
{
    await using PocHostHarness host = await PocHostHarness.StartAsync();

    Assert.ThrowsAsync<WebSocketException>(async () =>
        await host.ConnectRawAsync(
            "/ingest",
            origin: "https://ordinary-web-page"));
}

[Test]
public async Task render_reconnect_receives_hide_all_before_any_show_and_requires_a_new_snapshot()
{
    await using PocHostHarness host = await PocHostHarness.StartAsync();
    await using ClientWebSocket ingest = await host.ConnectAsync("/ingest", PeerRole.IngestBridge);
    await using ClientWebSocket firstRender = await host.ConnectAsync("/render", PeerRole.RenderBridge);

    await host.ReceiveWelcomeAsync(ingest);
    await host.ReceiveWelcomeAsync(firstRender);
    OverlayCommand firstHide = await host.ReceiveOverlayCommandAsync(firstRender);
    await host.SendHiddenReceiptAsync(firstRender, firstHide);
    await host.SendAuthoritativeSnapshotAsync(ingest, sequence: 1);
    OverlayCommand firstShow = await host.ReceiveOverlayCommandAsync(firstRender);
    Assert.That(firstShow.Operation, Is.EqualTo(OverlayOperation.ShowPocMarker));

    await firstRender.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);

    await using ClientWebSocket secondRender = await host.ConnectAsync("/render", PeerRole.RenderBridge);
    await host.ReceiveWelcomeAsync(secondRender);
    OverlayCommand secondHide = await host.ReceiveOverlayCommandAsync(secondRender);
    await host.SendHiddenReceiptAsync(secondRender, secondHide);

    Assert.Multiple(() =>
    {
        Assert.That(secondHide.Operation, Is.EqualTo(OverlayOperation.HideAll));
        Assert.That(secondHide.RenderLeaseId, Is.Not.EqualTo(firstShow.RenderLeaseId));
        Assert.That(await host.TryReceiveOverlayCommandAsync(secondRender, TimeSpan.FromMilliseconds(150)), Is.Null);
    });

    await host.SendAuthoritativeSnapshotAsync(ingest, sequence: 2);
    OverlayCommand secondShow = await host.ReceiveOverlayCommandAsync(secondRender);

    Assert.That(secondShow.Operation, Is.EqualTo(OverlayOperation.ShowPocMarker));
}

[Test]
public async Task oversized_or_binary_frame_is_rejected_without_affecting_the_other_channel()
{
    await using PocHostHarness host = await PocHostHarness.StartAsync();
    await using ClientWebSocket render = await host.ConnectAsync("/render", PeerRole.RenderBridge);
    await host.ReceiveWelcomeAsync(render);
    OverlayCommand hide = await host.ReceiveOverlayCommandAsync(render);

    await using ClientWebSocket ingest = await host.ConnectAsync("/ingest", PeerRole.IngestBridge);
    await host.ReceiveWelcomeAsync(ingest);
    await host.SendBinaryFrameAsync(ingest, new byte[] { 1, 2, 3 });

    Assert.That(await host.ReceiveTextAsync(ingest), Does.Contain("BINARY_FRAME_REJECTED"));
    Assert.That(hide.Operation, Is.EqualTo(OverlayOperation.HideAll));
}

[Test]
public async Task ingest_disconnect_hides_the_marker_and_reconnect_requires_a_snapshot()
{
    await using PocHostHarness host = await PocHostHarness.StartAsync();
    await using ClientWebSocket ingest = await host.ConnectAsync("/ingest", PeerRole.IngestBridge);
    await using ClientWebSocket render = await host.ConnectAsync("/render", PeerRole.RenderBridge);

    await host.ReceiveWelcomeAsync(ingest);
    await host.ReceiveWelcomeAsync(render);
    OverlayCommand initialHide = await host.ReceiveOverlayCommandAsync(render);
    await host.SendHiddenReceiptAsync(render, initialHide);
    await host.SendAuthoritativeSnapshotAsync(ingest, sequence: 1);
    OverlayCommand initialShow = await host.ReceiveOverlayCommandAsync(render);
    Assert.That(initialShow.Operation, Is.EqualTo(OverlayOperation.ShowPocMarker));

    await ingest.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
    OverlayCommand gapHide = await host.ReceiveOverlayCommandAsync(render);
    Assert.That(gapHide.Operation, Is.EqualTo(OverlayOperation.HideAll));

    await using ClientWebSocket reconnectedIngest = await host.ConnectAsync("/ingest", PeerRole.IngestBridge);
    WelcomePayload welcome = await host.ReceiveWelcomeAsync(reconnectedIngest);

    Assert.Multiple(() =>
    {
        Assert.That(welcome.ResyncRequired, Is.True);
        Assert.That(await host.TryReceiveOverlayCommandAsync(render, TimeSpan.FromMilliseconds(150)), Is.Null);
    });

    await host.SendAuthoritativeSnapshotAsync(reconnectedIngest, sequence: 1);
}
~~~

- [ ] **Step 2: Run integration tests and confirm the intended red state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter FullyQualifiedName~PocHostIntegrationTests
~~~

Expected: compilation fails because PocHostHarness and the Host runtime have not been implemented.

- [ ] **Step 3: Implement the Host without an HTTP control surface**

Implement PocHostFactory using Kestrel's explicit IPv4 overload:

~~~csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(
        System.Net.IPAddress.Loopback,
        hostOptions.Port,
        listenOptions => listenOptions.Protocols =
            Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
});
~~~

Map only GET upgrade handling for /ingest and /render. Every other request returns 404 without body data. Each route must:

1. Require HttpContext.WebSockets.IsWebSocketRequest.
2. Require context.Connection.RemoteIpAddress to equal IPAddress.Loopback.
3. Reject a missing/different Origin, query string, or non-loopback request with HTTP 403 before AcceptWebSocketAsync; do not put an Origin or configuration value in the response.
4. Read exactly one text Hello frame within three seconds after the accepted upgrade.
5. Validate the frame before allocating a channel-specific connection state.
6. For a Hello validation or pairing failure, send a short named Rejected message, then close the WebSocket.
7. Send Welcome with a Host-generated server instance ID and monotonically increasing connection epoch only after all checks pass.

WebSocketFrameReader must reject binary, fragmented messages exceeding ProtocolConstants.MaximumTextFrameBytes, messages that do not end within the byte limit, and a text frame whose UTF-8 decoder reports invalid data. It must never log a received body.

PocRuntime must contain two independent bounded channels:

~~~csharp
private readonly Channel<WireEnvelope> ingestMailbox =
    Channel.CreateBounded<WireEnvelope>(new BoundedChannelOptions(
        ProtocolConstants.IngressMailboxCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });

private readonly Channel<OverlayCommand> renderMailbox =
    Channel.CreateBounded<OverlayCommand>(new BoundedChannelOptions(
        ProtocolConstants.RenderMailboxCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
~~~

If ingestMailbox.TryWrite returns false, do not drop silently. Set the reducer to resync-required and send a ResyncRequired message on the active ingest connection. Do not write an event spool or retry database. If renderMailbox cannot accept HideAll, close the render connection and leave the in-memory lease invalid; the Background Bridge's local close handler is responsible for immediately hiding the marker.

When a render channel disconnects or reconnects:

~~~text
Host:
  invalidate current render lease
  mark current state as requiring a new authoritative snapshot for the next lease
  on the new render handshake, enqueue HideAll first
  send ResyncRequired to active ingest

Background Bridge:
  immediately issue an internal HideAll to the Renderer on render socket close
  request getInfo() only because it received ResyncRequired

Host:
  waits for matching Hidden receipt and a later authoritative StateSnapshot
  only then may enqueue one ShowPocMarker
~~~

When an ingest channel disconnects, PocRuntime calls PocSessionReducer.MarkResyncRequired(), invalidates the current render eligibility, and enqueues HideAll for an active render connection. On the next accepted ingest Hello, Welcome has ResyncRequired set to true. The Background Bridge responds by calling getInfo once and publishing a new authoritative semantic StateSnapshot. It does not continue an old stream sequence, restore an old marker, or infer missing facts.

PocRuntime updates SanitizedPocStatus no more than once per second. It saves only:

~~~text
runtime epoch
whether each link is online
whether match and round are observed
freshness label
gap label
last named error code
~~~

Do not log any received envelope, Origin, token, raw GEP payload, renderer content, match identifier, or stack trace containing request data. Console output is limited to fixed codes such as TFTPOC_READY, TFTPOC_CONFIG_REJECTED, and TFTPOC_STOPPED.

- [ ] **Step 4: Run integration and full .NET tests**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter FullyQualifiedName~PocHostIntegrationTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore
~~~

Expected: all in-process communication uses only 127.0.0.1. The reconnect test proves HideAll is first and an old marker does not revive. No test starts an external Overwolf runtime.

- [ ] **Step 5: Commit the local Host**

~~~powershell
git add tft-companion/src/TftCompanion.Poc.Host tft-companion/tests/TftCompanion.Poc.Tests
git commit -m "feat(tft-poc): add loopback host and isolated channels"
~~~

## 7. Task 6: Add the minimal Overwolf Background Bridge and mouse-transparent Renderer

**Files:**

- Create: tft-companion/overwolf/tft-companion-poc/manifest.json
- Create: tft-companion/overwolf/tft-companion-poc/background.html
- Create: tft-companion/overwolf/tft-companion-poc/background.mjs
- Create: tft-companion/overwolf/tft-companion-poc/overlay.html
- Create: tft-companion/overwolf/tft-companion-poc/overlay.mjs
- Create: tft-companion/overwolf/tft-companion-poc/overlay.css
- Create: tft-companion/overwolf/tft-companion-poc/shared/protocol.mjs
- Create: tft-companion/overwolf/tft-companion-poc/shared/bridge-link.mjs
- Create: tft-companion/overwolf/tft-companion-poc/dev-settings.example.js
- Create: tft-companion/overwolf/tft-companion-poc/README.md
- Create: tft-companion/tests/overwolf/protocol.test.mjs
- Create: tft-companion/tests/overwolf/source-boundary.test.mjs

### Manifest rule before code is written

First re-check the current official manifest schema and validate a minimal Native manifest with the official validator. The manifest must declare only:

~~~text
one background window
one transparent overlay window
the TFT game target
the minimum GEP permissions/features validated by the current official TFT page
~~~

It must not request input, keyboard, mouse, desktop-control, process, arbitrary network, extension, file, capture, microphone, or remote content capability. If a proposed manifest field cannot be confirmed by the current official schema, omit the field and record the incompatibility; do not invent a compatibility shim.

- [ ] **Step 1: Write the failing Node tests for semantic conversion and forbidden browser persistence**

Create protocol.test.mjs.

~~~javascript
import assert from "node:assert/strict";
import test from "node:test";
import {
  buildHello,
  mapInfoToSnapshot,
  mapEventToSemanticSignal,
  shouldAcceptHostCommand
} from "../../overwolf/tft-companion-poc/shared/protocol.mjs";

test("semantic snapshot retains booleans but drops pseudo match identifiers", () => {
  const snapshot = mapInfoToSnapshot({
    match_info: {
      pseudo_match_id: "do-not-forward-or-persist",
      round_type: "combat"
    }
  });

  assert.deepEqual(snapshot, {
    matchObserved: true,
    roundObserved: true,
    isAuthoritativeSnapshot: true
  });
  assert.equal(JSON.stringify(snapshot).includes("do-not-forward-or-persist"), false);
});

test("round event becomes only a round-observed signal", () => {
  assert.deepEqual(
    mapEventToSemanticSignal({ name: "round_start", data: "sensitive-event-data" }),
    { roundObserved: true }
  );
});

test("hello puts pairing proof in the body and never in a URL", () => {
  const hello = buildHello({
    role: "IngestBridge",
    channel: "Ingest",
    pairingToken: "local-pairing-token",
    bridgeInstanceId: "bridge-1",
    transportEpoch: 1,
    streamId: "ingest-1"
  });

  assert.equal(hello.payload.pairingProof, "local-pairing-token");
  assert.equal(JSON.stringify(hello).includes("ws://"), false);
});

test("renderer accepts only the two fixed declarative commands", () => {
  assert.equal(shouldAcceptHostCommand({ operation: "HideAll" }), true);
  assert.equal(shouldAcceptHostCommand({ operation: "ShowPocMarker" }), true);
  assert.equal(shouldAcceptHostCommand({ operation: "ArbitraryHtml" }), false);
});
~~~

Create source-boundary.test.mjs. It must read only the Bridge/Renderer source tree and fail if it finds any forbidden browser persistence, fallback transport, or game-control token.

~~~javascript
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const root = path.resolve("overwolf/tft-companion-poc");
const forbidden = [
  "overwolf.io",
  "localStorage",
  "sessionStorage",
  "indexedDB",
  "fetch(",
  "XMLHttpRequest",
  "SendInput",
  "MouseClick",
  "KeyboardInject",
  "MemoryRead",
  "PacketCapture",
  "adb",
  "http://",
  "https://"
];

test("bridge and renderer contain no persistence, fallback transport, or game control API", () => {
  const files = [
    "background.mjs",
    "overlay.mjs",
    "shared/protocol.mjs",
    "shared/bridge-link.mjs"
  ];

  for (const file of files) {
    const source = fs.readFileSync(path.join(root, file), "utf8");
    for (const token of forbidden) {
      assert.equal(
        source.includes(token),
        false,
        file + " contains forbidden token: " + token
      );
    }
  }
});
~~~

- [ ] **Step 2: Run Node tests and confirm the intended red state**

Run:

~~~powershell
node --test .\tests\overwolf\protocol.test.mjs .\tests\overwolf\source-boundary.test.mjs
~~~

Expected: failure because the Overwolf bundle and exports do not exist.

- [ ] **Step 3: Implement a strict Background Bridge**

dev-settings.example.js must contain an explicit non-working example:

~~~javascript
window.TftCompanionPocDevSettings = Object.freeze({
  host: "ws://127.0.0.1:32173",
  allowedOrigin: "copy-the-actual-overwolf-origin-here",
  pairingToken: "copy-a-32-byte-base64url-token-here",
  requiredFeatures: []
});
~~~

The ignored dev-settings.local.js has the same shape but must contain the actual local values. It is created manually by the developer; neither Bridge nor Host creates or edits it. background.mjs must refuse startup if:

~~~text
host is not exactly ws://127.0.0.1:32173
allowedOrigin is blank
pairingToken is blank
requiredFeatures is empty
requiredFeatures contains a non-string
~~~

The Bridge's GEP logic is restricted to this sequence:

~~~javascript
overwolf.games.events.setRequiredFeatures(settings.requiredFeatures, onFeatureRegistration);
overwolf.games.events.onNewEvents.addListener(onNewEvents);
overwolf.games.events.onInfoUpdates2.addListener(onInfoUpdates);
overwolf.games.events.getInfo(onInitialInfo);
~~~

onFeatureRegistration records only success/failure as a Boolean capability. onNewEvents and onInfoUpdates2 map raw objects immediately to the semantic Booleans in protocol.mjs. getInfo maps only:

~~~text
matchObserved: whether the documented match signal is present
roundObserved: whether the documented round signal is present
isAuthoritativeSnapshot: true
~~~

The mapper is allowed to inspect the current documented field path for pseudo_match_id or round signals, but it must not copy any value to the outgoing object, local variable captured by a logger, DOM, storage, file, queue persistence, or receipt.

Bridge-link.mjs must:

1. Create distinct WebSocket objects for /ingest and /render.
2. Send Hello as each socket's first application frame.
3. Keep only bounded in-memory semantic queues: 64 ingress messages and 16 render receipts.
4. On an inbound ResyncRequired or a Welcome whose ResyncRequired field is true, call getInfo once. It may not start an HTTP, file, SQLite, or timer-polling fallback.
5. On /render close or error, send an internal HideAll message to the declared overlay window before scheduling one of at most five bounded reconnect attempts.
6. On /render reconnect, wait for Host's HideAll; forward it to the Renderer; forward the Renderer’s actual Hidden receipt; and do not synthesize OverlayShown.
7. Never add a token to a WebSocket URL, query string, console output, exception text, or renderer message.

The Renderer receives only these command objects:

~~~javascript
{ operation: "HideAll", renderLeaseId, renderGeneration, operationId }
{ operation: "ShowPocMarker", renderLeaseId, renderGeneration, operationId }
~~~

Any extra property needed for a future feature is rejected in protocol.mjs. No arbitrary text, HTML, URL, style, script, position, or unit data crosses the boundary.

overlay.css must contain:

~~~css
html,
body {
  width: 100%;
  height: 100%;
  margin: 0;
  overflow: hidden;
  background: transparent;
  pointer-events: none;
}

#poc-marker {
  position: fixed;
  top: 16px;
  left: 16px;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #00ff88;
  pointer-events: none;
}
~~~

overlay.mjs must add the marker only after a ShowPocMarker command, wait one requestAnimationFrame, verify marker.isConnected and marker.getClientRects().length is greater than zero, and then forward an OverlayShown receipt through Background Bridge. HideAll must remove every node with data-tft-poc-marker, wait one requestAnimationFrame, and then forward Hidden. A local window rebuild begins by executing the same HideAll path before accepting any later ShowPocMarker.

The documented manifest must configure the overlay as transparent, non-focus-stealing, and click-through using field names supported by the current official manifest schema. CSS pointer-events is defense in depth, not the entire click-through guarantee.

- [ ] **Step 4: Run Node tests and validate the manifest**

Run:

~~~powershell
node --test .\tests\overwolf\protocol.test.mjs .\tests\overwolf\source-boundary.test.mjs
~~~

Expected: all Node tests pass. Then validate manifest.json with the current official Overwolf manifest validator. Expected: a schema-valid manifest with only the two declared windows and TFT GEP requirements.

If the official validator rejects an assumed property, correct only the manifest schema usage. Do not add a fallback URL, remote resource, input permission, or a broad capability to satisfy the validator.

- [ ] **Step 5: Commit the local-only Overwolf bundle**

~~~powershell
git add tft-companion/overwolf tft-companion/tests/overwolf
git commit -m "feat(tft-poc): add read-only overwolf bridge and renderer"
~~~

## 8. Task 7: Add cross-boundary safety tests and an auditable target-machine runbook

**Files:**

- Create: tft-companion/tests/TftCompanion.Poc.Tests/SourceBoundaryTests.cs
- Create: docs/tft-companion/v0.0.1-target-machine-runbook.md
- Create: docs/tft-companion/v0.0.1-go-no-go-record.md

- [ ] **Step 1: Write failing static source-boundary tests**

Create SourceBoundaryTests.cs. It must load source files below the repository root and inspect only the new tft-companion tree.

~~~csharp
[TestFixture]
public sealed class SourceBoundaryTests
{
    [Test]
    public void dotnet_runtime_does_not_reference_disallowed_alife_or_game_control_symbols()
    {
        string source = ReadTftCompanionSource();
        string[] forbidden =
        [
            "AlifePath",
            "StorageSystem",
            "ConfigurationSystem",
            "WebBridge",
            "SpeechService",
            "DataAgent",
            "QChat",
            "OneBot",
            "VisionService",
            "SendInput",
            "MouseClick",
            "KeyboardInject",
            "GameFocus",
            "MemoryRead",
            "PacketCapture",
            "0.0.0.0",
            "ListenAnyIP",
            "ListenLocalhost"
        ];

        foreach (string token in forbidden)
            Assert.That(source, Does.Not.Contain(token), token + " is forbidden in TFT PoC source.");
    }

    [Test]
    public void sanitized_status_model_has_only_the_eight_approved_fields()
    {
        string[] names = typeof(SanitizedPocStatus)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(names, Is.EqualTo(new[]
        {
            "BridgeOnline",
            "Freshness",
            "GapState",
            "LastErrorCode",
            "MatchObserved",
            "RenderOnline",
            "RoundObserved",
            "RuntimeEpoch"
        }));
    }
}
~~~

- [ ] **Step 2: Run source-boundary tests and confirm the intended red state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --filter FullyQualifiedName~SourceBoundaryTests
~~~

Expected: failure because SourceBoundaryTests has not yet been added or its repository-root helper has not been implemented.

- [ ] **Step 3: Implement the static boundary helper and write the exact manual runbook**

SourceBoundaryTests must locate the root by walking upward until it finds tft-companion/TftCompanion.Poc.slnx, then concatenate only:

~~~text
tft-companion/src/**/*.cs
tft-companion/overwolf/tft-companion-poc/**/*.mjs
tft-companion/overwolf/tft-companion-poc/**/*.html
tft-companion/overwolf/tft-companion-poc/**/*.css
tft-companion/overwolf/tft-companion-poc/manifest.json
~~~

It must exclude tests, ignored local config, artifacts, and documentation so a forbidden term in a test name or runbook cannot create a false positive.

Write v0.0.1-target-machine-runbook.md with these fixed sections and actions:

1. **Preflight**

   - Confirm D:\AlifeData\TFTCompanion is the only Host persistence root.
   - Record Windows build, .NET SDK version, Overwolf runtime version, TFT patch/set, and that the match is a custom game. Do not record Riot ID or player names.
   - Validate manifest.json using the official current validator.
   - Confirm dev-settings.local.js is ignored by Git and contains a nonempty exact Origin, 32-byte base64url pairing token, and current official feature list.

2. **Host start**

   ~~~powershell
   $env:TFT_COMPANION_POC_ALLOWED_ORIGIN = 'the-exact-origin-observed-for-the-loaded-overwolf-app'
   $env:TFT_COMPANION_POC_PAIRING_TOKEN = 'the-same-32-byte-base64url-token-as-dev-settings.local.js'
   & 'C:\Users\hu shu\.dotnet\dotnet.exe' run --project .\src\TftCompanion.Poc.Host\TftCompanion.Poc.Host.csproj --no-build -- --port 32173
   ~~~

   Expected: only TFTPOC_READY appears. The console must not print a token, Origin, raw event, GEP object, or storage path outside the fixed root.

3. **Channel proof**

   - Load the app in the existing Overwolf development environment.
   - Confirm one accepted /ingest Hello/Welcome and one accepted /render Hello/Welcome in the sanitized status view.
   - Confirm the two connection epochs differ.
   - Confirm the first render command is HideAll and the Renderer returns Hidden only after it removes the marker.

4. **GEP proof in a custom game**

   - Confirm setRequiredFeatures callback result, getInfo result, onNewEvents observation, and onInfoUpdates2 observation.
   - Confirm a sanitized StateSnapshot reports only MatchObserved, RoundObserved, IsAuthoritativeSnapshot.
   - Confirm Host reports Fresh after the snapshot.
   - Reload the Background Bridge through the existing Overwolf developer tool to close and reconnect /ingest. Confirm Host sends HideAll to any active renderer, the new Welcome has ResyncRequired true, the Bridge calls getInfo once, and fresh state returns only after a new authoritative snapshot.

5. **Renderer rebuild/disconnect proof**

   - Reload or rebuild the declared overlay through the existing Overwolf developer tool.
   - Confirm Background Bridge performs local HideAll immediately.
   - Confirm the next Host render connection begins with HideAll.
   - Confirm no marker returns until Hidden is received and getInfo produces a post-reconnect authoritative snapshot.

6. **D-drive degraded proof**

   - Run the Host once with --simulate-storage-unavailable.
   - Confirm StorageHealth is MemoryOnlyDegraded, no poc-status.json is written, and all bridge/renderer behavior remains in memory.
   - Use the automated FakeStorageFileSystem tests as the authoritative proof for C-path non-fallback. For a real process audit, filter only the Host executable in a file-I/O monitor; do not attribute Chromium/Overwolf runtime cache files to plugin-controlled Host writes.

7. **Stop and clean check**

   - Stop Host and unload the dev app.
   - Confirm the only optional artifact is D:\AlifeData\TFTCompanion\poc-status.json.
   - Confirm it is at most 8192 bytes and contains only the eight approved SanitizedPocStatus fields.

Write v0.0.1-go-no-go-record.md with a decision table containing exactly these rows:

| Gate | Pass criterion | Failure action |
|---|---|---|
| D-root integrity | Canonical final path resolves to expected D volume; failure becomes MemoryOnlyDegraded or IntegrityDegraded without any alternative write | Stop; repair path configuration; do not add a fallback root |
| Loopback transport | Both routes connect as WebSocket over 127.0.0.1 with exact Origin and pairing proof | Stop; open the separate Sidecar + Named Pipe re-review, do not use polling |
| GEP minimum facts | getInfo plus supported callbacks establish match, round, freshness, and gap facts, or a missing optional fact is explicitly capability-disabled | If match, round, freshness, or gap cannot be established, do not start v0.1.0 |
| Renderer safety | HideAll precedes all later ShowPocMarker commands; rebuild/disconnect does not revive an old marker | Stop; fix receipt/lease lifecycle before adding any UI |
| Privacy and storage | No raw GEP, identifiers, screenshots, tokens, URLs, or C/AppData/Temp Host writes | Stop; fix the boundary before broader testing |

The result record must store only Pass, Fail, or BlockedExternal; runtime version strings; duration buckets; named error codes; and the eight status fields. It must not copy a raw console log.

- [ ] **Step 4: Run all automated safety checks**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore
node --test .\tests\overwolf\protocol.test.mjs .\tests\overwolf\source-boundary.test.mjs
rg -n "AlifePath|StorageSystem|ConfigurationSystem|WebBridge|SpeechService|DataAgent|QChat|OneBot|VisionService|SendInput|MouseClick|KeyboardInject|GameFocus|MemoryRead|PacketCapture|0\.0\.0\.0|ListenAnyIP|ListenLocalhost" .\src .\overwolf\tft-companion-poc
~~~

Expected: all .NET and Node tests pass. The rg command returns exit code 1 because it finds no matches; treat that specific exit code as success for this final negative scan.

- [ ] **Step 5: Commit safety checks and target-machine documentation**

~~~powershell
git add tft-companion/tests/TftCompanion.Poc.Tests docs/tft-companion
git commit -m "test(tft-poc): document compatibility gates and safety checks"
~~~

## 9. Task 8: Run final local verification and package the v0.0.1 decision

**Files:**

- Modify: tft-companion/README.md
- Modify: docs/tft-companion/v0.0.1-go-no-go-record.md
- Test: all files created by Tasks 1 through 7

- [ ] **Step 1: Write the failing final acceptance checklist into README.md**

Create README.md with this exact acceptance checklist. Leave every checkbox unchecked until evidence exists.

~~~markdown
# TFT Companion v0.0.1 Runtime Compatibility PoC

## Local automated gate

- [ ] .NET 9 solution builds with C:\Users\hu shu\.dotnet\dotnet.exe.
- [ ] All NUnit tests pass.
- [ ] All Node bridge/renderer tests pass.
- [ ] Source boundary scan has no forbidden runtime references.
- [ ] No test needs an external account, game process, or Overwolf runtime.

## Target-machine gate

- [ ] Manifest is valid against the current official Overwolf schema.
- [ ] /ingest and /render both complete Hello/Welcome over 127.0.0.1.
- [ ] The Origin, pairing, role, version, schema, size, and message-kind negative cases are rejected.
- [ ] GEP establishes the minimum semantic facts in a custom TFT game.
- [ ] A gap yields ResyncRequired and only a new snapshot clears it.
- [ ] Renderer rebuild/disconnect hides before any later marker.
- [ ] D-drive degradation remains memory-only and does not create a Host fallback file.
~~~

- [ ] **Step 2: Run the full local gate**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' restore .\TftCompanion.Poc.slnx
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build .\TftCompanion.Poc.slnx --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --logger "console;verbosity=normal" --results-directory .\artifacts\TestResults
node --test .\tests\overwolf\protocol.test.mjs .\tests\overwolf\source-boundary.test.mjs
git diff --check
~~~

Expected:

~~~text
.NET build: 0 warnings, 0 errors
.NET tests: all passed
Node tests: all passed
git diff --check: no output and exit code 0
~~~

Do not mark any target-machine checkbox as complete based solely on local tests.

- [ ] **Step 3: Execute the target-machine runbook only after the local gate passes**

Follow docs/tft-companion/v0.0.1-target-machine-runbook.md exactly. Record one value for every row in docs/tft-companion/v0.0.1-go-no-go-record.md:

~~~text
Pass
Fail
BlockedExternal
~~~

BlockedExternal is reserved for unavailable Overwolf development access, unavailable target game/custom game, unavailable D drive, or an official platform/runtime gate that cannot be tested. It is not a passing result and does not permit v0.1.0 work.

- [ ] **Step 4: Apply the version decision**

Apply exactly one result:

~~~text
GO:
  Every table row is Pass.
  The v0.1.0 Panel-first Local Coach plan may be reviewed.

NO-GO — loopback/platform:
  A loopback, Origin, CSP, manifest, or WebSocket compatibility gate fails.
  Stop. Do not use HTTP polling, file transfer, SQLite polling, or a hidden fallback.
  Create a separate Sidecar + Named Pipe architecture review.
  Budget impact: additional 5–10 net person-days, not silently included in v0.0.1.

NO-GO — facts:
  Match, round, freshness, or gap cannot be established reliably.
  Stop. Keep the unsupported capability Disabled and do not produce advice by inference.

NO-GO — safety:
  An old marker revives, a renderer is not click-through, a raw payload/identifier is retained,
  or the Host controls a C/AppData/Temp write.
  Stop. Fix the failed safety boundary before adding features.

BLOCKED EXTERNAL:
  Preserve the local automated evidence and record precisely which external precondition is absent.
  Do not create an account or alter the product scope without separate user authorization.
~~~

- [ ] **Step 5: Commit the final v0.0.1 evidence state**

~~~powershell
git add tft-companion/README.md docs/tft-companion/v0.0.1-go-no-go-record.md
git commit -m "docs(tft-poc): record runtime compatibility decision"
~~~

## 10. Scope-to-task coverage check

| Approved v0.0.1 requirement | Implementing task |
|---|---|
| Independent directory and .NET 9 toolchain | Task 1 |
| D root final-volume/reparse validation | Task 2 |
| No C/AppData/Temp fallback and memory-only degradation | Task 2, Task 7, Task 8 |
| Two physical loopback WebSocket channels | Task 5 |
| Hello/Welcome, Origin, role, pairing, schema and message allowlist | Task 3, Task 5 |
| Minimum semantic session/round/freshness/gap facts | Task 4, Task 6, Task 7 |
| GEP setRequiredFeatures, getInfo, onNewEvents, onInfoUpdates2 | Task 6, Task 7 |
| HideAll baseline, real Renderer receipt, reconnect non-revival | Task 4, Task 5, Task 6 |
| Raw GEP never persisted | Task 2, Task 6, Task 7 |
| Target-machine evidence and go/no-go outcome | Task 7, Task 8 |
| No strategy, advice, board positioning, TTS, DataAgent, LLM, RAG, Sidecar, polling or input | Tasks 1 through 8 negative boundary tests and fixed scope |

## 11. Plan self-review performed before handoff

### Spec coverage

The plan covers every v0.0.1 scope item from the approved roadmap: private D root; final path/reparse validation; no C fallback; separate Host; /ingest and /render; Hello/Welcome; pairing/Origin/role/schema/kind constraints; minimal semantic GEP facts; freshness/gap/resync; HideAll and actual receipt; reconnect non-revival; raw payload memory-only handling; and target-machine go/no-go evidence.

The plan intentionally leaves the approved deferred areas untouched: advice, Panel, strategy, board geometry, Overlay arrows, voice, provider accounts, DataAgent, RulesPack, CompPack, LLM, RAG, SQLite, complete recovery, Sidecar, Named Pipe, polling, OCR, capture, input, injection, memory reading, and packet capture.

### Placeholder scan

The implementation tasks have explicit file paths, named types, tests, commands, expected outcomes, commit messages, external-gate behavior, and failure decisions. The three user-provided runtime values in ignored development configuration are intentionally required inputs: exact Overwolf Origin, a locally generated pairing token, and the official current GEP feature list. The Host and Bridge fail closed when they are absent.

### Type and lifecycle consistency

The type names used throughout are consistent:

~~~text
StorageHealth
StorageRootPolicy
SanitizedPocStatus
WireEnvelope
ChannelKind
PeerRole
MessageKind
HelloPayload
WelcomePayload
PocStateSnapshot
PocCapability
PocSessionReducer
PocSessionProjection
PocRenderLeaseCoordinator
OverlayCommand
OverlayReceipt
~~~

The render lifecycle is consistent in every task:

~~~text
new render lease
  -> Host HideAll
  -> real Hidden receipt
  -> post-lease authoritative StateSnapshot
  -> one ShowPocMarker
  -> any disconnect/rebuild invalidates the lease and starts again at HideAll
~~~

No task claims a target-machine result before the manual runbook is executed.
