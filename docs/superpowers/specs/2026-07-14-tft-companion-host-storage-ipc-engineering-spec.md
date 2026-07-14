# TFT Companion 宿主、存储与 IPC 工程规格

> **状态：** 已确认设计的工程规格；定义运行边界与跨进程合同，不授权搭建 Host、Overwolf 应用、Sidecar 或服务。

**目标：** 在不把游戏采集、策略、渲染、持久化和音频堆进同一进程/对象的前提下，建立可验证、可关闭、可降级的本机运行骨架。

**架构：** Overwolf Background Bridge 是 GEP 与物理 Overlay 的唯一边界；独立 `TftCompanionHost` 拥有受监督的运行时、事实、策略、Advice 和持久化域。Bridge 与 Host 通过一条 ingress 和一条 render 的物理隔离 Loopback WebSocket 通道协作；Panel 只读取 Projection 并向唯一 owner 发送受控命令。

**依赖：** [工程文档索引](2026-07-14-tft-companion-engineering-document-index.md)、[运行时与决策规格](2026-07-14-tft-companion-runtime-decision-engineering-spec.md)、[交付体验规格](2026-07-14-tft-companion-delivery-experience-engineering-spec.md)。

---

## 1. 物理拓扑与职责

```text
Overwolf TFT GEP
   │
   ▼
Overwolf Background Bridge ── /ingest ──> TftCompanionHost
   │                                         │
   │                                         ├─ IngressAndStateDomain
   └─ Overlay Renderer <── /render ──────────┼─ DecisionDomain
                                             ├─ DeliveryDomain
Alife Side Panel <── PanelProjection ────────┴─ RuntimeSupervisor
```

| 组件 | 唯一职责 | 明确禁止 |
|---|---|---|
| Overwolf Background Bridge | 注册/接收 GEP、最小化规范化、维护连接、转发语义事件、接收受限 RenderCommand、管理物理 Renderer 并回传事实 Receipt | 策略、DataAgent、SQLite、TTS、LLM、第三方攻略请求、游戏控制、原始 GEP 落盘。 |
| Overlay Renderer | 鼠标穿透窗口、Show/Hide、可见性事实、viewport/ROI 观察、真实 `OverlayShown`/`Hidden` 回执 | 读取 GEP、选择策略、生成语音、接受游戏点击、把 Accepted 当作 Delivered。 |
| TftCompanionHost | 生命周期、存储政策、IPC、LiveState、策略、Advice、受控 Delivery、诊断投影 | 直接操纵游戏窗口、恢复旧实时内容、把全局 Alife 服务当万能依赖。 |
| Alife Side Panel | 展示 Projection、历史/说明、用户设置和 `Mute`/`Skip`/校准等受控命令 | 直接播放音频、直接给 Renderer 像素命令、改写 Advice/游戏事实、把用户输入发给攻略来源。 |

不引入首版常驻 Sidecar、通用事件总线、文件投递、SQLite polling、HTTP 长轮询或“所有模块都在大 Host 内彼此调用”的结构。若 G1 Loopback Gate 失败，是否评估最小 Sidecar + Named Pipe 是新的设计审阅，不是自动回退。

## 2. G0：私有存储根与唯一写者

### 2.1 根路径政策

所有 TFT 可控写入必须通过 `StorageRootPolicy` 校验最终卷、符号链接和目录联接点后，解析到：

```text
D:\AlifeData\TFTCompanion\
```

字符串前缀检查不足以防止目录联接点逃逸。SQLite 的 `-wal`/`-shm`、缓存索引、staging、tombstone、日志和清理记录都属于可控写入，不能由模块自行拼接路径或调用默认 SDK 目录。

| 命名空间 | 唯一 owner | 允许内容 |
|---|---|---|
| `data\` | Host 运行时持久化域 | 最小 Projection、checkpoint、宿主状态数据库。 |
| `dataagent\` | TftDataAgent | ledger、证据 Projection、知识操作 staging 与完整性检查。 |
| `packs\` / `knowledge\` | TftDataAgent | 已授权原始包/Manifest；规范化事实、索引、chunk、可选本地 embedding。 |
| `cache\voice\` | GameVoiceScheduler | 有 TTL、容量、引用计数/租约的游戏语音制品。 |
| `settings\` | ProfileAndStorageStore | 非秘密偏好、校准、credentialRef；密钥 blob 只可进入 `settings\credentials\`。 |
| `logs\` / `diagnostics\` / `exports\` | DiagnosticsSink / DiagnosticExportPolicy | 白名单、脱敏、有界运行诊断和用户显式导出。 |
| `backups\` / `temp\` / `update-staging\` | 对应受控 owner | 非秘密恢复元数据或有 TTL 的暂存；不得保存原始对局、凭据、已删除内容或可重新引入已清除知识的包。 |
| `Skills\` | Expression Skill loader | 已验证、版本化的专属表达 Skill。 |

D 盘不可用、满盘、根路径异常、WAL 失败或完整性检查失败时，分别进入 `MemoryOnlyDegraded`、`PersistUnavailable` 或 `IntegrityDegraded`。唯一允许的结果是关闭依赖持久化的能力、保留内存中仍可验证的实时核心，或者安全退出；绝不写入 C 盘、AppData、Temp 或 `AlifePath`。

### 2.2 数据最小化与保留

默认不保存 Riot ID、真实名、跨局稳定身份、聊天、麦克风音频、连续键盘流、全屏录像或原始 GEP。普通持久化建议预算为约 2GB、D 盘保留至少 5GB 安全空间、普通对局约 30 天或 200 场、规范化事件约 14 天；原始诊断默认关闭且短期保留。所有阈值都必须在实际实现前后测量，不可把此处的建议值硬编码为永远正确。

## 3. G1：双通道 Loopback IPC

### 3.1 协议方向

`/ingest` 和 `/render` 是物理隔离的双向 Loopback WebSocket 通道。它们可共享受限握手机制，但不能在同一连接上混入“源端事实”和“主机意图”，以减少重连、背压、权限与回执语义的混淆。

| 通道 | Bridge → Host | Host → Bridge | 不允许的负载 |
|---|---|---|---|
| `/ingest` | `Hello`、能力/连接事实、规范化 GEP 事件、`StateSnapshot`、窗口/Viewport 事实、Gap、健康状态 | `Welcome`、订阅/重同步请求、限流/关闭命令 | 策略结论、数据库命令、TTS/LLM 请求、原始持久化负载。 |
| `/render` | Renderer 真实 Receipt、实际可见性、资源释放、Renderer 健康 | `HideAll`、受限 `Show`/`Update`、render lease、终态确认 | 原始游戏事实、业务决策修改、任意脚本或游戏输入。 |

每个 Envelope 至少携带协议版本、消息类型、`messageId`、`connectionEpoch`、单调时间、相关 session/lease 身份和可审计的 `reasonCode`。Host 接收外部消息时分配 `hostIngressSequence`，并以 `(ingestMonotonicTime, hostIngressSequence)` 形成权威外部顺序；不能让 WebSocket 线程调度、墙钟或字典遍历决定领域结果。

### 3.2 握手与最小权限

G1 PoC 必须证明 current Overwolf Native Runtime 下 manifest、CSP、Origin、Loopback、Renderer 重建和 GEP 注册顺序可工作。握手至少验证：

- 明确的协议/Schema 兼容版本；
- 每次连接的 `connectionEpoch` 和配对材料；
- Loopback 来源、Origin、角色和 token/配对材料；
- Render 连接在开始接收 Show 前先建立 `HideAll` 基线；
- 不受信、旧 epoch、错误角色、错误 Origin、超大或不兼容帧被拒绝并只留下脱敏 `reasonCode`。

G1 失败时不得换成 HTTP polling、文件投递或 SQLite polling。只有重新批准的 Sidecar + Named Pipe 调研可以作为下一步，且它需重新评估 Job Object、关闭、权限、诊断和成本。

### 3.3 传输可靠性与恢复

传输可以是至少一次，但领域结果必须幂等：重复、乱序、ACK 丢失、Bridge/Host 轮流重启、ring overflow 或 sequence gap 均不能使旧内容复活。gap 的标准处理为：关闭依赖实时事实的 scope、撤回精确视觉、取消未播自动语音、标记 `ResyncRequired`、请求完整 `getInfo()`/StateSnapshot、建立新 `stateRevision` 后再允许新决策。

## 4. G2/G3：渲染租约与 Viewport 边界

Host 只拥有“希望显示的逻辑内容”和“租约是否仍有效”；Renderer 才拥有真实屏幕状态。`Accepted`、`OverlayShown` 与 `Delivered` 是不同事实：发送命令、Renderer 接受、物理窗口实际显示、Advice 的业务交付不能相互偷换。

每条精确 RenderCommand 必须绑定当前的 `runtimeEpoch`、`sessionId`、`sessionEpoch`、`roundKey`、`adviceId`/`adviceRevision`、`deliveryId`、`renderLeaseId` 和 `viewGeneration`。任何一个身份过期，Renderer 应 `HideAll` 或拒绝，不能补齐旧值。

窗口贴合与棋盘贴合是不同问题：

1. Renderer 先将透明根窗口贴合游戏表面和 DPI/显示器坐标。
2. `ViewportTransformTracker` 才判断游戏内缩放、ROI、动画、遮挡和棋盘语义锚点是否足以把逻辑格位投影为像素。
3. `High` 才允许精确格、箭头和短标签；`Medium`/`Low` 只能保留 Edge Dock 和 Panel 的方向性提示。

缩放、窗口/DPI/显示器变化、ROI 漂移、Bridge 断开、lease 失效、Advice supersede、会话结束或失焦战斗阶段必须先失效旧 `viewGeneration` 并撤回精确层，之后才可重新获得 High。透明层必须永远鼠标穿透；人工校准也只能通过 Panel 预览，不得向游戏发送点击。

## 5. RuntimeSupervisor、生命周期与健康

### 5.1 状态所有权

| 状态域 | 唯一 owner |
|---|---|
| `runtimeEpoch`、启动/关闭 gate、全局取消、可选 Sidecar 生命周期 | RuntimeSupervisor |
| 连接、握手、协议、`hostIngressSequence`、Bridge 健康 | BridgeLinkSupervisor / IPCGateway |
| session/round 归属 | SessionRoundController |
| 当前游戏事实与 `stateRevision` | LiveStateReducer |
| 字段可用性、schema、gap/resync | CapabilityMatrix |
| viewport 几何、`viewGeneration`、置信度 | ViewportTransformTracker |
| 已验证知识与持久化 Projection | TftDataAgent |
| 用户偏好、专属 Profile 与 storage 根政策 | ProfileAndStorageStore |
| Advice/Delivery 语义 | AdviceCoordinator |
| 游戏教练语音队列和播放租约 | GameVoiceScheduler |

### 5.2 生命周期

`RuntimePhase` 固定为 `Stopped → Bootstrapping → Ready → Stopping → Stopped`。Bootstrapping 生成新 `runtimeEpoch`、验证 D 盘根、加载已验证静态快照，并默认不存在有效租约；它绝不恢复旧 Advice、语音或箭头。Ready 可等待 Bridge/大厅，但在完整快照前不得生成实时建议。

关闭固定按下面顺序进行：

```text
拒绝新实时决策
→ AdviceCoordinator 终态化当前 scope
→ Overlay HideAll 与自动 Voice Cancel 并行
→ 在有界 deadline 内等待真实 receipt / outputReleased
→ 清空实时内存与过期制品引用
→ 异步完成最小 D 盘收尾
→ 关闭 IPC、句柄和可选受控子进程
```

任何等待超时只产生脱敏原因和通道降级，不能卡住退出。Renderer 在 render 连接断开、Host epoch 过期或 lease 超时时必须自行隐藏，Host 不能假设最后一次 Hide 命令一定会送达。

健康状态与生命周期正交：`StorageHealth`、`BridgeHealth`、`ViewportHealth`、`KnowledgeHealth` 和 `VoiceHealth` 分别降级。D 盘异常、Viewport Low 或 LLM 异常都不能无理由结束一条仍可安全运行的会话。

## 6. 安全、隐私与宿主集成边界

- Bridge、Host、Panel 和 Renderer 不存储/转发账号、聊天、音频、截图、原始游戏帧或连续视觉流。
- Diagnostics 只接收白名单、脱敏、容量受限 Projection；它不能反向影响实时决策。
- 单独的 TTS/LLM/攻略来源凭据均在各自专属 Profile/Store 中，不复用 QQ、通用 Alife 客户端、攻略来源或跨宿主市场凭据。
- 跨宿主插件注册、适配器 DLL、安装/升级包属于独立的跨宿主工程。TFT Host 不可因此加载另一个宿主的 Adapter DLL，也不依赖默认 `CompatibilityMode`。

## 7. 验收与版本映射

| Gate | 最小证据 | 失败后的正确状态 |
|---|---|---|
| G0 | 每一种写入解析到 D 盘私有根；D 盘失败无 C 盘副作用 | `MemoryOnlyDegraded` 或关闭持久化能力。 |
| G1 | 双通道握手、GEP 语义传输、Origin/CSP/manifest、Renderer 重建时 HideAll | 停止，不使用 polling 替代。 |
| G2 | 事件 → Reducer → Advice → Render Intent → 真实 Receipt 的最小链 | Panel/Dock 安全降级，不启用精确棋盘层。 |
| G3 | DPI/窗口/缩放/ROI 变化后旧精确层先消失，High 后才显示 | 永久 Panel/Dock 模式，不强行贴棋盘。 |

`v0.0.1` 只验证 G0–G2 的最小子集，`v0.2.0` 验证窄范围 G3，后续版本逐步扩展 Host、Overlay 和恢复合同。跨进程、D 盘与生命周期的完整实证在 `v1.3.0` 和 `v2.0.0` 前不得被视为完成。版本顺序和人日范围见[完整版本路线图](../plans/2026-07-14-tft-companion-complete-system-version-roadmap.md)。
