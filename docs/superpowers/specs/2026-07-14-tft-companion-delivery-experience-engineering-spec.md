# TFT Companion 交付体验工程规格

> **状态：** 已确认设计的工程规格；定义如何以陪伴型方式交付已仲裁结论，不授权实现 UI、音频、Overlay 或 Provider 适配器。

**目标：** 让同一份已验证的战略结论以 Panel、High-confidence Overlay 和可选独立游戏语音三种通道交付，同时保证少打扰、真实回执、可撤销和用户控制优先。

**架构：** 决策域只输出 `StrategicDecision`；`InterventionPolicy`、`AdviceSemanticCompiler`、专属表达 Skill 与 `ExpressionContractValidator` 只负责合法表达；`AdviceCoordinator` 是 Advice/Delivery 唯一写者。Voice、Renderer 和 Panel 只能报告事实 Receipt，不能自行宣布“已交付”。

**依赖：** [工程文档索引](2026-07-14-tft-companion-engineering-document-index.md)、[运行时与决策规格](2026-07-14-tft-companion-runtime-decision-engineering-spec.md)、[宿主/存储/IPC 规格](2026-07-14-tft-companion-host-storage-ipc-engineering-spec.md)、[质量与发布规格](2026-07-14-tft-companion-quality-release-engineering-spec.md)。

---

## 1. 体验原则与通道策略

默认体验是“安静的陪伴教练”：系统可以持续分析，但普通变化只更新 Panel；显著变化最多给一句短语音；关键生存风险或重大异常可主动提醒；用户主动问时才允许展开；沉默本身是正确输出。

| 通道 | 默认角色 | 不可替代的边界 |
|---|---|---|
| Panel | 首要、完整、可追溯的信息面；显示当前/历史/未知/已过期与受限原因 | 可以保留历史解释，但不能把旧 Advice 标为当前或直控播放器。 |
| Overlay | High 置信度下的极简棋盘格、最多 1–2 个箭头和必要短因；Medium/Low 时降级为 Edge Dock | 永远鼠标穿透；只能显示逻辑站位经过 Viewport 投影的结果。 |
| Game Voice | 可选、短、过期即丢弃的注意力提示 | 不与 QQ 语音共享队列/Provider/缓存/租约，不能阻塞 Panel 或 Overlay。 |

每个备战阶段通常至多一条主动战略语音，且不要求用完配额。普通陪伴消息不跨阶段排队，不因等待太久而升级优先级，也不为“有话可说”消耗注意力。

## 2. 表达 Skill：策略与话术分离

专属 Skill 固定为：

```text
render-tft-companion-advice
```

它位于经 `StorageRootPolicy` 校验的 `D:\AlifeData\TFTCompanion\Skills\render-tft-companion-advice\`，局内冻结版本。业务代码只提交结构化语义和锁定槽位，例如 `HIGH_COST_UNIT`、`OBSERVED_COPIES`、`GOLD_FLOOR`、`ACTION`、`STOP_CONDITION`；Skill 决定尊重、简短、低打扰的说法。

Skill 可以产生同一 `semanticDigest` 下的 Panel、Overlay、Voice 候选，但不能修改：

- 数字、金币底线、行动、优先级、有效期、站位格、停止条件或失败回退；
- `StrategicDecision`、`replaceKey`、`adviceRevision`、会话身份或用户控制状态；
- Provider、网络、工具、文件、游戏和任何外部服务。

每个表达候选都要带 `SkillVersion`、`messageKey`、slot schema、锁定槽位、通道存在/缺失状态和 `semanticDigest`。未完成、过期、被拒绝或不适用于某通道时，字段必须显式为 `absent`，不得用旧文本或另一个 Advice 的内容补齐。

### 2.1 FastPath

FastPath 是已验证本地事实到模板表达的确定性链：

```text
StrategicDecision
→ FastPathExpressionSkill（Skill 内已编译、版本化模板层）
→ locked slots + versioned messageKey
→ ExpressionContractValidator
→ AdviceCoordinator
```

它不调用远程/本地生成式 LLM、TftDataAgent 分析、SQLite 查找、RAG、embedding、攻略 API 或网络。目标为表达 P95 不超过 10ms，且断网、无 Grok、DataAgent/SQLite 故障时仍可安全输出或沉默。

### 2.2 RichPath 的位置

RichPath 仅供用户主动追问、复杂转型解释、回合总结或赛后复盘使用；它不是 FastPath 的回退，也不可以比本地 P1 风险更有权威。其完整最小化上下文和验证规则见[知识、LLM 与证据规格](2026-07-14-tft-companion-knowledge-llm-evidence-engineering-spec.md)。无论 RichPath 是否启用，模型草案都只能表达已经锁定的结论，不能反向改变数值或通道控制。

## 3. AdviceCoordinator：统一语义父对象与分通道 Delivery

### 3.1 Advice 基础合同

`Proposed`/`Eligible` Advice 至少具有：

```text
runtimeEpoch
sessionId / sessionEpoch / roundKey
adviceId / adviceRevision / logicalAdviceKey / replaceKey / supersedesAdviceId
decisionSnapshotId / ingressStateSnapshotId / knowledgeSnapshotId / stateRevision
scopeKey / scopeKind / expiryBehavior / semanticDigest / priorityClass
createdAt / validFrom / expiresAt / strategicObjective / sourceCandidateIds
```

`voiceText`、`overlayText` 和 `panelText` 不属于基础 Advice；它们只能存在于通过 Validator 的 `AdviceExpression` 或某一 Delivery 载荷中。这样可防止未完成表达、旧 revision 或错误 Skill 在新状态里复活。

父 Advice 状态为：

```text
Proposed → Eligible → Expressing → Dispatching → Active → Closing → Closed
```

`AdviceCoordinator` 使用单写者串行循环处理 Fact、Command 和 Receipt，并为其接受的事件分配 `coordinatorSequence`。它是唯一可将父 Advice 或 Delivery 标为 Delivered、Interrupted、Superseded、Expired、Rejected、Failed 等终态的组件。

### 3.2 scope、优先级与替换

`scopeKey` 是 Current、Supersede 与 Delivery 检查使用的唯一冲突范围键，至少编码 session、round、decision window 和 stage；`scopeKind` 只能是 `DecisionWindowBound`、`RoundBound`、`SessionBound` 或 `UserRequestBound`，不能替代 key。

优先级只描述游戏插件内的内容：P1 `EmergencySafety`、P2 `UserInitiated`、P3 `CriticalTactical`、P4 `OrdinaryCoach`、P5 `Companion`。`replaceKey` 用于仲裁资源/语义冲突，不能默认等于 `logicalAdviceKey`。同一 digest 不应跨通道反复轰炸；用户主动追问可软接管普通语音，但不必清空有效且不冲突的 Panel/Overlay。

新 revision 的取代采用两阶段语义：先使旧 Advice 不再 Current、冻结其新 Delivery，再由既有通道完成受控 Hide/Cancel/Receipt。旧回执不可在新 revision 之后重新开启显示或音频。

### 3.3 Delivery 与真实回执

每个通道产生独立 `Delivery`，携带 Advice 身份、delivery/attempt 身份、scope、deadline 和通道特有租约。标准 Receipt 是事实，不是状态写权限：

| 通道 | `Accepted` 的含义 | Delivered 的最低条件 |
|---|---|---|
| Panel | Projection 已接受 | 当前 revision 对应 Panel 状态已经可见且未被 supersede。 |
| Overlay | Bridge/Renderer 已接受命令 | Renderer 报告匹配 lease 和 `viewGeneration` 的物理 `OverlayShown`；必要时确认可见/撤回终态。 |
| Voice | TTS 制品已准备或播放请求已发出 | 所有必要片段自然完成，且最后一个片段真实结束；部分完成后软停止属于 Interrupted。 |

父 Advice 的 `FullyDelivered`、`PartiallyDelivered` 或 `Undelivered` 由 AdviceCoordinator 根据各 Delivery 的真实终态计算，绝不能由命令发出时间或固定延迟猜测。

## 4. Panel 与 Overlay 合同

### 4.1 Panel

Panel 显示当前主行动、支持行动、关键观察、有效期、未知/过期/降级原因、来源/版本摘要和用户可用控制。它应展示“为什么不能精确回答”，而不是以空白或旧内容假装正常。

Panel 可以保存受限历史，但当前区域的 revision 必须与 AdviceCoordinator 相同；session 变化、gap、supersede、scope 结束或过期后旧结论不得继续显示为当前。Panel 通过 `ControlRouter` 发送 `VoiceControlCommand`、校准请求或偏好改变，不可直接操作 TTS/Renderer。

### 4.2 Overlay

Overlay 的唯一功能是以尽可能少的视觉打断展示已经成立的建议：棋盘格、最多 1–2 个移动箭头和短原因。它不增加策略类别、不做实时全棋盘指挥、不显示候选对手身份，也不自行对 GEP/攻略内容作解释。

精确层的显示条件必须同时满足：Current Advice revision、当前 session/round/render lease、当前 `viewGeneration`、Viewport High、没有 gap、实时事实新鲜、兼容的知识快照和未过期 TTL。任一条件失效时先 `HideExactBoardInlay`，保留的只能是 Panel 或 Edge Dock 的方向性内容。

窗口贴合、棋盘 ROI、缩放、DPI、多显示器和校准属于宿主/Viewport 域；逻辑站位来自决策域。Overlay 绝不把屏幕像素反向当作策略事实，也不以校准、热键或透明窗口接收游戏鼠标输入。

## 5. GameVoiceScheduler：独立、可取消、过期即丢弃

### 5.1 独立语音链

```text
AdviceCoordinator
→ GameVoiceScheduler
→ IGameCoachTtsProvider
→ ICoachPlaybackAdapter
→ typed PlaybackTerminalReceipt
```

QQ 聊天继续使用其本地 `SpeechService`、TTS、队列、缓存、预算和播放逻辑。用户的使用方式保证 QQ 与游戏语音不同时播放；插件不检测、等待、取消或抢占 QQ，也不建立跨来源的 `AudioOutputGate`。游戏链只保证自身资源完全隔离。

`IGameCoachTtsProvider` 输入仅允许已验证的短 `VoiceSegment`、无身份的 attempt/segment 标识、`voiceProfileId`、语速/情绪枚举、输出格式和单调 deadline。禁止发送账号、match ID、GEP、棋盘、对手、商店、牌库、用户问题、QQ 内容、路径、诊断、Cookie、Token 或完整日志。凭据只在专属 `GameCoachCredentialStore` 中以 DPAPI 保护的 blob 保存；语音诊断只记录 providerId、长度/延迟桶、content hash、状态和 reasonCode。

### 5.2 语义切片、准备与播放

Skill 输出结构化 `VoiceUtterancePlan`。普通 Advice 默认 1–3 片、每片约 0.8–2.5 秒、总时长通常不超过 6 秒；更长解释进 Panel 或 RichPath。每片带 `segmentId`、文本、语义边界、预估时长、`canEndAfterThisSegment`、`mustStayWithNext`、优先级、过期时间和 digest。

完整设计的资源上限是：

- 一个 Active Voice Delivery；最多四个 Pending Voice Delivery；当前 Delivery 最多预取下一语义片段。
- 同时最多一个 `PreparingCandidate`；默认 TTS 正常并发为一。
- 仅 P1 在旧低优先级请求已取消但未在宽限内退出、Provider 支持且限流/熔断允许时，可短暂使用第二个紧急合成槽；P2 不能使用该旁路。
- 除当前播放片外，未播放音频制品最多两个：当前 Active 的下一片和唯一 Pending 的首片。其余 Pending 只保存结构化计划，不抢先消耗 TTS 或成本。

TTS 成功只表示 `TtsArtifact` 可用，绝不表示播放成功。每个制品返回时、进入 `ReadyToPlay` 前、真正开始播放前均复检 runtime/session/advice/delivery/attempt/segment/scope/deadline/Current revision/VoiceHealth 身份。迟到或已过期制品进入 `cache\voice\` 的有界清理，绝不重回队列。

`ICoachPlaybackAdapter` 必须区分 `CompletedNaturally`、`Interrupted`、`CancelledBeforeStart`、`DeviceLost`、`DecodeFailed` 与 `TimedOut`。新语音不能仅凭 `Stop()` 或固定 `Task.Delay` 假定旧播放资源已释放；必须等到真实终态或有界超时降级。

### 5.3 排队、抢占与用户控制

队列进入、替换和播放选择遵守以下确定性次序：删除失效项 → 高 priority 优先 → 同级按更早 `expiresAtMonotonic` → 再按更早 `coordinatorSequence`。相同 `replaceKey` 的新 revision 原子替换 Pending 旧项；相同 digest 不再重复播报；队满时只在新项严格优于最弱项时替换，淘汰项获得明确终态。

P1 可请求快速停止普通片；P2 在语义边界软接管 P3–P5；P3 软接管 P4–P5；P4 只能接管 P5；P5 永不主动抢占。无论优先级如何，新播放都必须等待旧 `CoachPlaybackLease` 的真实释放。

统一用户控制包括 `MuteUntil`、`SkipCurrentAdvice`、`CancelAutomaticQueue`、一次性朗读、音量和陪伴强度。Mute/Skip 是最终优先级：不影响 Panel/Overlay，但不能被同 revision 补播、旧 callback 或队列重排绕过。控制命令经 `ControlRouter` 到 Scheduler，UI/热键不直接操作播放适配器。

### 5.4 窄版本与完整版本

`v0.3.0` 只允许一个已配置 Provider、一个 Active 加最多一个 Pending、无投机预取、过期即丢弃。完整四 Pending、语义预取、P1 旁路、Provider 路由/熔断/费用、设备恢复和 `VoiceTimingProfile` 只能在 `v1.2.0` 及其 Gate 后进入。

TTS 失败、超时、设备异常或熔断时默认不回退到 QQ 的本地 TTS；Voice 终态化或降级，Panel 和 Overlay 继续。生成制品只可位于 `D:\AlifeData\TFTCompanion\cache\voice\`，D 盘不可写时使用内存或无缓存降级。

## 6. 失败、性能与验收

| 情形 | 必须结果 | 禁止结果 |
|---|---|---|
| supersede、scope 结束、session/gap/expiry | 未播 Voice 取消；旧 render lease 失效；旧 Panel current 移除 | 旧文本、箭头或语音之后复活。 |
| Overlay 低置信/缩放/断线 | 精确层先隐藏；Dock/Panel 允许保留 | 旧像素箭头继续留在游戏棋盘上。 |
| TTS 迟到、取消不响应、设备丢失 | 按 attempt 隔离，过期即丢弃，VoiceHealth 降级 | 补播、无限等待、阻塞下一 Advice 或回退 QQ。 |
| 用户 Mute/Skip | 真实 receipt、对应 Delivery 终态；Panel/Overlay 不受影响 | 直接操作播放器、静音后自动补播。 |
| Skill 缺失/无效/超时 | 通道显式 absent 或静默；记录脱敏原因 | 使用旧表达、编造文本、改变策略数值。 |

关键本机验收目标包括：AdviceCoordinator 状态转移 P50 ≤ 2ms、P95 ≤ 5ms、P99 ≤ 15ms；FastPath 表达 P95 ≤ 10ms；Supersede 到取消未播放 Voice P95 ≤ 20ms；单次事件循环不可中断占用 ≤ 20ms。远程 TTS 延迟不计入 AdviceCoordinator，但任何段落必须在 `latestUsefulVoiceStart` 前仍有效，否则必须放弃。

完整回放必须覆盖：同 replaceKey 新旧 revision、Renderer 迟到 ACK、TTS 迟到 artifact、用户 Mute/Skip 与 P1 并发、输出释放超时、缩放后 HideAll、和所有旧身份代际的僵尸回调。具体 P0/P1 场景见[质量与发布规格](2026-07-14-tft-companion-quality-release-engineering-spec.md)。

## 7. 版本映射

| 版本 | 允许交付能力 | 明确未开启 |
|---|---|---|
| v0.1.0 | Panel-first、FastPath 模板、Advice 的最小 current/expired 语义 | 精确 Overlay、游戏 Voice、RichPath、完整 Delivery 生命周期。 |
| v0.2.0 | High-only Overlay、真实 render receipt、Panel/Dock 降级 | 新策略类别、复杂校准、Voice。 |
| v0.3.0 | 单 Provider、1 Active + 1 Pending、短段落、最终 Mute/Skip | 多 Provider、预取、hedge、复杂 timing/设备恢复。 |
| v1.1.0 | AdviceCoordinator 的完整父/Delivery、supersede、关闭和回执合同 | 不能因此自动启用 LLM 或多 Provider。 |
| v1.2.0 | 完整游戏 Voice 调度、队列、准备、路由、deadline、设备与控制 | 仍不共享 QQ 播放状态。 |
| v1.3.0 | 扩展 Overlay/Host 兼容与校准、全显示环境验证 | 不承诺无限显示配置或 OCR 实时观战。 |

各版本的 Gate、工作量和停止条件见[完整版本路线图](../plans/2026-07-14-tft-companion-complete-system-version-roadmap.md)。
