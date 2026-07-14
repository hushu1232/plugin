# TFT Companion 质量、回放与发布工程规格

> **状态：** 已确认设计的工程规格；定义验证证据和发布门，不授权采集真实对局、运行性能测试、创建测试工程或发布任何制品。

**目标：** 让“只读、低打扰、未知时不误导、旧内容不复活、D 盘不回退、外部数据零泄漏”成为可重复验证的工程事实，而不是人工感觉或 UI 文案。

**架构：** 以合成、匿名、脱敏的 `TftReplayCapsule` 驱动真实核心逻辑和受控 fake 外部端口；使用虚拟单调时钟避免网络、线程调度、真实窗口和 Provider 时序影响结果。目标机自定义局 smoke、真实 IPC/Renderer 回执、性能 Harness 和确定性回放彼此分层，不能互相冒充通过。

**依赖：** [工程文档索引](2026-07-14-tft-companion-engineering-document-index.md)、[运行时与决策规格](2026-07-14-tft-companion-runtime-decision-engineering-spec.md)、[宿主/存储/IPC 规格](2026-07-14-tft-companion-host-storage-ipc-engineering-spec.md)、[交付体验规格](2026-07-14-tft-companion-delivery-experience-engineering-spec.md)、[知识/LLM/证据规格](2026-07-14-tft-companion-knowledge-llm-evidence-engineering-spec.md)。

---

## 1. 验证分层

| 层级 | 验证什么 | 不能替代什么 |
|---|---|---|
| 纯领域/属性测试 | 状态机、身份失效、候选仲裁、队列顺序、路径解析、schema/manifest 与固定输入输出 | 真实 Overwolf/IPC、DPI/缩放、Provider、桌面窗口。 |
| 确定性 Replay | 端到端核心链路、僵尸回调、降级 oracle、强类型 outcome、故障注入 | 真实设备延迟、Overwolf 上游延迟、真实音频/窗口行为。 |
| 受控集成 | 双通道 IPC、Renderer receipt、存储根、独立 TTS/LLM 出站白名单、Host/Bridge 重启 | 真实自定义局全流程和跨显示环境兼容。 |
| 性能 Harness | 本机 P50/P95/P99、背压、内存/句柄、deadline、时间预算 | 对所有硬件或外部 Provider 的绝对 SLA。 |
| 自定义局 smoke | 已确认的只读 GEP 能力、显示、时序和用户体验 | 任何游戏输入自动化、未授权采集或取代回放回归。 |

任何 P0 条件都至少需要两个层级的证据，其中一个必须是确定性 Replay 或受控集成；“手工看起来没有问题”不能作为 P0 通过证据。

## 2. TftReplayCapsule 与回放内核

Replay Capsule 只包含合成、匿名、语义化输入：版本/能力夹具、规范化 ingress、单调时间、用户控制、受控外部回执、预期结果和无副作用断言。它不能包含账号、真实 match ID、原始 GEP、截图、音频、完整攻略原文、LLM prompt/reasoning、TTS 原文或绝对文件路径。

专属回放内核至少提供：

```text
VirtualMonotonicClock
DeterministicTimerScheduler
fixed RandomSource / deterministic IDs
FakeGepIngress and FakeBridgeTransport
FakeRendererReceiptPort and FakeViewportAnchorDetector
FakeGameCoachTtsProvider and FakeCoachPlaybackAdapter
FakeKnowledgePackStore / FakeTftDataEvidenceLedger
FakeGameCoachLlmGateway
real LiveStateReducer + StrategicDecisionCoordinator
real AdviceCoordinator + GameVoiceScheduler + viewport confidence gate
```

真实网络、`Task.Delay`、线程池顺序、随机 UUID、真实 TTS/LLM、真实窗口时序和墙钟不能决定领域回放结果。每个延迟外部 completion 必须带并复检 runtime、transport、session/round、snapshot/revision、Advice/Delivery/attempt、view/lease 和 timer 代际。

### 2.1 强类型结果与稳定散列

Replay 的断言使用 `ExpectedStateTransition`、`ExpectedGapOrResync`、`ExpectedAdviceCurrentRevision`、`ExpectedDeliveryTerminal`、`ExpectedRenderReceipt`、`ExpectedVoiceOutcome`、`ExpectedDegradeState` 和 `ExpectedNoSideEffect`，而非“日志中包含某段文字”。

每次运行生成有序 `CanonicalOutcomeProjection` 和 `OutcomeProjectionHash`。散列涵盖 session/round/capability/state、StrategicDecision 的结构身份、Advice revision/replaceKey/digest/priority/scope/关闭原因、真实 Delivery 回执、Panel/Overlay/Voice 终态、知识快照与受控 reasonCode；它排除墙钟、设备 GUID、音频字节、日志行、绝对路径、异常原文和 LLM 自然语言措辞。发生差异时输出第一个脱敏分歧点和双方状态摘要，而非原始载荷。

## 3. 全系统安全降级 Oracle

棋盘内精确格、箭头和像素标签只能在下列条件同时成立时显示：

```text
Current Advice revision
+ current session / round / render lease
+ current viewGeneration
+ OverlayHighConfidence
+ no gap and fresh realtime facts
+ compatible knowledgeSnapshot
+ non-expired TTL
```

| 失效情况 | 可以保留 | 必须禁止 |
|---|---|---|
| GEP gap、重同步、实时事实过期 | 标明不确定的 Panel 历史/通用解释 | 精确站位、精确牌库、残缺事实上的高确定性建议。 |
| viewGeneration 改变或 Medium/Low | Edge Dock 和 Panel | 任何棋盘内精确格/箭头/像素标签。 |
| Advice 被 supersede、scope/TTL 结束 | 新 revision 和最小审计事实 | 旧 Panel current、旧 Overlay、旧 Voice 启动或补播。 |
| TTS/LLM 不可用 | 本地 FastPath、Panel、合格 Overlay | 等待远程服务后补播、外部服务重写决策。 |
| D 盘不可写 | 内存实时核心 | C 盘回退、伪造已保存/已发布。 |
| 知识过期/patch 不兼容 | 显示历史/低置信参考 | 当作当前 P1/P3 关键策略事实。 |

每个异步转换点都要做僵尸回调穷举：让旧 `attemptId`、`viewGeneration`、`renderLeaseId`、`sessionEpoch`、`runtimeEpoch` 在新状态建立后完成。唯一允许结果是忽略或带 `late/obsolete` reasonCode 的终态化。

## 4. P0/P1 场景库

P0 意味着不可进入自用发布或后续高风险版本；P1 是核心可靠性/体验门，P2 用于长期兼容性回归。下列场景是最小首批库，均应使用合成匿名语义输入。

| ID | 场景 | 必须看到 | 绝不允许 | 级别 |
|---|---|---|---|---|
| G-01 | 冷启动且存在上次异常记录 | 新 runtimeEpoch、HideAll 基线、新快照后才建立 Advice | 自动恢复旧语音、旧箭头、旧 current Panel | P0 |
| G-02 | 完整备战期、知识兼容、Viewport High | Bridge→Reducer→Strategy→Advice→Panel/Overlay/可选 Voice 及真实回执 | 仅命令发送就判视觉成功；策略直接写像素 | P0 |
| G-03 | 同 replaceKey 快速更新 | 新 revision 替换、旧 Pending Voice 取消、旧 lease 失效 | 旧 ACK/TTS/render 复活 | P0 |
| G-04 | 缩放/DPI/窗口/ROI 变化 | 先 HideExact，再重取 High；Low 留 Dock/Panel | 错位箭头、Low 下精确画格 | P0 |
| G-05 | sequence gap、Bridge 重启或快照失配 | Gap→Resync→完整 snapshot→新 state revision | 猜测补齐缺失棋盘/牌库 | P0 |
| G-06 | 回合/对局/新局切换后的迟到 callback | 旧 session/round/attempt/view 全部失效 | 上局 Voice/Overlay 在新局出现 | P0 |
| G-07 | 高费接近三星等 FastPath | 零 LLM、零 DataAgent、零网络的短提示；语音失败视觉仍在 | FastPath 调 Grok/RAG/外网或错过窗口 | P0 |
| G-08 | Mute、Skip、Cancel、一次朗读 | 控制经 Scheduler、Mute 不影响 Panel/Overlay | UI 直控播放器、静音后补播 | P0 |
| G-09 | TTS 超时、取消后返回、设备丢失 | deadline 后丢弃、attempt 隔离、Voice 降级 | 过期播放、无限重试、QQ 回退 | P1 |
| G-10 | Renderer/render 通道断线/重建 | 精确层撤回，重连先 HideAll 后仅交付 Current | 保留可能错位箭头或恢复旧 revision | P0 |
| G-11 | 知识导入、校验/索引失败或崩溃恢复 | staging→validation→publish 原子，失败保留旧快照 | 半成品/旧 patch 激活、DataAgent 推荐 | P1 |
| G-12 | 普通查找与 PinnedEvidence | 普通查询不产生无用账；证据可关联 source/manifest/hash | 将当前局、问题、LLM 内容或策略结论写进 ledger | P1 |
| G-13 | 用户 RichPath 追问、Grok 超时/越权/预算耗尽 | 本地结论仍在，草案经 Validator 才交付 | LLM 修改数值、行动、优先级、站位 | P1 |
| G-14 | D 盘拒绝/满盘/SQLite 故障 | 明确持久化降级，实时核心可安全运行 | C 盘/默认 Alife 路径写入或伪造成功 | P0 |
| G-15 | 对局中网络审计 | 攻略零上传；TTS 只收白名单 VoiceSegment；RichPath 只走专属通道 | GameSnapshot、账号、RAG query、用户问题等泄漏 | P0 |
| G-16 | 大 snapshot/event burst 与 Hide 并发 | ingress 有界、低价值合并、Hide 优先 | 大包阻塞撤回或 GEP callback | P1 |
| G-17 | 长时运行/休眠恢复/多次缩放 | 过期内容不补播、资源无泄漏、新 snapshot 重建 | 旧 timer/Provider 污染新状态 | P1 |
| G-18 | 校准取消/超时 | Overlay 始终鼠标穿透，Panel 完成预览 | 点击落入游戏或模拟输入 | P0 |

## 5. 故障注入与可观测性

故障矩阵必须至少覆盖：重复/乱序/缺序/超大/不兼容 GEP；单向 IPC 断线/ACK 丢失/重连；LiveState snapshot 不一致；并发 supersede；Viewport DPI/缩放/遮挡/迟到 High；Renderer 崩溃/Accepted 未显示；TTS 卡死/取消不响应/设备丢失；Voice 控制并发；Pack hash/许可/patch/索引故障；ledger WAL/I/O/hash-chain 故障；LLM 离线/注入/越权/迟到；D 盘权限/满盘/路径错误；休眠/GC/线程池停顿；以及对局网络出站审计。

稳定 `faultCategory` 至少包括 `TransportDisconnected`、`SequenceGap`、`RingBufferOverflow`、`SchemaRejected`、`SnapshotUnavailable`、`ViewportConfidenceLost`、`RendererAckTimedOut`、`OverlayResourceReleaseTimedOut`、`TtsPrepareTimedOut`、`TtsCancelAckTimedOut`、`PlaybackStartFailed`、`PlaybackDeviceLost`、`PlaybackReleaseTimedOut`、`StorageUnavailable`、`StorageIntegrityDegraded`、`KnowledgeSnapshotIncompatible`、`LlmTimeout`、`LlmBudgetDenied`。

诊断只记录白名单、脱敏、容量受限 Projection，例如版本、健康、延迟桶、reasonCode、source/pack 的受控引用、回放 Hash 和第一分歧点。日志、Replay、性能制品和导出全部遵守 D 盘路径、默认关闭/短期保留和显式删除政策；诊断绝不能成为保存原始对局的旁路。

## 6. 性能目标

性能目标是受支持目标机的初始验收口径，不是对所有硬件、Windows 合成器、Overwolf 上游或远程 Provider 的外部 SLA。

| 路径 | 初始目标 |
|---|---|
| Bridge callback 入有界内存队列 | P95 ≤ 2ms，且不阻塞 GEP callback。 |
| GEP 到本地 Reducer | P95 ≤ 10ms，不含 Overwolf 上游延迟。 |
| ingest 到 Host 接收/ACK | P95 ≤ 15ms。 |
| AdviceCoordinator 状态转移 | P50 ≤ 2ms，P95 ≤ 5ms，P99 ≤ 15ms。 |
| 本地规则分析与候选仲裁 | P95 50–150ms。 |
| FastPath 表达 | P95 ≤ 10ms；零 LLM/DataAgent/网络。 |
| StrategicDecision 到 Panel 首结果 | P95 ≤ 100ms。 |
| Host RenderCommand 到 Bridge Accepted | P95 ≤ 15ms。 |
| 已验证投影下 RenderCommand 到 OverlayShown | P95 ≤ 50ms。 |
| 有效本地状态到首个 High 精确箭头 | P95 ≤ 100–150ms；端到端复合预算 ≤ 250ms。 |
| High 单格投影 | P95 ≤ 2ms。 |
| 窗口/DPI/缩放变化后的旧精确层撤回 | 不超过一个渲染帧。 |
| ROI 发现内部缩放/漂移 | 约 67–100ms；恢复 High 约 200–300ms P95，失败则保持降级。 |
| Supersede 到取消未播放 Voice | P95 ≤ 20ms。 |
| 单次事件循环不可中断占用 | ≤ 20ms。 |

远程 TTS/LLM 的网络时延不计入上述本地预算；其质量门是过期、取消、失败和资源释放后绝不播放/复活，而不是强行追求远程响应时间。

## 7. 发布证据与版本 Gate

任何进入自用或完整发布的版本都应能证明：

1. 所有旧 runtime/session/revision/view/attempt/lease callback 无法复活内容。
2. gap、知识失配或低 Viewport 置信度不会显示伪精确建议。
3. 缩放/窗口变化先隐藏旧箭头，再允许重新显示。
4. 重启、休眠、重连不补播旧 Voice，也不恢复旧 Overlay。
5. FastPath 在断网、LLM Disabled、DataAgent/SQLite 故障时仍可运行或明确沉默，且无远程调用。
6. D 盘故障不回退 C 盘，也不伪造已保存/发布。
7. 攻略来源对局中零上传；TTS 仅收白名单 VoiceSegment；ledger 没有对局/账号/截图/LLM/策略内容。
8. event burst 不阻塞 P1 Hide/Invalidate；Overlay 永远鼠标穿透；不存在输入模拟。

`v0.3.0` 只需要有限合成断言和自定义局 smoke；完整 Replay/故障/性能平台属于 `v1.7.0`，其证据是进入 `v2.0.0` 集成发布的必要前置。完整基础设施的粗略复杂度为 13–22 净人日，不包括具体策略、Overwolf 兼容性 PoC、真实 TTS/LLM 接入或知识授权。
