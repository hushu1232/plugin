# TFT Companion 运行时与决策工程规格

> **状态：** 已确认设计的工程规格；只定义运行时和策略边界，不授权实现。

**目标：** 以经过能力门控、版本冻结和新鲜度验证的本地事实，产生少量可解释、可撤销的 `StrategicDecision`，并在信息不足时安全沉默或降级。

**架构：** `IngressAndStateDomain` 是实时事实的唯一入口和归约域；`DecisionDomain` 只消费不可变 `DecisionSnapshot` 与冻结 `KnowledgeFactView`。各分析器只生成候选，`StrategicDecisionCoordinator` 是唯一的战略仲裁者，后续表达与交付不可以反写它。

**依赖：** [工程文档索引](2026-07-14-tft-companion-engineering-document-index.md)、[宿主/存储/IPC 规格](2026-07-14-tft-companion-host-storage-ipc-engineering-spec.md)、[知识/LLM/证据规格](2026-07-14-tft-companion-knowledge-llm-evidence-engineering-spec.md)。

---

## 1. 责任边界

本域回答三个问题：当前已可靠观察到什么、在此刻允许基于这些事实推断什么、哪一个行动最值得用户注意。

本域负责：

- 受支持 GEP 语义事实的规范化、去重、顺序、新鲜度、gap 与重同步状态。
- 匿名会话、回合、快照、版本与能力矩阵的建立和失效。
- 局内冻结的 RulesPack、CompPack、CalibrationPack、LocalizationCatalog、表达 Skill 与 Provider 版本引用。
- 战略状态、阵容路径、连败/连胜目标、经济、牌库、复制器、装备、强化、BoardStrength、承伤风险与逻辑站位候选。
- 由同一 `DecisionSnapshot` 推导一个主行动、至多两个面板支持行动和至多一个关键观察。

本域明确不负责：

- 像素坐标、Overlay 可见性、窗口鼠标穿透、音频制品或播放生命周期。
- SQLite 查询、知识下载、索引、证据落账、远程 TTS、远程 LLM、网页/API 查询。
- 玩家身份、账号、聊天、截图、视频、游戏输入、游戏内操作或队列类型判断。

## 2. 输入、身份与不可变快照

### 2.1 事实的最小类型

进入 `LiveStateReducer` 的每条事实必须有来源、单调接收时间、schema/能力信息、新鲜度和明确的 `Present`、`Absent` 或 `Unknown` 状态。`Unknown` 不是空值的同义词：它表示系统不能安全判断字段是否真实不存在、尚未到达或已经失效。

| 类型 | 写者 | 消费者 | 规则 |
|---|---|---|---|
| `NormalizedIngressEvent` | IPCGateway / IngressAndStateDomain | LiveStateReducer | 已验证的语义事实；不能携带任意原始帧给业务域。 |
| `CapabilityMatrix` | IngressAndStateDomain | SnapshotBuilder、分析器、Panel | 每个字段需同时通过 schema、实际到达、格式、新鲜度、Ruleset 和回放覆盖门。 |
| `RoundSettlementSnapshot` | SnapshotBuilder | 复盘、对手板快照、可见牌库账本 | 回合结束稳定窗口建议约 800ms、最大 2s；具体时序由实测回放校准。 |
| `DecisionSnapshot` | DecisionSnapshotBuilder | 所有分析器和决策协调器 | 冻结 `stateRevision`、能力、会话/回合、`knowledgeSnapshotId` 与版本引用；发布后不可原地修改。 |
| `KnowledgeFactView` | KnowledgeAndProfileDomain | DecisionSnapshotBuilder | 仅包含本回合已经冻结、已验证的内存事实；缺失时返回 `Unknown`，不触发查库。 |
| `ActionCandidate` / `RiskSignal` / `ObservationCandidate` | 各专用分析器 | StrategicDecisionCoordinator | 不带 TTS、Overlay、Panel、数据库或网络动作。 |

### 2.2 分层身份

下面的身份不能互相替代；任何跨域消息只携带与自身语义相关的最小集合。

| 身份 | 唯一生成者 | 失效语义 |
|---|---|---|
| `runtimeEpoch` | RuntimeSupervisor | 每次 Host 启动必换；所有旧异步回调失效。 |
| `sessionId` / `sessionEpoch` | SessionRoundController | 匿名本机会话与代际；Host 重启或不可靠恢复必换 `sessionEpoch`。 |
| `roundKey` | SessionRoundController | 当前回合范围；不能用玩家身份构造。 |
| `ingressStateSnapshotId` / `stateRevision` | LiveStateReducer | 已验证游戏事实快照与递增事实版本。 |
| `decisionSnapshotId` | DecisionSnapshotBuilder | 一次仲裁的冻结输入；不能与输入快照混用。 |
| `knowledgeSnapshotId` | TftDataAgent | 被验证的规则/知识事实版本；活跃决策窗口内不变。 |
| `coordinatorSequence` | AdviceCoordinator | 只用于 Advice/Delivery 的最终仲裁，不替代事实 revision。 |

来源 Bridge 的 `bridgeSnapshotId` 和 `streamSequence` 只用于传输关联与顺序辅助，未被 Host 验证前不能进入策略、Advice 或 Replay 的领域血缘。

### 2.3 会话与恢复

`SessionRoundController` 使用如下状态：

```text
Idle → Starting(Fresh | RecoveredPartial) → Active ↔ Suspended
                                                ↓
                     Interrupted → Ending → Finalized → Idle
```

- `Starting(Fresh)` 需要新的、完整且受支持的快照。
- `Starting(RecoveredPartial)` 只在 Host 重启后当前快照足够支持保守启动时使用；它绝不继承旧 Advice、旧语音、旧 Overlay 或旧实时租约。
- `Suspended` 由 gap、快照失配、关键事实过期或会话身份不可验证触发；实时建议立刻过期，未播语音取消，精确 Overlay 隐藏，直到新的完整快照。
- `Interrupted` 是强制收尾原因而不是可长期悬挂状态；不能只因时间接近将旧局拼接到新局。

## 3. 版本、规则和能力门控

### 3.1 RulesetKey 与局内冻结

`RulesetKey` 至少由 `game`、`platform`、`modeFamily`、`region`、`setId`、`runtimePatchLine`、`staticDataVersion`、`contentFingerprint`、`gepSchemaVersion`、`rulesPackVersion` 与 `compPackVersion` 构成；它不包含队列类型。

一局开始后冻结下列引用：

- RulesPack、CompPack、CalibrationPack 与 LocalizationCatalog；
- `knowledgeSnapshotId` 和关联 Source/Manifest 证据；
- 表达 Skill 版本；
- 已启用 TTS/LLM Profile 的能力快照，而不是凭据本身。

补丁只能在局外原子更新。若只能确认 Set 不能确认准确 patch，进入 `SetOnly`：保留已观察事实，关闭精确牌库概率、智能追三排序、补丁相关阵容/装备/经济结论，并把精度降级显式交给 Panel。

### 3.2 CapabilityMatrix

字段可用于精确建议的前提是：公开 schema 支持、实际事件已经到达、格式正确、当前仍新鲜、Ruleset 兼容、回放已覆盖且缺失时有明确降级。`board_players`、`opponent_board_pieces`、强化三选一候选等字段必须经真实自定义局验证，文档字段名本身不构成实现承诺。

CapabilityMatrix 的核心输出不是“尽量补全数据”，而是以下判断：

```text
可用于当前精确结论
 / 只能用于方向性提示
 / 仅保留为观察记录
 / 完全不能使用
```

## 4. 内容事实与战略状态

### 4.1 内容包职责

| 包 | 仅承担的事实 |
|---|---|
| RulesPack | 弈子、羁绊、装备、强化、卡池、商店概率、经验、玩家伤害、复制器、回池、棋盘逻辑坐标和特殊升级语义。 |
| CompPack | `CompFamily`、`BoardNode`、`TransitionEdge`、`FallbackEdge`，以及止血板、标准板、上限板、回退、装备转移、Keep/Sell/DoNotChase、追三条件和关键角色位。 |
| CalibrationPack | BoardStrength 权重、承伤/连败/升星/追三/干预阈值与语音疲劳默认值。 |
| LocalizationCatalog | 经版本化验证的显示名称、短语和 UI 术语；不承担策略计算。 |

第一批内容应维护 10–20 条完整可过渡的阵容家族，而非大量只有最终棋盘截图的浅层阵容。数值判断仍由确定性代码和上述包完成，不能交给 LLM 即兴推算。

### 4.2 三维战略状态

战略状态分为互不混淆的三层：

| 层 | 可取值或例子 | 目的 |
|---|---|---|
| `StrategicObjective` | Balanced、PreserveWinStreak、PreserveLossStreak、PreparingToLaunch、Stabilize、AllInSurvival、RecoverEconomy、CapBoard | 当前局面的规则驱动目标。 |
| `PlanState` | Flexible → DirectionSelected → Committed → PreparingTransition → RollingDown → MinimumBoardReached → StandardBoardReached → Capped | 阵容路径的进度与可逆性。 |
| `UserIntent` | 保守/平衡/激进、连败锁定、阵容路径锁定 | 用户选择的可见意图。 |

高危生存可以临时覆盖用户的连败或路径锁，但必须给出原因，不能后台静默解除用户意图。仲裁优先级固定为：数据有效与动作合法、近期淘汰风险、用户显式意图、不可逆承诺、战略目标、路径一致性、净价值、个性化、打扰成本。

### 4.3 连败作为正常战略

连败模型必须区分计划内连败、可控延迟启动、即将到达启动点、错过安全启动窗口、以及因血量/牌/装备/强化变化必须提前止血。每个连败相关候选都要携带经济曲线、预计启动回合、启动所需金币、血量缓冲、核心装备/弈子、转型成本、最低止血板可达性与失败回退。

因此“当前战力低”不是失败结论；只有生存风险、启动资金和回退路径共同显示计划不可持续时，才可产生止血或转型建议。

## 5. 候选分析器合同

所有分析器使用同一份 `DecisionSnapshot`，并只产生候选，不直接表达或交付。

| 分析器 | 必须输出 | 限制与降级 |
|---|---|---|
| Comp / Transition | 方向、最低止血板、标准/上限板、转型边、失败回退 | patch 或包不兼容时只保留通用事实和 `Unknown`。 |
| BoardStrength / DamageRisk | 可解释 `BoardStrengthVector`、阶段相对强度档、结构缺口、Low–LethalRisk 宽风险带 | 不承诺精确胜率、精确伤害、完整战斗模拟、在线训练或全棋盘搜索。 |
| Economy | 预算、金币底线、目标集合、停止条件、失败回退、有效期 | 只规划下一关键行动，不做多回合随机优化。 |
| VisiblePoolLedger | 已确认持有、可购买、未来可抽上界、未知保留、命中难度区间 | 不能声称精确剩余牌数；未知对手信息必须保守。 |
| Duplicator | `confirmedCopies`、`applicableDuplicators`、`reachableCopies` | 同一复制器不能分配给多个目标；纳入费用限制、机会成本和战略目标。 |
| OpponentUpgradeThreat | 观察到的对手升星风险与重要性 | 1–3 费仅在 8 张或三星重点；4 费以 6/7/8/9 对应 Watch/High/Imminent/Completed；5 费以 5/6–7/8/9 对应相同等级。语音还受相关性和注意力预算限制。 |
| ChaseThree | ActiveChase、ConditionalChase、NaturalOnly 或 Reject | 零持有不进入主动追三；低费先硬过滤；8 张低价值可仅提示自然来牌，不鼓励深搜。 |
| Item / Augment | 角色需求、效果标签、有限全局分配、即时战力、机会成本、转移性 | 特殊装备逐项白名单；未知高影响强化功能降级；不做 OCR 或自动点击。 |
| Positioning | 逻辑棋盘目标、角色槽、有限结构变体、`PositioningScoreVector` | 不创建像素坐标；只产生 Base、Mirror、CarrySplit、反后排/范围伤害等有限变体，最多 1–2 个关键移动。 |

追三价值函数至少权衡三星边际战力、阵容一致性、可继续使用回合、已持有/可达副本、竞争、商店等级与费用、搜牌预算、复制器机会成本、备战席、对生存风险改善、失败回退与提示噪声。入口固定为智能推荐、当前阵容、四费、五费与指定弈子；这保证大量低费不会淹没推荐。

## 6. 战略仲裁与触发窗口

`StrategicDecisionCoordinator` 按固定顺序处理候选：

```text
版本/能力/时效门控
→ 动作合法性
→ 硬政策
→ 共享资源冲突
→ 支配剪枝
→ 战略状态化效用
→ 决策滞回
→ StrategicDecision
```

共享资源包括金币/经验、棋盘格/备战席、弈子、组件/成装、复制器、消耗品、时间和用户注意力。输出最多一个主行动、两个支持行动、一个观察，且每个行动都有停止条件、失败回退和明确有效期；“没有主行动”是合法且常见的高质量结果。

主动阵容或战略更新只可在信息增益明显时发生：首次形成方向、核心装备/强化/高费牌改变路径、原路径触发失败条件、连败进入启动窗口、血量进入必须止血状态、或用户主动询问。普通来牌、小幅评分波动和局内细微换位默认只更新 Panel。

## 7. 逻辑站位与敌方信息的正确使用

逻辑棋盘统一为 4×7：`row 0` 靠敌方、`row 3` 靠己方底部、`column 0` 为己方左、`column 6` 为己方右。必须存在版本化映射：GEP 坐标 → 逻辑坐标、知识模板坐标 → 逻辑坐标；逻辑坐标 → 屏幕像素只属于 Viewport/Overlay 域。

对手棋盘只能作为受支持、已验证且新鲜的快照事实，用于后台微调风险和有限站位变体。它不能被假定为“下回合候选对手列表”，也不能要求频繁切换观战。没有可信 `opponent_board_pieces` 或大厅威胁摘要时，系统退回 BaselineFormation、己方角色映射和通用反制原则，而不是编造敌方具体位置。

## 8. 失败语义与验收

| 条件 | 决策域必须做什么 |
|---|---|
| gap、重连、会话失配、关键事实过期 | 终止当前依赖实时事实的决策范围；等待新快照；不补齐猜测。 |
| SetOnly、patch 不匹配、知识包失效 | 关闭精确牌库、追三排序、版本特化装备/经济/阵容判断；显示精度降级。 |
| 低 Viewport 置信度 | 保留逻辑站位与 Panel 解释；不请求精确格位交付。 |
| DataAgent/SQLite/网络不可用 | 只使用已冻结内存 RulesPack/CompPack；不阻塞也不补查。 |
| 用户静音、跳过或关闭提示 | 决策可以继续生成，但不越过 Delivery 域直接发声或显示。 |

验收至少要求：同一输入快照和内容版本产生稳定的候选排序/仲裁；无事实或不兼容事实不会产生伪精确行动；新 snapshot 只能生成新决策而不能原地修改旧决策；任何分析器不能引用 TTS、Renderer、数据库写端或 LLM 客户端。

## 9. 版本映射与成本边界

| 能力 | 首次窄范围版本 | 完整化版本 | 计划复杂度边界 |
|---|---|---|---|
| 最小会话/快照/能力门 | v0.0.1–v0.1.0 | v1.0.0 | 前提是 G1/G2 已实测。 |
| 有限本地 Comp 与策略 | v0.1.0 | v0.5.0 | 先做 10–20 条完整路径。 |
| 连败、转型与经济 | v0.1.0 的有限建议 | v0.5.0–v0.6.0 | 不做随机多回合优化。 |
| 牌库、复制器、追三、对手升星 | Panel 观察占位 | v0.6.0 | 不宣称全局精确剩余牌。 |
| BoardStrength、承伤、装备、强化 | 受限风险观察 | v0.7.0 | BoardStrength 粗略复杂度约 21–39 人日。 |
| 逻辑站位与对手快照微调 | v0.2.0 仅显示既有建议 | v0.8.0 | 完整站位子系统粗略复杂度约 35–68 人日，不含像素渲染壳。 |

这些数字是范围预估，不是对某个日期或算法准确性的承诺；完整顺序、增量和停止条件见[完整版本路线图](../plans/2026-07-14-tft-companion-complete-system-version-roadmap.md)。
