# TFT Companion v0.0.1 — 目标机器兼容性 Gate

## 目的与边界

本手册只验证受限的本机链路：`.NET Host ↔ Overwolf Background Bridge ↔ Renderer`。

它不是插件发布手册，也不授权运行排位、控制游戏、读取内存、注入、抓包、截图、OCR、键鼠/ADB 输入、第三方网络请求或修改 `D:\Alife`。首次目标机验证只允许在自定义/练习对局中进行，并且必须单独获得执行授权。

在本 Gate 没有真实证据前，v0.0.1 状态只能是 `LocalImplementationReady / TargetMachineGatePending`，不能标为 Go，也不能进入 v0.1.0 实现阶段。

## 架构合同

```text
Background Bridge
  ├─ ws://127.0.0.1:32173/ingest
  └─ ws://127.0.0.1:32173/render
       ↓（仅固定内部命令 + 回执）
Renderer
```

- 只有 Background Bridge 持有两条 Host WebSocket；Renderer **绝不**直接连接 Host。
- 两条 Hello 使用同一个 `bridgeInstanceId` 和同一个 43 字符 base64url pairing proof；proof 不得进入 URL、日志、状态文件、截图或 Git。
- Host 只接受精确的真实 HTTP `Origin`、精确 body Origin、`127.0.0.1`、空 query、已知协议/schema、白名单消息类型和每连接受限的消息速率。
- Host 只接收 `stateSnapshot` 的最小布尔语义字段，不保存或投递原始 GEP JSON。
- 每次 render 连接首先收到 `hideAll`。每个 command 与 receipt 都必须完整回显 `runtimeInstanceId + connectionEpoch + commandSequence + sessionId + renderLeaseId + commandId`。只有同一完整 identity 的 `hidden` 回执**及其后的**权威快照都成立，且 `matchObserved`、`roundObserved`、freshness 都有效时，Host 才允许 `showMarker`。

## 0. 本地 Gate（无需游戏）

在 `D:\TFTCompanion` 执行：

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' restore .\TftCompanion.Poc.slnx
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build .\TftCompanion.Poc.slnx --no-restore --configuration Release
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test .\TftCompanion.Poc.slnx --no-restore --configuration Release
node --test .\tests\overwolf\static-boundary-check.js
Push-Location .\overwolf\tft-companion-poc
npm test
Pop-Location
git diff --check
```

通过条件：

- .NET build 为 0 warning / 0 error；
- 全部 NUnit 测试通过；
- Node 静态边界检查通过；
- `npm test` 从 bundle 目录能够正确找到同一份静态检查；
- `git diff --check` 无输出；
- 本地检查没有启动 Overwolf、游戏或外部 API。

## 1. 目标机前置条件

以下动作均需在执行 Gate 前逐项确认：

1. 当前官方 Overwolf manifest schema 已核验；现有 `manifest.json` 不得因“兼容”而添加 input、capture、memory、注入、任意网络或远程内容权限。
2. 已确认当前 PC TFT GEP 的 `game_id`、feature allowlist 和实际字段路径。不得凭记忆填写。
3. 已确认本机加载的 Overwolf app 的**精确** HTTP Origin。
4. D 盘可用；插件可控持久化只允许为 `D:\AlifeData\TFTCompanion\poc-status.json`。D 盘不可用时必须 MemoryOnlyDegraded，绝无 C 盘/AppData/Temp 回退。
5. 已有合法自定义/练习对局和现有 Overwolf 开发加载能力。

记录时只记录：Windows 构建、.NET SDK、Overwolf runtime、TFT patch/set、manifest 版本、feature 名称、配置的非秘密标识和 Pass/Fail/BlockedExternal。不得记录 Riot ID、玩家名、真实 match ID、token、Origin 原文、原始事件、截图或控制台原文。

## 2. 创建本机配置（手工，不提交）

从 `overwolf\tft-companion-poc\dev-settings.example.js` 复制出同目录的 `dev-settings.local.js`。该文件被 Git 忽略，必须只在目标机创建。

填写并人工复核：

- `host` 必须恰为 `ws://127.0.0.1:32173`；
- `allowedOrigin` 为已实测的精确 Overwolf Origin；
- `pairingToken` 为新生成的 32-byte base64url token（43 字符）；
- `requiredFeatures` 为当前官方文档与目标机验证过的非空 feature allowlist；
- `matchObservedPath`、`roundObservedPath` 为当前官方验证过的、只用于存在性判断的字段路径。

缺失、空白、非精确 host、无效 token、空 feature 或无效路径必须让 Bridge 不连接、不注册 GEP listener、不显示 marker。不要把本机设置复制到问题单、日志或 Git。

## 3. 启动 Host

在 `D:\TFTCompanion` 以与本机配置相同的 Origin 和 pairing proof 启动：

```powershell
$env:TFT_COMPANION_POC_ALLOWED_ORIGIN = 'the-exact-origin-from-local-settings'
$env:TFT_COMPANION_POC_PAIRING_TOKEN = 'the-same-43-character-base64url-token'
& 'C:\Users\hu shu\.dotnet\dotnet.exe' run --project .\src\TftCompanion.Poc.Host\TftCompanion.Poc.Host.csproj --no-build -- --port 32173
```

通过条件：控制台只输出 `TFTPOC_READY`。缺少配置或错误端口必须只输出命名的 `TFTPOC_CONFIG_REJECTED:<code>`，不得输出 secret、Origin、请求体或 GEP 对象。

## 4. 加载 PoC 与双通道验证

1. 使用已有 Overwolf 开发工具加载 `D:\TFTCompanion\overwolf\tft-companion-poc\`。
2. 在不进入游戏前确认 Background Bridge 无本机配置时 fail-closed；配置有效后才允许连接。
3. 在自定义/练习对局中确认 Bridge 先连 `/ingest`，再以相同 `bridgeInstanceId` 连 `/render`。
4. 确认 Renderer **没有**自己的 Host WebSocket。
5. 确认新 render lease 的首个命令是 `hideAll`；Renderer 仅在真正隐藏后回传同一 `runtimeInstanceId`、`connectionEpoch`、`commandSequence`、`sessionId`、`renderLeaseId`、`commandId` 的 `hidden`。
6. 确认错 Origin、错 pairing、错 route/channel、错 runtime/epoch/sequence/session/lease/command、二进制帧、超过 64 KiB 的 text frame 与超速消息都被拒绝；记录命名错误码，不记录消息内容。

## 5. GEP 最小事实与显示 Gate

1. 确认 `setRequiredFeatures` callback、`onNewEvents`、`onInfoUpdates2`、`getInfo` 的运行时可用性；此处是外部平台兼容性证据，不能由静态检查替代。
2. 仅在 `getInfo` 产生的权威快照中确认 Bridge 发送：`matchObserved`、`roundObserved`、`isAuthoritativeSnapshot`、单调 sequence。不得转发源对象或任意字段值。
3. 确认 `hidden` 后若没有当前权威快照，marker 仍保持隐藏。
4. 确认当前 lease 之后的权威快照同时报告 match/round observed，且 Host freshness 有效时，才收到一个 `showMarker`。
5. 断开 ingest 或 render、重载 Bridge/Renderer、发送旧 runtime/epoch/sequence receipt、制造 sequence gap 后，确认先隐藏；旧 marker、旧 receipt 和旧 session 绝不能复活显示。
6. 等待 freshness TTL 过期，确认 Host 重新 HideAll 而非让 marker 残留。

如果任何真实 GEP 字段、Origin、manifest 属性、窗口消息 API 或窗口级 mouse passthrough 无法核实，填 `BlockedExternal` 或 `No-Go`；不得以猜测字段、HTTP polling、文件中转、SQLite polling 或 Sidecar 代替。

## 6. 鼠标穿透与存储

- Renderer 必须同时具备 manifest clickthrough、CSS `pointer-events: none` 和 `setMouseGrab(..., false)` 的成功回调；回调失败/未知时必须保持隐藏。
- 在 marker 可见时点击 marker 区域，确认不会夺取焦点或吞掉输入。该项失败即 No-Go。
- 检查 `D:\AlifeData\TFTCompanion\poc-status.json`（若 D 盘可用）：文件不超过 8192 bytes，只含 runtime epoch、bridge/render online、match/round observed、freshness、gap 和命名错误码。
- D 盘不可写的自动化 fake 是本地无回退证据；真实机器上若要验证，应仅观察 Host 进程的文件 I/O，不能把 Chromium/Overwolf 自身缓存归因给插件。

## 7. 结论

仅当本地 Gate 和全部目标机 Gate 都为 Pass，v0.0.1 才可标为 `Go`。任一 No-Go / Fail / BlockedExternal 都阻断 v0.1.0 的功能实现；先修复或重新审查 v0.0.1 基线。
