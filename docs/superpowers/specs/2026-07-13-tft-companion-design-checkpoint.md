# TFT 只读陪伴教练插件设计检查点

- 文档状态：已确认设计的本地检查点，不是最终规格
- 记录日期：2026-07-13
- 目标产品：Alife 的 PC《云顶之弈》只读陪伴型教练插件
- 首选路线：PC《云顶之弈》 + Overwolf TFT GEP + Alife
- 用途：防止会话关闭造成设计结论丢失，并作为后续逐项讨论的稳定基线
- 当前阶段不进行：版本规划、排期承诺、功能实现

## 1. 产品定位与体验原则

插件的核心身份是“陪伴者 + 教练”，不是代打、自动化或高频指挥工具。

默认模式为“轻陪伴教练”：

- 系统可持续分析，但通常保持沉默。
- 普通变化只更新侧边面板。
- 显著变化最多给出一句短语音。
- 关键生存风险和重大异常允许主动提醒。
- 用户主动询问时才展开完整分析。
- 沉默是正常且高质量的输出。
- 每个备战阶段最多一条主动战略语音，但这不是必须用完的配额。
- 普通陪伴消息不跨阶段排队。
- 默认避免命令式、责备式、主播式和持续轰炸式话术。

目标：

- 在不操作游戏的前提下，帮助用户理解局面。
- 结合版本、阵容、经济、牌库、装备、强化和站位给出可解释建议。
- 优先处理回合结束后的稳定快照，不追求每次细微换位都通知。
- 对高费弈子升星威胁、牌库竞争和追三星价值提供低噪声提醒。
- 允许未来接入经正式授权的第三方阵容、站位或教练数据。
- 所有建议都服从“陪伴优先、少打扰、可忽略、可解释”。

## 2. 平台范围与只读边界

### 2.1 首版平台

首版只面向：

- PC《云顶之弈》客户端。
- Overwolf TFT Game Events Provider。
- Alife 本地分析、侧边面板、透明提示层和语音。

安卓模拟器中的《金铲铲之战》不作为首版数据采集平台。公开、稳定、低延迟且合规的实时棋盘接口不足；纯 OCR 也难以稳定覆盖频繁观战、棋盘坐标、牌库和对手状态。

### 2.2 严格只读

插件不得：

- 向游戏发送鼠标或键盘输入。
- 自动购买、刷新、升级、换位、合装、使用英雄复制器或切换观战。
- 读取游戏进程内存。
- 注入游戏进程。
- 抓包、解密或绕过客户端保护。
- 使用 ADB 控制安卓模拟器。
- 将建议转换成可执行输入。
- 在 TFT 模块加载桌面操作网关。

透明 Overlay 必须完全鼠标穿透。所有交互按钮只能位于 Alife 独立侧边面板。

### 2.3 不检测队列类型

插件不判断排位、匹配或自定义，不实现：

- QueueTypeDetector
- CustomGameSessionGate
- RankedModeBlocker

会话只依据 match_start 和 match_end 建立与结束。系统仍需确认是否为受支持的标准八人 TFT 规则集，但这不等于检测队列。

使用者仍需遵守 Riot、Overwolf 和第三方平台的最新条款。技术上的只读不自动等于所有场景都获得平台许可。

## 3. 总体架构

    Overwolf TFT GEP
          |
          v
    Overwolf Bridge
          |
          v
    规范化事件与 CapabilityMatrix
          |
          v
    LiveStateReducer（单写者）
          |
          +--------------------+
          |                    |
          v                    v
    RoundSettlementSnapshot   DecisionSnapshot
          |                    |
          +---------+----------+
                    v
          各分析模块生成候选
          |
          +-- 阵容与转型
          +-- 经济与搜牌
          +-- 牌库、复制器与追三
          +-- 装备与强化
          +-- BoardStrength
          +-- ExpectedDamage 风险带
          +-- 站位
          +-- 对手升星威胁
                    |
                    v
       StrategicDecisionCoordinator
                    |
                    v
          StrategicDecision
                    |
                    v
          InterventionPolicy
                    |
                    v
          AdviceSemanticCompiler
                    |
                    v
          AdviceExpressionRequest
                    |
                    v
       render-tft-companion-advice Skill
                    |
                    v
          AdviceExpressionResult
                    |
                    v
       ExpressionContractValidator
                    |
                    v
          AdviceCoordinator
          |         |         |
          v         v         v
    GameVoiceScheduler  Overlay Render Intent  PanelProjection Store

各分析模块只能产生：

- ActionCandidate
- RiskSignal
- ObservationCandidate

分析模块不能直接调用 TTS、Overlay 或面板。最终是否提示、提示什么以及占用哪些资源，只能由中央协调器和干预策略决定。

## 4. 数据来源与能力门控

### 4.1 Overwolf 已核验基础

截至 2026-07-13：

- TFT Game ID：21570。
- Overwolf 状态：published。
- 状态地址：https://game-events-status.overwolf.com/21570_prod.json
- 官方 TFT GEP 文档：https://dev.overwolf.com/ow-native/live-game-data-gep/supported-games/teamfight-tactics/

关键事件或字段包括：

- match_start / match_end
- round_start / round_end
- battle_start / battle_end / battle_state
- round_type
- opponent
- board_spectate
- board_pieces
- opponent_board_pieces
- board_players
- bench_pieces
- item_bench
- shop_pieces
- gold / xp / health
- player_status
- augments
- picked_augment

match_info.opponent 当前示例是单一实际对象，不是候选对手列表。公开接口中未确认 possible_opponents 一类候选字段。

用户已经确认：

- 不需要播报“下一轮可能遇见谁”。
- 不需要展示候选对手身份。
- 只需要最终站位建议。

因此基础设计不建设候选对手提示 UI，也不增加 UiCandidateMarkerProvider。

### 4.2 CapabilityMatrix

字段只有同时满足以下条件，才能参与精确建议：

- 当前 GEP Schema 声明支持。
- 实际事件流已经收到。
- 格式验证通过。
- 新鲜度满足决策窗口。
- 与当前 RulesetKey 兼容。
- 回放测试已经覆盖。
- 缺失时存在明确降级行为。

board_players、opponent_board_pieces、三选一强化候选等能力必须通过真实自定义局实测，不能只凭文档字段名承诺稳定可用。

### 4.3 Riot 与第三方来源

Riot TFT 文档：

https://developer.riotgames.com/docs/tft

公开 Riot TFT API 主要提供赛后与静态数据，不应假设它能提供当前对局的实时敌方站位。

第三方数据必须通过正式授权的 Provider 接入。不得抓取网站私有端点、复制未经授权的数据流或依赖随时可能改变的内部接口。

## 5. 版本与规则兼容

采用严格分级：

- Verified
- PatchMatched
- SetOnly
- ManualOverride
- Mismatch
- Unknown

RulesetKey 包含：

- game
- platform
- modeFamily
- region
- setId
- runtimePatchLine
- staticDataVersion
- contentFingerprint
- gepSchemaVersion
- rulesPackVersion
- compPackVersion

RulesetKey 不包含队列类型。

一局开始后冻结：

- RulesPack
- CompPack
- CalibrationPack
- LocalizationCatalog
- 表达 Skill 版本
- Provider 版本

局中不得热更新，以免同一局前后使用不同规则。

若只确认 Set、无法确认准确补丁，则进入 SetOnly：

- 保留可见事实。
- 关闭精确牌库概率。
- 关闭智能追三排序。
- 关闭版本相关阵容建议。
- 关闭补丁相关的精确装备与经济判断。
- 仍可显示至少可见副本和已发生的三星。
- 输出必须明确说明精度降级。

## 6. 会话、事件、快照与重连

### 6.1 会话状态

    Idle
      |
      v
    Starting
      |
      v
    Active <----> Suspended
      |
      v
    Ending
      |
      v
    Finalized
      |
      v
    Idle

异常状态：

- Interrupted。

恢复处置标签：

- RecoveredPartial 只能作为 Starting 的标签，即 Starting(RecoveredPartial)，不再是与 Active、Suspended 并列的 SessionPhase。

### 6.1.1 标识、快照与 revision 的唯一关系

所有实时身份使用以下分层，避免把会话、快照与 revision 视为同义字段：

| 标识或版本 | 生成者与生命周期 | 用途与关系 |
|---|---|---|
| runtimeEpoch | RuntimeSupervisor；每次 Host 启动必换 | 进程内外所有实时回调的第一失效边界；不可恢复为旧值 |
| serverInstanceId | Host；runtimeEpoch 的不透明 wire 表示 | 仅用于 Hello / Welcome 与跨进程握手；不另建第二套生命周期 |
| sessionId | SessionRoundController；本机生成的匿名逻辑会话标识 | 不等于 Riot ID、账号或 match ID；仅在可靠恢复时可保留为同一逻辑会话 |
| sessionEpoch | SessionRoundController；每次会话创建、Host 重启后的恢复或会话替换必换 | 实时状态、render、Voice 与 receipt 的会话代际；旧 epoch 回调一律无效 |
| roundKey | SessionRoundController；由已验证当前回合事实产生 | 标识当前回合范围；不使用玩家身份构造 |
| bridgeSnapshotId | Bridge；每次源端 `StateSnapshot` 捕获生成 | 仅作跨进程传输关联；Host 验证前不是规范游戏事实 ID，不能进入 Decision、Advice 或 Replay 的领域血缘 |
| ingressStateSnapshotId | Bridge snapshot 经 Reducer 验证后产生 | 标识一份输入游戏事实快照；不能与 DecisionSnapshot 或知识快照混用 |
| stateRevision | LiveStateReducer | 仅表示游戏事实状态的递增版本 |
| hostIngressSequence | IPCGateway；每个被 Host 接受的外部 Envelope 分配 | 仅排序跨进程外部 ingress；与 ingestMonotonicTime 组成外部事实的确定性顺序，不替代 Advice 的仲裁序号 |
| coordinatorSequence | AdviceCoordinator；每个被其串行循环接受的 Fact、Command 或 Receipt 分配 | Advice、Delivery、Voice 冲突与回放状态转移的最终稳定决胜序号；不替代 stateRevision |
| decisionSnapshotId | DecisionSnapshotBuilder | 冻结一次仲裁使用的 stateRevision、Capability 与 knowledgeSnapshotId；不可原地修改 |
| knowledgeSnapshotId | TftDataAgent | 已验证规则与知识事实快照；活跃 DecisionSnapshot 固定引用它 |
| adviceId / adviceRevision | AdviceCoordinator | 一条不可变 Advice revision 的身份与其版本；不得与 stateRevision 混用 |
| logicalAdviceKey | AdviceCoordinator | 稳定业务语义类别，例如 positioning.primary；同一逻辑建议的多个 adviceRevision 共享它 |
| replaceKey | AdviceCoordinator | 仲裁冲突键；可等于 logicalAdviceKey，也可因资源 / 表达冲突而更宽，二者不得默认当作同义词 |
| supersedesAdviceId | AdviceCoordinator | 新 Advice 明确指向它替换的旧 adviceId；取代旧的 replacementOf 命名 |

Advice、Delivery、Voice、OverlayCommand 与 OverlayReceipt 在适用时必须携带 runtimeEpoch、sessionId、sessionEpoch、roundKey、adviceId、adviceRevision、deliveryId 与 attemptId；跨进程消息还必须带 connectionEpoch、renderLeaseId 或 viewGeneration 等对应边界。未知或不适用字段必须显式为 absent，不得用旧值补齐。

Bridge 入站 `StateSnapshot` 只能携带 `bridgeSnapshotId`（以及 Envelope 的 `messageId`、捕获语义和 schema）。只有 Host 接收、验证并经 LiveStateReducer 规范化成功后，才分配新的 `ingressStateSnapshotId`；后续 Decision、Advice、Delivery 与 Replay 一律引用后者，不得把未经验证的 `bridgeSnapshotId` 当作领域快照 ID。

顺序合同分为两层：外部 Bridge / Panel ingress 由 Host 按 `(ingestMonotonicTime, hostIngressSequence)` 排序；一旦规范 Fact、Command 或 Receipt 被 AdviceCoordinator 接受，Advice、Delivery、Voice 的最终仲裁按该事件的 Host 单调时间与 `coordinatorSequence` 决定。Bridge 的 `streamSequence`、源端时间、线程调度、字典遍历和墙钟都不能替代任一层的权威序号。

为兼容前文叙述，未限定的 revision 在 Advice、Delivery、Voice、Overlay 与表达链上下文中一律指 adviceRevision；在 LiveStateReducer、ingress 或游戏事实上下文中一律指 stateRevision。新的协议、SQLite schema、Replay fixture 与代码字段不得继续使用无前缀的 revision 或 snapshotId。

### 6.2 事件时间与状态

每条规范化事件区分：

- sourceTime
- ingestWallTime
- ingestMonotonicTime

采用：

- 有界规范化事件日志。
- 单写者 LiveStateReducer。
- 不可变快照。
- 可审计来源与新鲜度。
- 保守重连。

重要字段使用三态：

- Present
- Absent
- Unknown

每个事实还应携带：

- 来源。
- 观察时间。
- 来源回合。
- 覆盖范围。
- 新鲜度。
- 置信状态。

### 6.3 两类快照

RoundSettlementSnapshot：

- 面向回合结束后的稳定记录。
- 用于本地复盘、对手板快照、牌库账本更新和阶段总结。
- 初始稳定窗口建议约 800ms。
- 最大等待建议 2s。
- 具体窗口由真实事件时序回放校准。

DecisionSnapshot：

- 面向当前可执行决策窗口。
- 生成时冻结输入版本和能力状态。
- 只能产生有明确有效期的候选和建议。

已发布快照不得原地修改。迟到事实只能产生新 revision，并通过 Supersede 使旧建议失效。

### 6.4 断线恢复

断线时：

- 所有实时 Advice 立即过期。
- 清空未播放的陪伴语音。
- 隐藏 Overlay。
- 只有能可靠证明仍为同一局时才恢复原会话。
- 否则旧局必须进入 `Interrupted → Ending → Finalized → Idle` 的强制收尾。Interrupted 立即使旧 scope、未播 Voice、render lease、未终态 Delivery 与迟到回执围栏失效；它不是可以长期悬挂、等待新局覆盖的平行终态。
- 只有旧会话的实时失效围栏已经由 SessionRoundController 建立后，当前 snapshot 本身支持启动时，才可创建不继承旧 Advice、旧视觉或旧 Voice 的 Starting(RecoveredPartial)。有界 D 盘收尾失败只进入降级状态，不能阻塞该实时失效围栏或诱使旧内容复活。
- 同一 Host 内的 Bridge 重连只有在完整当前 snapshot 可靠证明仍为同一逻辑会话、且没有 match end 或新局证据时，才可从 Suspended 回到 Active 并保留 sessionId / sessionEpoch；它仍生成新的 ingressStateSnapshotId、stateRevision 与 Advice revision。
- Host 重启必换 runtimeEpoch 与 sessionEpoch；不得把旧实时会话直接恢复为 Active。
- 禁止仅凭“时间接近”合并两局。

## 7. 本地存储与 C 盘约束

采用：

- SQLite WAL。
- 后台单写者。
- 关键边界事务。
- 结构化列与版本化 JSON。
- 分层保留。
- 默认脱敏。

插件数据根：

D:\AlifeData\TFTCompanion

规范存储命名空间：

| 相对目录 | 唯一 owner | 允许内容与清理责任 |
|---|---|---|
| data\ | Host 运行时持久化域 | 宿主状态数据库、checkpoint 与最小化 Projection；按保留策略维护 |
| dataagent\ | TftDataAgent | ledger、Projection、知识操作 staging 与完整性检查；详见第 28 节 |
| packs\ | TftDataAgent（仅接收 KnowledgeAcquisition 的受控入站候选） | 已授权原始包与 Manifest；KnowledgeAcquisition 不直接写文件，由 TftDataAgent 按包保留 / 删除策略清理 |
| knowledge\ | TftDataAgent | 规范化事实、索引、chunk 与本地 embedding；不保存当前局面 |
| cache\voice\ | GameVoiceScheduler | 受引用计数、租约、TTL 与容量限制的游戏语音制品 |
| settings\ | ProfileAndStorageStore | 用户偏好、校准、非秘密专属 Profile 与 credentialRef；不读取或写入凭据 blob、密钥材料或 tombstone |
| settings\credentials\ | GameCoachCredentialStore（ProfileAndStorageStore 的唯一可写凭据子端口） | 受 DPAPI 保护的凭据 blob、引用元数据与删除 tombstone；无明文密钥 |
| logs\ | DiagnosticsSink | 白名单化、有界、脱敏运行日志 |
| diagnostics\ | DiagnosticsSink | 用户显式诊断与 replay 导出；默认关闭或短期保留 |
| exports\ | DiagnosticExportPolicy | 用户主动导出的脱敏材料；有大小上限与显式删除 |
| backups\ | StorageMaintenance | 仅可保存经验证的非秘密恢复元数据 / checkpoint；不得保存原始对局载荷、凭据 blob、已删除来源内容或可重新引入已清除知识的完整包 / 索引；用户删除必须使相关恢复材料失效或一并清除 |
| temp\ | 对应写入 owner | 仅短期 staging；有 owner、TTL、容量上限和启动 / 空闲清理 |
| update-staging\ | UpdatePolicy | 已验证更新暂存；失败后有界清理 |
| Skills\ | Expression Skill loader | 版本化、已验证的专属表达 Skill 内容 |

所有 TFT 可控写入，包括嵌套目录、SQLite 的 -wal / -shm、staging、缓存索引、tombstone 与清理记录，都必须经同一个 StorageRootPolicy 解析最终卷、符号链接和目录联接点后验证仍位于该根目录；不得由任一模块自行拼接未验证路径。

路径校验必须解析符号链接、目录联接点和最终卷，不能只检查字符串前缀。

D 盘不可用时进入：

MemoryOnlyDegraded

绝不回退至：

- %LOCALAPPDATA%
- %APPDATA%
- %TEMP%
- C 盘数据库、缓存或诊断文件

初始保留建议：

- 插件自有数据软预算约 2GB。
- D 盘至少保留 5GB 安全空间。
- 普通对局约 30 天或 200 场。
- 规范化事件约 14 天。
- 原始诊断默认关闭并仅短期保存。
- 用户可固定重要对局。

具体阈值需实测校准。

默认不保存：

- Riot ID。
- 玩家真实名。
- 跨局稳定身份。
- 聊天。
- 麦克风音频。
- 连续键盘流。
- 全屏录像。

## 8. 内容包体系

采用：

- RulesPack
- CompPack
- CalibrationPack
- LocalizationCatalog

统一执行：

- Manifest。
- Schema 验证。
- 引用完整性验证。
- 语义验证。
- 图结构验证。
- 黄金回放。
- 局外原子激活。
- 局内版本冻结。

### 8.1 RulesPack

保存客观规则：

- 弈子、羁绊、装备、强化。
- 卡池。
- 商店概率。
- 经验与玩家伤害。
- 英雄复制器。
- 淘汰回池。
- 棋盘坐标。
- 特殊生成与升级语义。

### 8.2 CompPack

不是固定最终棋盘列表，而是：

- CompFamily
- BoardNode
- TransitionEdge
- FallbackEdge

每个重要启动节点必须包含：

- 最低止血板。
- 标准板。
- 上限板。
- 失败回退。
- 装备转移。
- Keep / Sell / DoNotChase。
- 追三策略。
- 人口变化时的模板缩减规则。
- 关键站位角色。

首批建议维护 10～20 个路径完整的阵容家族，而不是大量只有最终截图的阵容。

### 8.3 CalibrationPack

保存：

- BoardStrength 权重。
- 承伤风险阈值。
- 连败安全阈值。
- 升星威胁阈值。
- 追三价值阈值。
- 决策滞回。
- 干预阈值。
- 语音疲劳默认值。

数值决策由确定性代码和内容包负责，不交给 LLM 即兴计算。

## 9. 战略状态模型

三个维度分离：

- StrategicObjective
- PlanState
- UserIntent

StrategicObjective：

- Balanced
- PreserveWinStreak
- PreserveLossStreak
- PreparingToLaunch
- Stabilize
- AllInSurvival
- RecoverEconomy
- CapBoard

PlanState：

    Flexible
      |
      v
    DirectionSelected
      |
      v
    Committed
      |
      v
    PreparingTransition
      |
      v
    RollingDown
      |
      v
    MinimumBoardReached
      |
      v
    StandardBoardReached
      |
      v
    Capped

UserIntent：

- 保守 / 平衡 / 激进。
- 连败锁定。
- 阵容路径锁定。

采用“规则驱动的自适应战略目标 + 用户可手动锁定 + 高危生存覆盖”。

高危生存状态可临时覆盖连败锁，但必须解释，不能后台静默解除用户意图。

优先级：

1. 数据有效和动作合法。
2. 避免近期淘汰。
3. 用户显式意图。
4. 不可逆承诺。
5. 当前战略目标。
6. 阵容路径一致性。
7. 净价值。
8. 个性化。
9. 打扰成本。

### 9.1 连败玩法

连败是正常主流玩法，不能因为低血量、低即时战力或阵容晚成型就自动判断为运营失败。

必须区分：

- 计划内连败。
- 可控延迟启动。
- 即将到达预设启动点。
- 已错过安全启动窗口。
- 因牌、装备、强化或血量变化需要提前止血。

阵容建议还需评估：

- 经济曲线。
- 预计启动回合。
- 启动所需金币。
- 血量缓冲。
- 核心装备和关键弈子。
- 转型成本。
- 最低止血板可达性。
- 失败回退路径。

## 10. StrategicDecisionCoordinator

所有候选基于同一个 DecisionSnapshot，按顺序仲裁：

1. 版本、能力和时效门控。
2. 动作合法性。
3. 硬政策。
4. 共享资源冲突。
5. 支配剪枝。
6. 战略状态化效用。
7. 决策滞回。
8. 生成 StrategicDecision。

共享资源：

- 金币与经验。
- 棋盘格与备战席。
- 弈子。
- 组件与成装。
- 英雄复制器。
- 消耗品。
- 时间。
- 用户注意力预算。

输出：

- 一个主行动。
- 最多两个面板支持行动。
- 最多一个关键观察。
- 停止条件。
- 失败回退。
- 有效期。
- 允许没有主行动。

被抑制候选必须保存原因。

## 11. BoardStrength 与承伤风险 V1

采用成本收缩版：

- 可解释 BoardStrengthVector。
- 阶段相对强度档位。
- 结构缺口。
- 宽承伤风险带。
- 最多评估 4～6 个合法候选。
- 离线阈值校准。

强度维度：

- FrontlineDurability
- SustainedDamage
- BurstDamage
- RampSpeed
- Control
- Sustain
- BacklineAccess
- AntiTank
- ItemUtilization
- TraitCoherence
- Reliability

当前战力不包含：

- 金币。
- 备战席未来潜力。
- 尚未获得的装备。
- 未来可能搜到的牌。

强度输出：

- 高危
- 偏弱
- 可战
- 稳定
- 强势
- 高上限

承伤风险带：

- Low
- Medium
- High
- VeryHigh
- LethalRisk

V1 不做：

- 精确胜率。
- 精确伤害。
- 完整战斗模拟。
- 在线训练。
- 全棋盘搜索。
- LLM 数值决策。

粗略复杂度基线：BoardStrength 子系统约 21～39 人日，不是版本排期承诺。

## 12. 经济、牌库、复制器与追三

### 12.1 经济建议合同

每条经济建议必须包含：

- 预算。
- 金币底线。
- 目标集合。
- 停止条件。
- 失败回退。
- 有效期。

采用“下一关键行动规划”，不做多回合随机优化。

### 12.2 VisiblePoolLedger

统一副本规则：

- 一星 = 1 个基础副本。
- 二星 = 3 个基础副本。
- 三星 = 9 个基础副本。

特殊规则由 RulesPack 覆盖。

输出：

- 确认持有。
- 当前可购买。
- 未来可抽上界。
- 未知保留。
- 命中难度区间。

默认不得声称“精确剩余牌数”，应使用“至少可见”“上界”“区间”和“未知保留”。

### 12.3 英雄复制器

复制器不进入普通装备分配器。

区分：

- confirmedCopies
- applicableDuplicators
- reachableCopies

同一件复制器不能重复分配给多个目标。还需考虑复制器类型、费用限制、当前星级、自然来牌概率、机会成本和战略目标。

### 12.4 对手升星威胁

初始阈值：

- 1～3 费：8 张或三星时重点关注。
- 4 费：6 Watch、7 High、8 Imminent、9 Completed。
- 5 费：5 Watch、6～7 High、8 Imminent、9 Completed。

是否语音还需结合：

- 与己方牌库冲突的相关性。
- 该弈子在对方阵容中的价值。
- 当前阶段。
- 对方是否拥有适用复制器。
- 注意力负载。
- 最近是否已提示同类风险。

### 12.5 追三降噪

分类：

- ActiveChase
- ConditionalChase
- NaturalOnly
- Reject

规则：

- 零持有不进入智能主动追三。
- 大量低费先硬过滤。
- 已经 8 张但价值低，可返回“自然来牌可以完成，不建议额外深搜”。
- 不能只因接近三星就忽略升级人口、保经济、转型或高费卡机会成本。

入口：

- 智能推荐。
- 当前阵容。
- 四费。
- 五费。
- 指定弈子。

价值函数至少考虑：

- 三星边际战力。
- 阵容家族一致性。
- 可继续使用的回合数。
- 已持有与可达副本。
- 牌库竞争。
- 商店等级与费用匹配。
- 搜牌预算和金币底线。
- 复制器机会成本。
- 备战席占用。
- 对生存风险的改善。
- 失败回退。
- 提示噪声。

## 13. 装备与强化

### 13.1 装备

采用：

- 角色需求。
- 装备效果标签。
- 有限全局分配。
- 即时战力、机会成本与转移性。
- 特殊装备逐项白名单。

不能只维护“英雄三件神装”。

类型：

- Component
- StandardCompleted
- Emblem
- Radiant
- Artifact
- Support
- Randomized
- Consumable
- Duplicator
- Unknown

建议还需考虑当前持有者、临时持有者、最终持有者、合法转移、战略目标、组件机会成本和未知特殊效果降级。

### 13.2 强化

采用：

- 已选强化在数据可靠时自动建模。
- 三选一候选能力独立门控。
- 用户主动手动输入回退。
- SessionRuleContext。
- 未知高影响强化按功能降级。

不使用强化 OCR，不自动点击，不强制弹窗。

当前 Overwolf 健康状态含 me.augments 与 me.picked_augment，但三选一候选是否稳定、完整、及时仍需实测。

## 14. 阵容推荐与第三方平台

可关注：

- MetaTFT
- tactics.tools
- TFTAcademy
- Mobalytics
- TFTactics
- Blitz
- LoLCHESS.GG
- OP.GG TFT

目前没有确认到可直接依赖的、稳定且正式授权的公共阵容与站位 API。

路线：

- Riot 或本地数据负责版本。
- 本地 CompPack 负责首版阵容路径与站位知识。
- 第三方攻略平台只作为经正式授权的知识包来源，不作为当前局面的在线推荐或教练 API。
- 预留 IAuthorizedKnowledgePackSource、ILocalGuideRetrieval 与 ILocalStrategyDataAgentAdapter；不保留把实时对局状态提交给第三方的 ICompRecommendationProvider。
- 用户显式启用后，只在启动后、大厅或手动刷新时下载公开 / 授权的知识包；对局中和用户提问时均不按当前局面联网查询。
- 下载请求不得携带当前棋盘、经济、牌库、账号、匹配标识、截图、语音、查询文本、诊断或推荐结果；所有 RAG、DataAgent 联动和建议生成只在本机完成。
- 第三方知识包失败、过期或不兼容时回退至本局冻结的本地内容包和本地规则，不阻塞 Advice。
- 不抓取私有端点。

自动阵容建议按“决策窗口 + 信息增益”触发，适合主动更新的情况：

- 第一次形成明确方向。
- 核心装备、强化或高费卡使最优路径显著变化。
- 原路径达到失败条件。
- 连败进入启动窗口。
- 血量进入必须止血状态。
- 用户主动询问。

普通来牌和小幅评分变化只更新面板。

## 15. 站位系统

### 15.1 信息融合

最终站位建议来自：

- 第三方或本地 PositioningTemplate。
- 当前己方棋盘与单位角色映射。
- Overwolf 可得的大厅威胁摘要。
- 当前版本与规则集。
- 若可靠获得实际对手，可在后台静默微调。

第三方站位只是 BaselineFormation，不能直接照搬截图坐标。

### 15.2 统一逻辑坐标

采用 4×7：

- row 0：靠近敌方。
- row 3：己方底部。
- column 0：己方左。
- column 6：己方右。

不能硬编码 GEP cell_x 或屏幕像素。必须版本化校准：

- GEP 坐标到逻辑坐标。
- 第三方坐标到逻辑坐标。
- 逻辑坐标到 Overlay 像素。

### 15.3 算法与提示密度

采用：

- 角色槽位匹配。
- 不同人口模板缩减。
- 有限结构变体。
- PositioningScoreVector。
- 大厅威胁摘要。
- 默认最多两个关键移动。
- 部分移动后重新验证。
- 逻辑棋盘与像素投影分离。

有限变体：

- Base
- Mirror
- CarrySplit
- AntiBacklineAccess
- AntiAreaDamage
- FrontlineSpread
- FrontlineFocus
- BaitOppositeCarry
- LocalOneOrTwoMoveRepair

不做全排列，不做完整战斗模拟。

Overlay 默认只显示：

- 棋盘格定位。
- 最多 1～2 个关键移动箭头。
- 必要时一个简短原因。

侧边面板按需显示完整目标阵型，热力图后置。

粗略复杂度基线：站位子系统约 35～68 人日，不是版本排期承诺。

## 16. 专属表达 Skill

提示语言不得散落在业务代码中硬编码。

Skill 名称：

render-tft-companion-advice

未来路径：

D:\AlifeData\TFTCompanion\Skills\render-tft-companion-advice\

链路：

    StrategicDecision
          |
          v
    InterventionPolicy
          |
          v
    AdviceSemanticCompiler
          |
          v
    AdviceExpressionRequest
          |
          v
    render-tft-companion-advice
          |
          v
    AdviceExpressionResult
          |
          v
    ExpressionContractValidator
          |
          v
    AdviceCoordinator
          |
          v
    GameVoiceScheduler
          |
          v
    IGameCoachTtsProvider
          |
          v
    ICoachPlaybackAdapter

原则：

- 策略决定“说什么事实”。
- Skill 决定“怎样说”。
- Skill 不能修改数字、动作、预算、金币底线、停止条件或失败回退。
- 关键数字使用锁定槽位，例如 {{GOLD_FLOOR}}。
- 一次生成语音、Overlay、面板三个通道候选版本；任一不适用、未完成或被拒绝的通道必须显式为 absent。
- 三个通道绑定同一 semanticDigest。
- fallback templates 位于 Skill 内，不散落在业务代码。
- 关键告警优先使用已编译、已验证模板。
- Skill 纯表达、无工具、无网络、无游戏权限。
- 一局冻结 Skill 版本。
- Skill 缺失、超时或输出无效时宁可沉默。
- 默认语气是尊重选择的安静陪伴教练，不是指挥者。

现有 Alife 参考：

- alife-service/sources/Alife.Function/Alife.Function.Skill/SkillService.cs
- alife-service/sources/Alife.Function/Alife.Function.MessageFilter/MessageFilterService.cs
- alife-service/sources/Alife.Function/Alife.Function.Speech/SpeechService.cs
- alife-service/sources/Alife/Alife.Platform/AlifePath.cs

已确认：

- SkillService 的文件布局只可作为加载经验参考；TFT 专属 Skill 必须经自身 D 盘 StorageRootPolicy 定位。
- MessageFilter 是通用提示注入，不承载 TFT 专业话术。
- QQ 保留 `SpeechService`、其本地 TTS 与队列；游戏教练不复用其服务实例、Provider、凭据、缓存、预算或播放租约。
- 游戏链路使用 IGameCoachTtsProvider 与 ICoachPlaybackAdapter；GameVoiceScheduler 负责阶段、抢占、过期和闭环。
- FastPathExpressionSkill 是 render-tft-companion-advice 内已编译、经合同验证的模板层，不是第二个独立 Skill 或第二条表达链。

## 17. Advice 基础合同

每条 Advice 在 Proposed / Eligible 阶段至少具有：

- runtimeEpoch。
- sessionId、sessionEpoch、roundKey。
- adviceId、adviceRevision、logicalAdviceKey、replaceKey 与 supersedesAdviceId。
- decisionSnapshotId、ingressStateSnapshotId、knowledgeSnapshotId 与 stateRevision。
- scopeKey、scopeKind、expiryBehavior、semanticDigest、priorityClass、createdAt、validFrom 与 expiresAt。
- strategicObjective、sourceCandidateIds 与可选 suppressionReason。

voiceText、overlayText 与 panelText 不属于 Proposed / Eligible Advice 的基础字段。它们只能存在于通过 ExpressionContractValidator 的 AdviceExpression 或对应 Delivery 载荷中，并绑定 SkillVersion、messageKey、锁定槽位和同一 semanticDigest；表达尚未完成、被拒绝或过期时，三种文本显式 absent。

scopeKey 是 Current、Supersede 与 Delivery 校验使用的唯一冲突范围键，至少编码 session、round、decisionWindow 与 stage；scopeKind 仅分类为第 22.12 节的 DecisionWindowBound、RoundBound、SessionBound 或 UserRequestBound，不能替代 scopeKey。expiryBehavior 只能取第 22.12 节定义的版本化策略；不再使用未定义且不足以表达完整范围的 `roundScope`。

基础约束：

- 新 revision 可 Supersede 旧 Advice。
- 过期 Advice 不得继续播报。
- 战斗开始后，上一备战阶段的普通消息不得补播。
- 高优先级风险可抢占低优先级陪伴消息。
- 同一 semanticDigest 不应跨通道重复轰炸。
- 面板可保留历史解释，语音与 Overlay 严格服从有效期。
- TTS 失败不能阻塞后续生命周期。
- 用户静音、跳过或关闭提示应产生明确回执状态。

## 18. 成本控制

- 先做可解释规则与内容包，不做在线学习。
- 先做宽风险带，不做伪精确胜率。
- 先评估 4～6 个合法候选，不做全行动空间搜索。
- 先维护 10～20 个完整阵容家族，不追求海量浅层阵容。
- 先做最多两个关键站位移动，不做整盘实时指挥。
- 先用本地 CompPack，正式授权后再接第三方。
- 先使用回合结束快照，避免为每次换位付出高频成本。
- 先保证未知时正确沉默，再追求覆盖率。
- LLM 只承担受约束表达，不承担数值事实与最终仲裁。

## 19. 尚待继续讨论并确认

后续一次讨论一个关键问题：

1. GameVoiceScheduler：
   - 已确认：语义短句切片、最多预取下一片、在片段边界软接管；详见第 23 节。
   - 已确认：游戏教练拥有独立队列、独立 TTS 和独立故障域；QQ 与插件不同时播放是外部启动 / 使用前提，插件仅保证逻辑隔离且不设计跨来源抢占；详见第 23 节。
   - 已确认：1 个 Active 加最多 4 个 Pending 的有界优先队列，以及替换、去重、淘汰和出队复检；详见第 23 节。
   - 已确认：最多一个 PreparingCandidate、正常 TTS 单并发、仅 P1 可选紧急旁路的混合准备策略；详见第 23 节。
   - 已确认：有句柄的 PlaybackSession、真实 Started、唯一终态与资源释放后交接；详见第 23 节。
   - 已确认：真实终态驱动的自适应停顿、800ms 软边界和版本化 VoiceTimingProfile；详见第 23 节。
   - 已确认：截止时间感知的 Primary/Secondary TTS 路由、P1 受控 hedge、错误分类、熔断与费用预算；详见第 23 节。
   - 已确认：复用统一单调 deadline heap 的 PhaseDeadlineRegistry、两阶段 grace 与播放资源降级边界；详见第 23 节。
   - 已确认：安全边界跟随默认设备、开始前一次换设备重试、Started 后不自动重播与恢复后不补播；详见第 23 节。
   - 已确认：D 盘持久偏好、临时控制状态、统一静音/跳过/取消命令、音量与陪伴强度控制；详见第 23 节。
2. 侧边面板与透明 Overlay：
   - 已确认：采用“棋盘内极简提示 + 边缘 Dock + 独立侧边 Panel”的混合式布局；详细定位与安全合同见第 24 节。
   - 已确认：窗口定位、DPI 与多显示器、尺寸变化、游戏内画面缩放、透明穿透、像素投影校准、失焦与战斗阶段隐藏，统一由动态 ViewportTransformTracker 处理；详细合同见第 24 节。
   - 已确认：只有 High 置信度时才显示精确六边形格、移动箭头与短标签；缩放、漂移或定位不确定时先隐藏棋盘内提示，再重新定位，Dock 与 Panel 继续工作；详细合同见第 24 节。
3. Overwolf Bridge 与 Alife IPC：
   - 已确认：采用独立 TftCompanionHost、Overwolf Background Bridge 唯一订阅 GEP、双物理 Loopback WebSocket 通道、语义消息与真实回执的主架构；详见第 25 节。
   - 已确认：Background Bridge 只采集、校验、转发和受限渲染，不做策略、持久化、TTS、第三方 Provider 或游戏控制；详见第 25 节。
   - 已确认：命名管道 Sidecar 仅作为未来需要更强本机隔离时的升级路线；文件投递、SQLite polling、HTTP 长轮询不作为实时路径；详见第 25 节。
   - 已确认：协议协商、最小权限、双向鉴权、背压、重连、重复、乱序、gap、快照重建、旧消息隔离与 D 盘边界；详见第 25 节。
   - 待 PoC：验证当前 Overwolf Native Runtime 的 loopback WebSocket、Origin、CSP、manifest 配置及 Overlay 重建期间内部消息行为；未通过不得以轮询或文件中转替代，须评估 Sidecar / Named Pipe 路线。
4. 隐私、诊断导出、游戏教练专属 LLM 的最小上下文授权与最终整合：
   - 已确认：第三方攻略只作为本地 RAG 知识包来源，用户显式启用后仅在启动后、大厅或手动刷新时下载；绝不上传当前局面或查询；详见第 26 节。
   - 已确认：热门阵容、统计指标、版本弈子、装备、强化、站位模板和运营知识均可进入带来源、版本、统计口径与许可证的本地知识包；详见第 26 节。
   - 已确认：TftDataAgent 只做本地知识包与索引的查找、验证和保存；不分析、不解释、不推荐、不调用 LLM；禁用 HTTP Sidecar、外部 LLM Planner、External RAG 与 Postgres；详见第 26 节。
   - 已确认：TftDataAgent 保存本地原生证据链，用于重建知识包获取、校验、规范化、索引、发布和查询流程状态；不记录原始对局状态或 LLM 解释；详见第 28 节。
   - 已确认：游戏教练专属 LLM 使用最小化结构化上下文，预期模型族为 Grok 4.5；FastPath 无任何 LLM；实际 model id / endpoint、密钥、费用、超时与离线降级详见第 27 节并在 PoC 时验证。
5. 全系统确定性回放、黄金场景、故障注入和性能预算：
   - 已确认：以脱敏、合成的 Replay Capsule、虚拟单调时钟、真实核心链路和受控 fake adapter 做确定性验证；将正确性回放、性能 Harness、真实 Overwolf 集成和人工自定义局验收严格分层；详见第 29 节。
   - 已确认：任何旧 runtime/session/revision/viewGeneration/attempt/lease 的迟到回调均不得复活语音、棋盘箭头或旧 Delivery；在 gap、知识版本不兼容、低定位置信度或 D 盘故障时必须安全降级；详见第 29 节。
6. 整体架构一致性复核：
   - 已确认：采用一个独立 TftCompanionHost 内的受监督分域 Runtime；保持 Overwolf Background Bridge + 双 Loopback WebSocket 的既定物理拓扑，不新增首版常驻 Sidecar、通用事件总线或内部微服务；详见第 30 节。
   - 已确认：状态唯一所有者、Runtime / Session 生命周期与健康度正交、热冷路径、跨领域端口方向、PoC 依赖和架构验收门；详见第 30 节。
7. 所有设计确认后的最终规格整合、自检与用户审阅。
8. 只有最终规格获批后，才进入版本规划与实施计划。

## 20. 当前明确不做

- 游戏自动操作。
- 进程内存读取或注入。
- 抓包或协议绕过。
- 安卓模拟器控制。
- 按队列类型启停。
- 实时候选对手身份播报。
- 每次棋子换位都播报。
- 全棋盘战斗仿真。
- 精确胜率与精确承伤承诺。
- 未授权第三方私有 API 抓取。
- LLM 即兴决定金币、卡池或站位数值。
- 普通陪伴消息跨阶段排队。
- 为追求“有话可说”消耗注意力。

### 20.1 首期范围护栏（已确认：避免过度设计）

本节只固定首期实现边界，不构成版本排期，也不删除已经确认的长期合同。首期目标是验证一个可靠、安静、只读的本地陪伴教练闭环，而不是同时建设攻略平台、云模型产品、审计仓库和测试基础设施平台。

首期只闭合：

    只读 Overwolf / GEP
    → 最小 session / round / freshness gate
    → 本地 RulesPack + 有限结构化 CompPack
    → 冻结 DecisionSnapshot / KnowledgeFactView
    → 本地确定性策略与同一个 render-tft-companion-advice Skill
    → AdviceCoordinator
    → Panel 优先
    → 仅 High 置信度时的鼠标穿透 Overlay
    → 可选、单 Provider、永不阻塞其他通道的游戏语音

首期必须保留的不是完整平台化实现，而是：严格只读、D 盘唯一可控写入且不回退 C 盘、FastPath 不等待网络 / LLM / RAG / SQLite、DataAgent 不分析不推荐、低定位置信度或事实缺口时安全降级、断线 / 切局 / 缩放 / Supersede 后旧 Voice 与旧 Overlay 不复活、以及用户静音 / Skip 的最终优先权。

以下能力明确后置，不得阻塞首期最小闭环：

- RichPath、Grok 或其他远程 LLM 请求，以及其 endpoint、费用、复杂审计和长解释能力。
- 多 Provider、P1 hedge、预取、复杂熔断 / 费用模型、设备恢复和完整 VoiceTimingProfile；首期只需一个 Active、最多一个 Pending、过期即丢弃的独立游戏语音链。
- 多来源、多地区 / 分段指标、embedding、rerank、自由文本 RAG 和完整知识包平台；首期只使用版本冻结的本地结构化事实。
- 查询级 PinnedEvidenceBundle、hash-chain / DAG、复杂 Projection 重建、compaction 和维护 Panel；首期证据链只需保留 Pack、Manifest、hash、patch、校验结果、发布 / 拒绝原因和 active snapshot。
- 全量 Replay Capsule、所有 fake adapter、完整黄金场景库、性能实验室、长时 soak、Sidecar / Named Pipe / Job Object 升级路线，以及备份 / 导出 / 更新暂存体系。
- 未在本节首期闭环内、且不能直接提升当前局本地正确建议、低打扰展示或只读安全的新增组件。

后置不等于取消：这些长期设计仍是未来启用相关能力时必须遵守的合同。进入版本规划前，任何候选工作都必须先证明自己属于上述首期闭环；否则默认后置。

## 21. 检查点结论

当前设计已明确：

- 产品边界与平台路线。
- 数据来源和能力门控。
- 版本冻结。
- 事件、快照与重连。
- 本地存储和 D 盘约束。
- 内容包。
- 战略状态与中央仲裁。
- 基础战力评估。
- 经济、牌库、复制器和追三。
- 装备与强化。
- 阵容与站位。
- 第三方 Provider 边界。
- 专属表达 Skill。
- AdviceCoordinator 的完整生命周期、通道回执、确定性仲裁、恢复与验收合同。
- GameVoiceScheduler 的语义短句切片、有限预取与 D 盘语音缓存基础合同。
- 游戏教练的独立语音生产链；QQ 与插件不同时播放属于外部使用前提，插件不进入跨来源播放仲裁且两条链逻辑隔离。
- GameVoiceScheduler 内部 1 Active + 4 Pending 的有界优先队列、确定性合并与满队列淘汰合同。
- Pending 语义计划的有界混合 TTS 准备、单正常并发和 P1 可选紧急旁路合同。
- PlaybackSession 的真实开始、自然 EOF、停止意图、唯一终态和资源释放合同。
- 同 Advice pauseClass、跨 Advice 优先级间隔、P1/P2/P3/P4 接管与可取消 gap 的自适应时序合同。
- 游戏 TTS 的 deadline-aware fallback/hedge、Provider 熔断、Winner 仲裁和成本降级合同。
- TTS、PlaybackSession 和资源释放的分阶段 watchdog、deadlineGeneration 和两阶段强制终态合同。
- 默认设备安全边界切换、设备丢失、开始前一次重试、Started 后不重播与恢复后不补播合同。
- D 盘 VoicePreference、EphemeralVoiceControlState、静音/跳过/取消、音量和陪伴强度的用户控制合同。
- 全系统确定性 Replay Capsule、黄金场景、故障注入、性能口径和发布验收门。
- 独立 Host 内受监督分域 Runtime 的状态唯一所有者、生命周期、端口方向、PoC 依赖和总体一致性验收门。

AdviceCoordinator 的消息生命周期、通道回执、确定性仲裁、恢复和验收合同已经在第 22 节确认。GameVoiceScheduler 的队列、抢占、时序、TTS、watchdog、设备和用户控制合同已经在第 23 节确认并获用户审阅。Overlay 的动态定位、缩放安全降级、透明穿透和混合式呈现合同已经在第 24 节确认。Overwolf Bridge、独立 TftCompanionHost、双通道 IPC、回执和恢复合同已经在第 25 节确认，但仍有明确的 Overwolf loopback 兼容性 PoC 门。第三方攻略的本地知识包、RAG、统计口径、TftDataAgent 隔离、原生证据链和无上传合同已经在第 26 与第 28 节确认；游戏教练专属 LLM 的最小化远程上下文、FastPath / RichPath、Grok 4.5 配置抽象和隔离合同已经在第 27 节确认，实际 Provider / 密钥 / endpoint 仍需 PoC 验证。第 29 节已经确认了全系统脱敏回放、黄金场景、故障注入、性能预算和最小发布验收门；第 30 节已经确认了整体运行拓扑、状态所有权、生命周期、端口方向和 PoC 依赖。剩余设计工作是最终规格的交叉整合、自检和用户审阅；之后才可讨论版本规划。本文是后续讨论的稳定基线，不是可直接实施的最终规格。

## 22. AdviceCoordinator 完整设计

本节记录已经逐项获得用户确认的 AdviceCoordinator 设计。它补充并细化第 17 节，不进入代码实现或版本规划。

### 22.1 设计选择

采用：

    统一语义父 Advice
    +
    Voice、Overlay、Panel 分通道 Delivery
    +
    单写者状态机
    +
    明确终态回执
    +
    轻量本地执行

未采用：

- 单一全局线性消息状态。
- 三个通道完全独立生成和管理消息。
- 通用重量级工作流引擎。
- 分布式消息系统。
- 依赖固定延迟猜测播放是否结束。

用户主动请求采用软接管：

- 立即取消尚未播放的普通被动语音。
- 正在播放的普通短句优先在当前语义边界结束。
- 内容已过期、剩余过长或出现高风险冲突时请求平滑停止。
- P1 致命告警高于普通用户问题。
- 用户请求默认不清空仍然有效且不冲突的 Overlay 或面板内容。

### 22.2 父 Advice 状态与双结论

父 Advice 阶段：

    Proposed
      |
      v
    Eligible
      |
      v
    Expressing
      |
      v
    Dispatching
      |
      v
    Active
      |
      v
    Closing
      |
      v
    Closed

阶段含义：

- Proposed：已从 StrategicDecision 建立不可变语义载荷，但尚未通过发布资格检查。
- Eligible：数据、版本、时效、合法性与干预门控已通过，通道计划已冻结。
- Expressing：专属 Skill 或已验证模板正在产生三个通道的表达。
- Dispatching：表达合同已通过，正在原子创建和提交 Delivery。
- Active：至少一个已创建 Delivery 尚未终止。
- Closing：不再接受新 Delivery，正在撤销、停止、隐藏或等待收尾。
- Closed：所有已创建 Delivery 均已终止，父 Advice 永久不可重新打开。

生命周期阶段、关闭原因和交付结果必须分开。

AdviceCloseCause：

- CompletedNaturally
- SuppressedByPolicy
- InvalidOrStaleInput
- ExpressionFailed
- Expired
- Superseded
- UserSkipped
- RoundEnded
- SessionEnded
- SafetyRevoked
- ApplicationShutdown
- InternalFailure

AdviceDeliveryOutcome：

- NotDispatched
- FullyDelivered
- PartiallyDelivered
- Undelivered
- ForcedClosedUnknown

Closed 只表示闭环完成，不等于全部通道成功。例如 Voice 失败、Overlay 与 Panel 已交付时，父 Advice 可以 Closed，CloseCause 为 CompletedNaturally，DeliveryOutcome 为 PartiallyDelivered。

### 22.3 父状态合法转移

合法转移：

- Proposed → Eligible：Snapshot、版本、合法性和时效检查通过。
- Proposed → Closed：硬门控失败、策略抑制、已经过期或已有更新 revision。
- Eligible → Expressing：Advice 仍有效并开始表达。
- Eligible → Closed：表达前失效。
- Expressing → Dispatching：Skill 输出或 FastPath 模板通过合同验证。
- Expressing → Closed：表达失败且无合法 fallback。
- Dispatching → Active：Delivery 子记录已提交，至少一个仍非终态。
- Dispatching → Closing：提交期间收到过期、回合结束或 Supersede。
- Dispatching → Closed：所有 Delivery 在提交阶段已经终态化。
- Active → Closed：所有已创建 Delivery 自然进入终态。
- Active → Closing：过期、替换、用户跳过、回合结束、断线或会话结束。
- Closing → Closed：所有 Delivery 已终止，或 watchdog 将无回执任务明确终态化。
- Closed → Closed：只允许审计迟到事件，业务状态不再变化。

以下属于非法转移并必须拒绝：

- Proposed → Active
- Expressing → Active
- Closing → Dispatching
- Closed → Active
- Closed → Expressing

### 22.4 单写者与事件权限

只有 AdviceCoordinator 可以修改：

- AdvicePhase。
- AdviceCloseCause。
- AdviceDeliveryOutcome。
- Delivery 当前状态。
- 当前 revision 指针。
- replaceKey、resourceKey 和 scopeKey 的当前索引。

其他组件只能提交事实或请求：

- StrategicDecisionCoordinator 提交 StrategicDecision。
- InterventionPolicy 提交 EligibilityResult 与 ChannelPlan。
- 表达 Skill 提交 AdviceExpressionResult。
- ExpressionContractValidator 提交 Valid 或 Invalid。
- GameVoiceScheduler 提交 VoiceDeliveryReceipt。
- Overlay Host 提交 OverlayDeliveryReceipt。
- Panel Store 提交 PanelCommitReceipt。
- Session 与 Round Tracker 提交窗口、回合、断线和会话事件。
- 用户入口提交 Skip、Mute、Dismiss 与 UserRequest。
- Watchdog 提交 TimeoutObserved。

任何组件都不能绕过 AdviceCoordinator 直接关闭父 Advice 或直接触发其他通道。

### 22.5 revision 与幂等

每个不可变 revision 使用独立 adviceId：

    logicalAdviceKey = positioning.primary
    adviceId = A-101
    adviceRevision = 4
    supersedesAdviceId = A-100

规则：

- 新 revision 不复用旧 adviceId。
- 旧 revision 进入 Closing，新 revision 独立准备和发布。
- 旧回执只能更新旧 adviceId 和 deliveryId。
- Closed Advice 不重新打开；纠正必须生成新 revision。
- 同一 replaceKey + scopeKey 最多一个 Current revision。
- 重复 eventId 或 receiptId 只处理一次。
- 同一 Delivery 状态只能前进。
- 迟到回执可写诊断，但不能造成非法逆转。
- 状态转移、审计事件和必要持久化处于同一事务。
- 事件顺序以 AdviceCoordinator 分配的 coordinatorSequence 为最终决胜依据。

### 22.6 Delivery 通用合同

每个实际创建的 Delivery 使用：

    Pending
      |
      v
    Submitted
      |
      v
    Accepted
      |
      v
    Preparing
      |
      v
    Queued
      |
      v
    Active
      |
      v
    Closing
      |
      v
    Terminal

通道可以按声明的合法路径跳过不适用阶段，但不能任意逆转。

统一 DeliveryTerminalResult：

- Delivered
- Skipped
- Expired
- Superseded
- Interrupted
- Cancelled
- Rejected
- Failed
- TimedOut

具体原因通过 reasonCode 表达，例如：

- UserMutedChannel
- UserSkippedAdvice
- ExpiredBeforeSubmit
- ExpiredWhileQueued
- RoundEnded
- SessionEnded
- Disconnected
- NewerRevision
- UserInitiatedSoftTakeover
- TtsGenerationFailed
- AudioDeviceUnavailable
- AudioPlaybackFailed
- OverlayWindowUnavailable
- OverlayProjectionInvalid
- PanelCommitFailed
- AcceptanceTimeout
- PreparationTimeout
- CompletionTimeout
- InvalidReceipt
- SourceInstanceReplaced
- ProcessRestart

所有已经创建的 Delivery 最终都必须 Terminal。Required 与 BestEffort 只影响父级成功分类，不能允许 BestEffort 永久悬挂。

### 22.7 标准回执

DeliveryReceipt 至少包含：

- receiptId
- eventId
- runtimeEpoch
- sessionId
- sessionEpoch
- roundKey
- adviceId
- adviceRevision
- deliveryId
- attemptId
- channel
- eventKind
- sourceInstanceId
- sourceSequence
- observedAtMonotonic
- observedAtWall
- semanticDigest
- terminalResult
- reasonCode
- errorCategory
- metrics

deliveryId 标识逻辑通道任务；attemptId 标识同一任务的一次具体尝试。重试不能创建第二个逻辑 Delivery。

sourceInstanceId 用于区分通道重启前后的实例。旧实例迟到回执只能进入诊断。

semanticDigest 必须与父 Advice 一致。Digest 不匹配的回执不得更新业务状态。

命令与事实严格分开：

- AdviceCoordinator 发出 SubmitDelivery、RequestSoftStop、RequestImmediateStop、RequestHide、RequestSupersede 或 RequestCancel。
- 通道返回 Accepted、PlaybackStarted、PlaybackCompleted、OverlayShown、PanelCommitted、Interrupted、Failed 或其他事实回执。
- 发出停止命令不能立即把 Voice 标记为 Interrupted；必须等待真实停止回执或 watchdog 超时。

### 22.8 三通道 Delivered 定义

VoiceDelivery 只有满足以下条件才算 Delivered：

    PlaybackStarted
    +
    播放器真实自然完成
    +
    无播放异常

以下均不算 Delivered：

- TTS 请求已提交。
- 音频文件已生成。
- 已进入语音队列。
- 普通 SpeechService.SpeakAsync 已返回。
- 刚开始发声。
- 因取消触发了无法区分原因的 PlaybackStopped。

播放后被软接管时：

    terminalResult = Interrupted
    reasonCode = UserInitiatedSoftTakeover

同时记录 playedDuration、estimatedTotalDuration、playedFraction、lastSemanticBoundary 和 stopWasGraceful。

OverlayDelivery 必须真正显示、投影仍有效并达到 minimumVisibleDuration 才算 Delivered。达到最低可见时间后，Delivery 可以终止为 Delivered，视觉投影可以继续保留到 expiresAt；后续隐藏属于 UI Projection 清理，不继续阻塞父 Advice。

PanelDelivery 在对应 adviceId + adviceRevision 被原子提交到面板状态模型时算 Delivered。不要求用户已经打开面板、滚动到卡片或点击确认。后续折叠、移入历史或用户关闭卡片不倒退修改原 Delivery 结果。

### 22.9 优先级与冲突键

三类键必须分开：

- replaceKey：同一语义问题的新旧答案，例如 positioning.primary。
- resourceKey：不同语义对同一物理资源的竞争，例如 voice.game。
- scopeKey：Advice 所属 session、round、decisionWindow 和 stage。

固定优先级：

- P0 HardInvalidation：会话结束、身份不一致、断线和安全撤销。
- P1 EmergencySafety：即将淘汰、致命风险、严重版本或数据错误。
- P2 UserInitiated：用户提问和主动分析按钮。
- P3 CriticalTactical：关键停止条件、高费卡升星威胁和必须止血。
- P4 OrdinaryCoach：常规经济、阵容和站位建议。
- P5 Companion：鼓励、轻陪伴和非必要观察。

P1 高于普通用户回答；P2 可以软接管 P3～P5；P4 与 P5 不得延迟用户主动请求；P5 默认不进入长队。

### 22.10 两阶段 Supersede

新 revision 先进入 PrepareReplacement：

- 完成版本和能力门控。
- 完成 StrategicDecision。
- 完成干预策略。
- 完成表达和合同验证。
- 完成 Delivery 计划。
- 再次检查有效期。

旧 revision 只要仍然正确，可以继续维持。

只有新 revision 已可发布时，才原子执行 ActivateReplacement：

    旧 current revision → Closing / Superseded
    新 revision → Dispatching
    currentRevisionPointer → 新 revision

如果旧 Advice 已被证明错误、已经过期、数据源失效或安全策略撤销，则不等待替代品，立即关闭。

若 revision 5 在 revision 4 尚未发布时到达，revision 4 直接 SupersededBeforeDispatch，只保留最新版本继续准备。

### 22.11 semanticDigest 去重

如果新旧 semanticDigest 相同：

- Voice 默认 Skipped / SemanticDuplicate。
- Panel 更新来源回合、观察时间与新鲜度。
- Overlay 在投影仍有效时更新有效期；窗口或坐标变化时重新投影，但不重复语音。
- 审计记录 SemanticRefresh。

semanticDigest 覆盖主行动、目标、预算、金币底线、停止条件、失败回退、关键理由和风险等级，不包含 observedAt 这类纯新鲜度字段。

### 22.12 过期与 scope

每条 Advice 在六个边界检查有效期：

1. Eligibility 前。
2. 调用表达 Skill 前。
3. 表达返回后。
4. 创建 Delivery 前。
5. 通道实际开始显示或播放前。
6. Active 期间由统一 deadline 调度器检查。

不为每条 Advice 建立独立 Task.Delay，采用一个本地 deadline heap 或 timer wheel。

ExpiryBehavior：

- DropIfNotStarted：未开始直接过期。
- FinishIfAlreadyActive：排队中取消，已开始允许短宽限后结束。
- WithdrawImmediately：继续说或显示会误导时立即撤回，Voice 在最近安全边界停止。

Scope：

- DecisionWindowBound：普通经济、搜牌和站位。
- RoundBound：当前回合。
- SessionBound：长期阵容方向和用户锁定。
- UserRequestBound：用户主动问题。

旧窗口先关闭，再开放新 decisionWindowId。普通被动语音不跨回合。用户主动答案如果包含当前行动建议，跨回合时必须用新快照重新评估；只解释历史事实时可以保留上一快照标记。

### 22.13 用户主动软接管

UserRequest 到达后：

- 分配 P2。
- 清除尚未开始的 P3～P5 被动语音。
- 当前 P4/P5 Voice 请求软停止。
- 当前 P3 若即将结束且仍有效可短暂说完，否则在语义边界停止。
- 当前 P1 短告警先完成，用户回答随后开始。
- Panel 可以立即显示用户请求结果，不等待语音。
- 不冲突的 Overlay 默认保留。

重复按钮使用 requestDedupKey 去重。不同用户请求保留输入顺序。语音最多一个正在播放和一个待播放的用户回答；队列超限时面板仍返回结果，但不能为了 FIFO 播放过期答案。

### 22.14 关闭原因仲裁

一个 Advice 可以同时保存：

- primaryCloseCause
- contributingCloseCauses

主原因优先级：

    SafetyRevoked / SessionEnded
    >
    UserSkipped
    >
    Superseded
    >
    RoundEnded
    >
    Expired
    >
    CompletedNaturally

Closing 期间出现更高优先级原因时，可以提升 primaryCloseCause，原原因进入 contributing 列表。Closed 后不再修改业务结果。

同一 replaceKey 由更高 adviceRevision 获胜；不同 replaceKey 争用同 resourceKey 时由更高 priorityClass 获胜；相同优先级使用确定性的 coordinatorSequence，不使用线程调度、随机数或字典遍历顺序。

### 22.15 轻量执行与延迟

完整合同不等于重量级运行时。采用：

- 内存状态投影。
- 单写者事件循环。
- SQLite WAL 短事务。
- 三通道异步并行。
- 有界队列。
- 只持久化关键里程碑。
- 同 replaceKey 最新 revision 优先。
- 外部 I/O 不在协调器循环中等待。

AdviceCoordinator 循环禁止等待 TTS、音频播放、Overlay 窗口搜索、第三方 API、远程 LLM、长数据库查询或用户输入。

三个通道在表达合同通过后并行开始：

    Panel Commit
    Overlay Prepare
    Voice Synthesis

只持久化 Advice 创建、门控结果、Delivery 创建、通道接受、首次真实交付、关闭请求、Delivery 终态和 Advice Closed。高频播放进度、动画帧、音量采样和 Overlay 每帧坐标只在内存中维护。

语音队列有界：

- 正在播放最多一条。
- 下一条高优先级或用户请求最多一条。
- 普通被动消息默认不排长队。
- 已接近过期的内容直接丢弃。
- 已生成 TTS 不构成必须播放的理由。

### 22.16 表达 FastPath 与 RichPath

FastPath 用于致命风险、高费卡即将三星、回合结束前的关键停止条件、数据断线、版本失配和用户主动触发的短结论。

FastPath 使用专属 FastPathExpressionSkill 内已经编译、经过合同验证的版本化模板，只填锁定槽位，不调用远程 LLM、任何本地生成式 LLM 或 DataAgent。提示语言不硬编码在业务逻辑中，而由专属 Skill 的语言包、消息键、条件、变体选择和锁定槽位控制。

RichPath 用于用户要求详细解释、回合总结、复杂转型、装备经济权衡和赛后复盘。RichPath 可以较慢，但不能阻塞面板先显示结构化结论。

### 22.17 初始性能预算

这些是工程目标，需要在固定硬件基线上实测：

- GEP 事件进入本地 Reducer：P95 ≤ 10ms，不含 Overwolf 自身发送延迟。
- AdviceCoordinator 单次转移：P50 ≤ 2ms、P95 ≤ 5ms、P99 ≤ 15ms。
- 事件循环单次不可中断占用：≤ 20ms。
- 本地规则分析与候选仲裁：P95 目标 50～150ms。
- FastPath 表达：P95 ≤ 10ms。
- 面板首个结构化结果：从 StrategicDecision 可用起 P95 ≤ 100ms。
- 已校准窗口的 Overlay 首显：P95 ≤ 150ms。
- Supersede 决策和取消尚未播放语音：P95 ≤ 20ms。
- 软接管等待语义边界：目标不超过约 800ms，最终上限由 GameVoiceScheduler 章节确认。
- 远程 LLM 和远程 TTS 延迟单独计量，不归因给 AdviceCoordinator。

ClosureLatency 可以包含真实语音时长，它不等于 FirstVisualLatency 或 FirstAudioLatency，也不应阻塞无冲突通道。

### 22.18 RuntimeEpoch 与恢复

每次启动生成新的 runtimeEpoch 和 coordinatorInstanceId。正常关闭写入 RuntimeStoppedCleanly、lastCommittedSequence 和 stoppedAt。

异常重启后：

- 恢复审计，不恢复旧播放。
- 旧 VoiceDelivery 不自动重放。
- 旧 Overlay 不自动恢复。
- Panel 只能先恢复为 Historical 或 StaleUntilRevalidated。
- 旧非终态 Advice 与 Delivery 使用明确原因终态化。
- 同一局恢复也基于新 DecisionSnapshot 创建新 Advice。
- 无法证明同一局时，旧 Session 标记 Interrupted；仅当当前 snapshot 自身支持启动时，才创建不继承旧 Advice、旧视觉或旧 Voice 的 Starting(RecoveredPartial)。

Dispatch 使用本地 Outbox/Inbox：

- Advice 状态、Delivery 和 OutboxCommand 在同一事务提交。
- Dispatcher 在事务后异步发送。
- Receipt 先通过 Inbox 按 receiptId 和 eventId 去重。
- 重启后不盲目重放实时 Voice、Overlay 和旧软停止命令。
- Panel 历史与本地审计可在验证后幂等恢复。

### 22.19 watchdog 与通道健康

watchdog 使用两阶段超时：

    DeadlineReached
      |
      v
    发取消或健康探测
      |
      v
    GracePeriod
      |
      v
    ForcedTerminal

watchdog 不能伪造 Delivered，只能在无回执时标记 TimedOut，并携带 AcceptanceTimeout、PreparationTimeout 或 CompletionTimeout。

每个 watchdog 事件包含 deliveryId、attemptId、expectedPhase 和 deadlineGeneration，旧 attempt 的计时器不能终止新 attempt。

通道健康状态：

- Healthy
- Degraded
- Unavailable
- Recovering

Voice 不可用时 Panel 与 Overlay 继续，不能使用 Voice 播报语音故障。Overlay 不可用时完整站位转移到 Panel。所有通道不可用时 Advice 闭环为 Undelivered，不无限重试。

TTS 忽略取消时，协调器不等待；迟到音频因 attemptId 不匹配而丢弃，并在 D 盘进入有界清理。普通已经开始的语音失败后默认不自动从头重播。

### 22.20 当前 SpeechService 边界

现有 SpeechService 仅保留给 QQ / 通用语音，不属于游戏教练的合成、播放、队列、缓存、预算、凭据或故障链。游戏链路只能借鉴低层适配经验，不能复用其服务实例或默认路径。

当前 SpeakAsync 不能作为 Advice 播放完成回执；现有 DisposeAsync 直接等待 playAudioTask，也不能成为游戏链路的无界关闭机制。

未来边界：

    GameVoiceScheduler
      |
      v
    IGameCoachTtsProvider
      |
      v
    ICoachPlaybackAdapter

IGameCoachTtsProvider、ICoachPlaybackAdapter 与 GameVoiceScheduler 负责带 deliveryId 的真实播放回执、软接管、设备变化、有界关闭和跨回合清队列。TFT 规则不塞入通用 SpeechService。

### 22.21 SQLite 与 D 盘故障

宿主状态数据库位于 D:\AlifeData\TFTCompanion\data\；TftDataAgent ledger 使用独立的 dataagent\ 命名空间。

启动检查 Schema、WAL、必需表、最近投影和轻量一致性。数据库损坏时不自动删除原库，只在 D 盘隔离并尝试新库或 MemoryOnlyDegraded。

D 盘中途不可写时：

- 内存状态继续运行。
- 停止持久化。
- 不回退 C 盘。
- 关闭依赖完整历史连续性的功能。
- D 盘恢复后从新的安全边界恢复写入。
- 不把不完整事务伪装成连续历史。

启动重建使用最近 Projection Checkpoint 加有限后续事件，不从第一场对局开始全量回放。

### 22.22 测试体系

测试分层：

1. 纯状态机单元测试：覆盖每条合法边和每类非法边。
2. 模型化与属性测试：生成重复、乱序、缺失、超时和并发事件，失败 seed 可复现。
3. 通道合同测试：验证 Voice、Overlay 和 Panel 的真实 Delivered 条件。
4. FakeMonotonicClock 集成测试：不使用真实长时间 Task.Delay。
5. 崩溃与故障注入：覆盖 Outbox、Receipt、WAL、通道和关闭边界。
6. 黄金回放与自定义对局：相同输入、版本和配置产生相同 Projection hash。

关键不变量：

- 不存在两个 Current revision。
- 不存在 Closed → Active。
- 不存在父 Closed 但子 Delivery 非终态。
- 不存在两个游戏语音同时 Active。
- 不存在普通语音跨回合播放。
- 不存在旧 adviceId 回执修改新 revision。
- 不存在相同 semanticDigest 重复语音。

黄金场景覆盖正常交付、Supersede、用户主动请求、回合和会话边界、TTS 与设备故障、Overlay 与 Panel 故障、断线恢复、D 盘降级和提示疲劳。

### 22.23 可观测性与隐私

结构化事件至少关联：

- runtimeEpoch
- sessionId
- sessionEpoch
- roundKey
- decisionWindowId
- ingressStateSnapshotId
- decisionSnapshotId
- knowledgeSnapshotId
- stateRevision
- adviceId
- adviceRevision
- deliveryId
- attemptId
- replaceKey
- resourceKey
- scopeKey
- priorityClass
- channel
- semanticDigest
- rulesetKey
- sourceInstanceId
- sourceSequence
- hostIngressSequence
- coordinatorSequence
- eventKind
- phaseBefore
- phaseAfter
- terminalResult
- reasonCode
- durationMs

默认不记录 Riot ID、玩家真实名、完整聊天、麦克风音频、全屏画面、连续键盘输入、第三方 Token、完整远程请求或无限期保留的完整语音文本。

诊断优先记录 templateId、semanticDigest、文本长度、预计语音时长、锁定槽位名称、状态和 reasonCode。

日志与指标使用内存计数器、有界 ring buffer、后台批量写入、聚合和容量上限。日志失败不能阻塞 Advice，且所有插件自有日志只写 D 盘。

### 22.24 验收门槛

正确性：

- illegal_transition_total = 0。
- orphan_delivery_total = 0。
- simultaneous_voice_active_total = 0。
- cross_round_voice_played_total = 0。
- Closed 后业务状态变化 = 0。
- 相同 semanticDigest 重复语音 = 0。

恢复：

- 干净关闭与异常退出恢复通过。
- 崩溃后不补播旧 Voice。
- 崩溃后不恢复旧 Overlay。
- 同一局恢复产生新 Advice。
- 数据库故障可进入 MemoryOnlyDegraded。
- 所有创建 Delivery 最终 Terminal。
- 所有等待、队列与关闭均有界。

长稳：

- 模拟 10,000 条历史 Advice、100 条并发非终态 Delivery、10 万条重复或乱序 Receipt 和连续三小时事件。
- 活跃索引不随历史线性变慢。
- Timer、Task、线程、文件句柄和内存不持续单调增长。
- 日志遵守容量上限。
- 关闭时间有界。

C 盘零写入是二进制门槛：

- TFT 插件数据库、TTS 临时音频、Overlay 缓存、日志、诊断、内容包和更新暂存均不得写 C 盘。
- 必须区分插件自身与 Windows、Riot、Overwolf、.NET 或 Codex 自身写入。
- D 盘不可用时进入 MemoryOnlyDegraded，不回退 C 盘。

实际自定义对局按以下测试顺序验证：

    Shadow
      →
    Panel-only
      →
    Overlay
      →
    Voice

这是测试顺序，不是版本规划。

### 22.25 完成定义与成本

AdviceCoordinator 只有在以下内容全部具备时才算完成：

- 父 Advice、Delivery 与 ReceiptEnvelope 合同版本化。
- close cause、terminal result 和 reasonCode 字典明确。
- 所有命令与回执分离。
- 所有 scope、replaceKey 和 resourceKey 规则明确。
- 每条合法和非法状态转移有测试。
- 重复、乱序、迟到和确定性回放通过。
- 干净关闭、异常恢复和 D 盘降级通过。
- P95 与 P99 性能门槛通过。
- 队列、等待和关闭有界。
- 用户软接管、P1 优先与跨回合清理通过。
- 默认日志脱敏、容量有界并只写 D 盘。

修正后的工程量：

- AdviceCoordinator 核心与基本测试：约 12～20 人日。
- 完整故障、回放、性能和长稳验收：约 4～7 人日。
- 合计：约 16～27 人日。

该估算不包含完整 GameVoiceScheduler、Overlay 窗口工程、分析算法、表达 Skill 内容制作和第三方 Provider，也不是版本排期承诺。

### 22.26 本节结论

锁定以下原则：

    完整语义合同
    +
    轻量本地执行
    +
    外部 I/O 全异步
    +
    三通道并行
    +
    真实终态回执
    +
    确定性 Supersede 与 scope
    +
    崩溃后不补播旧实时消息
    +
    日志异步、有界、脱敏且只写 D 盘

GameVoiceScheduler 的具体播放、抢占、语义边界、设备切换、延迟和用户控制策略已经在第 23 节逐项确认并获用户审阅；下一步进入 Overlay 工程细节讨论，而不是进入实现或版本规划。

## 23. GameVoiceScheduler 完整设计（已确认）

本节记录已经逐项获得用户确认并完成用户审阅的 GameVoiceScheduler 决策。它已覆盖队列、抢占、时序、故障、音频设备、用户控制与完整验收合同；本节仍不是实现计划或版本规划。

### 23.1 当前代码边界

当前 `SpeechService.QueueSpeakAsync()` 在启动 `ISpeechAudioPlayer.PlayAsync()` 后即可返回，因此 `SpeechService.SpeakAsync()` 完成不能作为音频自然播放完成的回执。

当前 `NAudioSpeechAudioPlayer.PlayAsync()` 会等待 `PlaybackStopped`，但取消令牌触发的 `speaker.Stop()` 与自然播放结束在没有异常时都会完成同一个任务，尚不能可靠区分自然结束、软停止、跳过、回合取消与设备异常。

因此，GameVoiceScheduler 不直接把现有 `SpeechService.SpeakAsync()` 作为交付边界，也不与 QQ 聊天共用语音队列或 TTS Provider。游戏教练边界为：

    AdviceCoordinator
    → GameVoiceScheduler
    → IGameCoachTtsProvider
    → CoachPlaybackAdapter

QQ 聊天保留自己的 `SpeechService`、队列和本地 TTS。QQ 与游戏插件不同时播放是由用户的启动 / 使用方式保证的外部运行前提，不是插件可检测或证明的扬声器层事实；因此插件不增加跨来源共享播放门、优先级映射、等待、取消或抢占协议。插件可保证的仅是两条链的队列、Provider、缓存、预算、状态和播放租约完全不互通。

CoachPlaybackAdapter 必须返回有类型的真实播放结果，不能用固定延迟或 `SpeakAsync()` 返回时间推测完成。

### 23.2 已确认选择：语义短句切片与有限预取

不采用整条长消息单音频作为游戏提示的默认形式，也不把流式 TTS 作为首要依赖。采用：

    专属表达 Skill 生成 VoiceUtterancePlan
    +
    按语义边界切成短片段
    +
    当前片播放时最多预取下一片
    +
    软接管在片段边界生效

表达 Skill 输出的不是任意长文本，而是结构化 `VoiceUtterancePlan`。每个 `VoiceSegment` 至少包含：

- segmentId。
- text。
- semanticBoundary。
- estimatedDurationMs。
- canEndAfterThisSegment。
- mustStayWithNext。
- priority。
- expiresAtMonotonic。
- semanticDigest。

普通游戏教练建议默认控制为 1～3 个片段。每片目标约 0.8～2.5 秒，总语音通常不超过约 6 秒。需要更长解释时优先进入 Panel 或 RichPath，不把长篇分析连续塞入游戏语音。

### 23.3 合成、播放与预取规则

- 第一片合成完成并通过有效期复检后即可开始播放，不等待整条 Advice 全部合成。
- 播放当前片段时最多预取紧随其后的一个片段。
- 不提前生成整条长语音，避免 Advice 失效后留下大量无效音频和远程 TTS 成本。
- 预取片仍绑定 adviceId、adviceRevision、deliveryId、attemptId、segmentId 与 scope；任一身份不匹配都不得播放。
- 每片实际播放前再次检查 Advice 是否仍为 Current、scope 是否有效、截止时间是否到达以及资源是否已经被更高优先级占用。
- `mustStayWithNext` 只用于不能被错误拆开的极短语义组合；不得用它把多个普通短句重新捆成不可打断的长语音。

### 23.4 已确认的软接管语义

用户主动请求到来时：

- 尚未开始播放的普通被动片段立即取消。
- 当前短片接近语义边界且内容仍有效时，允许它自然结束，然后停止剩余片段。
- 当前片已经过长、内容已失效或与高风险提示冲突时，允许短淡出后停止。
- P1 EmergencySafety 仍可立即接管普通语音资源。
- P2 UserInitiated 按 AdviceCoordinator 已确认的软接管策略接管 P3～P5。
- 已完成的片段不回滚；被取消的后续片段不得在新请求结束后自行恢复。

### 23.5 分片播放结果与父 Delivery

ICoachPlaybackAdapter 至少区分：

- CompletedNaturally。
- Interrupted。
- CancelledBeforeStart。
- DeviceLost。
- DecodeFailed。
- TimedOut。

具体映射到 AdviceCoordinator 的统一 `DeliveryTerminalResult` 和 `reasonCode`，不另建一套互相矛盾的父状态。

Voice Delivery 只有在所有必要片段都自然完成、没有播放异常且最后一个必要片段确实结束时，才可终止为 Delivered。已经自然说完一部分、随后因软接管停止时，Voice Delivery 终止为 Interrupted；父 Advice 再结合 Panel 与 Overlay 的结果计算 FullyDelivered、PartiallyDelivered 或 Undelivered。

### 23.6 D 盘缓存约束

生成的语音片段、索引与清理记录只能位于：

    D:\AlifeData\TFTCompanion\cache\voice

缓存必须有容量上限、TTL、引用计数或租约保护以及有界清理。D 盘不可写时转为内存或无缓存降级，不得回退到 C 盘。迟到、过期和 attemptId 不匹配的音频进入 D 盘有界清理，不得重新进入播放队列。

### 23.7 成本边界

语义切片合同、有限预取、有类型播放结果、软停止和对应确定性测试预计约 6～10 人日。该数字不包含完整队列仲裁、音频设备切换、watchdog、长稳故障注入、TTS Provider 改造和表达 Skill 内容制作，也不是版本排期承诺。

流式 TTS 可保留为未来可替换后端，但不进入当前基础合同。它不能改变 GameVoiceScheduler 的片段身份、终态回执、scope、取消或 D 盘约束。

### 23.8 已确认选择：独立游戏教练语音生产链

游戏教练必须拥有自己的 GameVoiceScheduler 和自己的低延迟 TTS Provider。QQ 聊天继续使用独立队列和本地部署 TTS。两者不得共享：

- 待合成或待播放业务队列。
- TTS Provider 实例。
- 合成并发配额。
- 请求超时。
- 熔断器与健康状态。
- 取消令牌。
- 音频缓存命名空间。
- 延迟指标。
- 播放 attempt 身份。

QQ 本地 TTS 不进入 GameVoiceScheduler 的任何状态、指标或资源预算。插件不检测 QQ 队列，也不等待、取消或抢占 QQ 播放。

### 23.9 IGameCoachTtsProvider 合同

GameVoiceScheduler 不硬编码具体第三方 TTS。它依赖独立的 `IGameCoachTtsProvider`，以便替换第三方服务或未来接入自建低延迟服务而不改变调度状态机。

`GameTtsRequest` 至少包含：

- attemptId。
- segmentId。
- text。
- voiceProfileId。
- speakingRate。
- emotionHint。
- deadlineMonotonic。
- outputFormat。
- cancellationToken。

成功结果 `TtsArtifact` 至少包含：

- artifactId。
- providerId。
- filePath 或受控音频流引用。
- format。
- durationMs。
- contentHash。
- generatedAtMonotonic。
- expiresAtMonotonic。
- generationLatencyMs。

Provider 返回成功只代表音频制品已经可用，不代表片段已经播放或 Voice Delivery 已经完成。

#### TTS 出站、凭据与隐私边界

独立 TTS Provider 默认 Disabled；只有用户显式配置并启用专属 Profile、endpoint 与凭据后才允许发起网络请求。出站白名单只允许：

- 经 ExpressionContractValidator 验证的短 VoiceSegment 文本。
- voiceProfileId、speakingRate、emotionHint、outputFormat 与 deadline 等受限枚举 / 数值。
- attemptId、segmentId 等不含玩家身份的协议关联标识。

明确禁止发送 Riot ID、账号、昵称、match ID、原始 GEP、GameSnapshot、棋盘 / 敌方 / 商店 / 牌库、用户问题、QQ 内容、文件路径、诊断载荷、Token、Cookie 或完整日志。Provider 凭据由专属 GameCoachCredentialStore 管理，不与 QQ、Alife 全局 LLM 或攻略来源共享；客户端使用已验证 endpoint 与默认系统 TLS 校验，不得静默转发到未配置 Provider。

TTS 诊断仅允许保存 providerId、文本长度桶、contentHash、延迟桶、状态和 reasonCode；不得保存 VoiceSegment 原文或音频字节。第 29 节网络审计必须包含“受限 VoiceSegment 可以出站、任何游戏或身份载荷均不得出站”的负向测试。

### 23.10 无跨来源播放仲裁

锁定以下支持前提与插件边界：

    用户的启动 / 使用方式保证 QQ 与游戏插件语音不同时播放
    →
    插件不检测或验证该物理播放事实
    →
    插件只保证两条语音链完全隔离
    →
    不设计共享 AudioOutputGate、QQ 抢占或跨来源优先级

GameVoiceScheduler 只管理游戏插件自己的语音。QQ 的播放状态不进入 AdviceCoordinator，也不要求插件检测 QQ 是否正在合成或播放。若外部使用方式违反“不同时播放”前提，插件不承诺扬声器层仲裁；用户必须通过外部运行安排解决，当前不得为了假设性并发增加组件。

### 23.11 插件内部 ReadyToPlay 与唯一播放

只有游戏 TTS 制品已经生成、仍在有效期内并通过身份复检后，片段才可以进入 `ReadyToPlay`。正在合成、排队等待 Provider 或重试中的任务不占用插件内部播放租约。

GameVoiceScheduler 内部同时最多只有一个 `CoachPlaybackLease` 为 Active，用于防止两个教练 Advice 或两个片段互相重叠。该租约不是跨 QQ 的全局资源，也不接入 QQ 队列。

发出停止命令不等于已经释放 `CoachPlaybackLease`。新教练音频必须等待旧播放器返回自然结束、已确认中断或已确认释放；不得使用固定 `Task.Delay` 猜测设备已经静音。

### 23.12 TTS 失败与回退边界

游戏教练的第三方 TTS 失败、超时或熔断时，默认不回退到 QQ 的高延迟本地 TTS。过期语音应终止为 Failed、TimedOut 或 Expired，Panel 与 Overlay 继续交付。

若未来需要语音冗余，应配置第二个满足游戏延迟预算的 `IGameCoachTtsProvider`。QQ 本地 TTS 不属于游戏插件的回退链。

### 23.13 本项成本边界

独立游戏教练 Provider 合同和插件内部 `CoachPlaybackLease` 预计增加约 1～2 人日。取消跨 QQ 的共享播放门、租约和抢占协议，撤销此前对应的约 2～4 人日估算。

该估算不包含第三方 Provider 适配器本身、鉴权、计费治理、设备切换和完整插件内部抢占矩阵。游戏教练的音频制品仍严格使用第 23.6 节的 D 盘路径和降级规则。

### 23.14 已确认选择：有界优先队列

GameVoiceScheduler 不采用无界 FIFO，也不采用只有一个 Pending 的默认极简队列。锁定：

    1 个 Active Voice Delivery
    +
    最多 4 个 Pending Voice Delivery
    +
    当前 Voice Delivery 最多预取下一语义片段

Active 不计入四个 Pending 名额。当前 Delivery 的下一语义片段属于该 Delivery 的内部预取，不另占 Pending Delivery 名额。

四个 Pending 是突发事件和故障抖动的硬安全边界，不是等待逐条播完的清单。每次准备下一条时都必须重新执行有效期、scope、Current revision、注意力预算和通道健康检查。

### 23.15 入队、替换与去重

- P0 HardInvalidation 不进入可播放队列，只执行取消、清理和关闭。
- 相同 replaceKey 的更高 revision 原子替换旧 Pending revision。
- 旧 revision 已经 Active 时，新 revision 按第 22.10 节的两阶段 Supersede 和第 23.4 节的软接管规则处理，不能出现两个 Active。
- 相同 semanticDigest 不创建第二次 Voice 播报；Panel 或 Overlay 可以刷新。
- 所有入队项必须已经携带 priority、replaceKey、scopeKey、notBefore、expiresAt、coordinatorSequence 和 preemptionMode；GameVoiceScheduler 不调用 LLM 补齐这些字段。
- 已经终态化的 deliveryId 或 attemptId 不得重新入队。

### 23.16 满队列淘汰与确定性顺序

队列未满时，新项通过身份和有效期检查后进入 Pending。队列已满时，将新项与当前最弱 Pending 项比较。

选择下一条播放的固定顺序为：

1. 删除已经过期、scope 失效、非 Current revision 或健康门控失败的项。
2. priority 更高者优先。
3. 同优先级时，expiresAtMonotonic 更早者优先。
4. 截止时间相同时，coordinatorSequence 更早者优先。

最弱项是上述顺序的反向末位。新项严格优于最弱项时，淘汰最弱项并进入队列；否则新项被拒绝。相同 replaceKey 的新 revision 替换规则先于容量比较。

被淘汰或拒绝的 Voice Delivery 必须真实终态化，并使用 Superseded、Expired、Rejected / QueueCapacity 或 Skipped / AttentionBudget 等明确结果与 reasonCode。其他通道不因此自动关闭。

不得使用等待时间提升 priority。P5 等待再久也不会自动成为 P4 或 P3；旧陪伴内容应过期，而不是变得紧急。

### 23.17 插件内部抢占与播放选择

插件内部沿用已确认的优先级：

- P1 EmergencySafety 可以请求当前普通片段快速停止。
- P2 UserInitiated 在当前语义片段边界软接管 P3～P5。
- P3 CriticalTactical 可以软接管 P4～P5，不接管 P1 或 P2。
- P4 OrdinaryCoach 只能接管 P5。
- P5 Companion 永不主动接管其他语音。

发出抢占命令后，被选中的下一条 Pending 不能立即开始播放。必须等待旧 CoachPlaybackLease 收到自然完成、已确认中断或已确认释放的真实回执。

P1～P5 全部只描述游戏插件内部 Advice。QQ 语音不进入该优先级比较。

### 23.18 防止连续播报与跨 scope 清理

- 普通决策窗口通常最多选择一条主动教练语音，其余有价值内容进入 Panel 或 Overlay。
- P5 在存在任何 P1～P4 Pending 时优先被淘汰。
- RoundBound 和 DecisionWindowBound 的普通 Pending 项在对应 scope 关闭时立即过期。
- 普通被动语音不得跨回合继续等待或播放。
- 被淘汰、过期或取消的内容在队列重新空闲后也不得恢复补播。
- 出队时如果结论已不再改变用户下一步行动，则取消 Voice，只保留仍有价值的视觉交付。
- 队列容量、淘汰次数、等待时间和过期原因只记录有界脱敏指标，不记录额外音频副本。

### 23.19 本项不变量、测试与成本

至少验证：

- 永远不超过一个 Active Voice Delivery。
- Pending Voice Delivery 永远不超过四个。
- 相同 replaceKey 不存在两个可播放 Current revision。
- 相同 semanticDigest 不重复播报。
- 高优先级入队不会被低优先级占满永久阻塞。
- 相同输入事件序列产生相同选择和淘汰结果。
- scope 关闭后不存在普通旧语音继续播放。
- 被淘汰项都有终态回执，不能静默消失。

该有界队列、合并、淘汰、出队复检和确定性测试预计约 3～5 人日。它只增加内存状态转移和小规模排序，不进入 TTS 或远程 I/O 关键路径，正常仲裁不应形成可感知延迟。

### 23.20 已确认选择：有界、优先级感知的混合准备

不为四个 Pending 全部提前调用 TTS，也不等到前一条语音结束后才开始所有合成。采用：

    Pending 主要保存结构化语义计划
    +
    最多一个 Pending Delivery 成为 PreparingCandidate
    +
    当前 Delivery 最多预取下一语义片段
    +
    正常 TTS 单并发
    +
    仅 P1 可按 Provider 能力使用一个紧急旁路

`PreparingCandidate` 是四个 Pending 之一的准备状态，不增加第五个 Pending 名额。

### 23.21 制品与准备预算

运行时允许：

- 一个正在播放的当前片段。
- 当前 Active Delivery 最多一个尚未播放的预取后续片段。
- 最多一个 Pending Delivery 的预备首片。

因此，除当前播放片段外，推测性保留的未播放音频制品最多两个。队列中的其他 Pending 只保存文本计划、身份、优先级、有效期与 scope，不调用 TTS。

若当前 Delivery 已没有后续片段，正常合成槽可以用于最高 Pending 的首片。若当前 Delivery 的后续片只是可选解释，而新的高优先级候选到达，不保证继续为旧解释消耗 TTS。

### 23.22 PreparingCandidate 选择与替换

每次队列、scope、Current revision 或通道健康变化后，按第 23.16 节的确定性顺序重算唯一 PreparingCandidate：

1. 先删除过期、失效或身份不匹配项。
2. priority 更高者优先。
3. 同优先级时 expiresAtMonotonic 更早者优先。
4. 截止时间相同时 coordinatorSequence 更早者优先。

新 P1 或 P2 到达时，可以取消较低优先级 PreparingCandidate。P1、P2 的首片通过门控后立即获得准备资格；P3 只有成为最高 Pending 且仍有足够决策时间时提前准备；P4 只在成为最高 Pending、当前播放接近结束且仍值得播报时准备；P5 默认不做跨 Delivery 推测性合成。

合成资源的机械优先顺序为：

1. P1 新候选。
2. P2 新候选。
3. 当前 Active Delivery 中语义上必须连续的下一片。
4. 当前最高 P3 Pending 的首片。
5. 当前 Active Delivery 的普通可选后续片。
6. P4。
7. P5。

该顺序不改变 Advice priority，只决定有限 TTS 准备资源先服务谁。

### 23.23 TTS 并发与 P1 紧急旁路

默认：

    normalSynthesisConcurrency = 1

低优先级合成被更高优先级替换时先发送取消。Provider 正确响应取消时，不创建第二个正常请求。

仅当以下条件全部成立时，P1 可以使用一个 `emergencyBypassConcurrency`：

- 新任务确为 P1。
- 低优先级请求已经收到取消命令。
- 旧请求尚未在取消确认宽限内退出。
- Provider 明确支持并发请求，且套餐、速率限制与熔断状态允许。
- 总物理合成并发不超过两个。

P2 不默认使用紧急旁路。Provider 只允许单并发时关闭该能力；P1 的 Overlay 或 Panel 仍立即交付，Voice 根据剩余有效期等待或放弃。

取消确认宽限和 Provider 超时的具体毫秒值留给后续时序与故障章节确认，不能在实现中散落硬编码。

### 23.24 合成完成后的身份复检与迟到制品

TTS 成功只产生 `TtsArtifact`，不直接触发播放。结果返回时至少复检：

- runtimeEpoch。
- adviceId。
- revision。
- deliveryId。
- attemptId。
- segmentId。
- semanticDigest。
- scopeKey。
- expiresAtMonotonic。
- 当前队列选择与 PreparingCandidate 身份。
- Voice 通道健康。

任一不匹配时，音频不得进入 ReadyToPlay。对应任务按真实原因终态化，制品进入 `D:\AlifeData\TFTCompanion\cache\voice` 的有界清理。

Provider 忽略取消而迟到返回时，旧 attemptId 永远不能复活、覆盖或重新入队。清理失败只影响缓存健康，不得阻塞 GameVoiceScheduler。

### 23.25 测试、指标与成本

至少验证：

- 同时最多一个 PreparingCandidate。
- 正常物理合成并发最多一个。
- 只有 P1 且满足全部门控时总并发才可短暂达到两个。
- 四个 Pending 不会形成四个推测性 TTS 请求。
- P2 可以取消低优先级准备，但不能开启 P1 紧急旁路。
- 迟到 attempt 不会进入 ReadyToPlay。
- 过期、被替换和被取消制品只在 D 盘有界清理。
- Provider 不响应取消时协调器循环仍不等待。

指标至少区分 generationLatency、queueToPreparingLatency、cancelAckLatency、speculativeArtifactDiscarded、emergencyBypassUsed、providerConcurrencyRejected 和 estimatedTtsWasteCost。不得记录未脱敏 TTS 密钥或额外复制音频内容。

混合准备、PreparingCandidate、单正常并发、可选 P1 旁路、迟到制品隔离与对应测试预计约 4～7 人日。该估算不包含具体 Provider 的鉴权、SDK、计费、音频格式适配和服务采购。

### 23.26 已确认选择：有句柄的 PlaybackSession

游戏教练不直接把现有 `Task PlayAsync(filePath, cancellationToken)` 的完成当作可信播放合同。采用：

    ICoachPlaybackAdapter.StartAsync(PlaybackRequest)
    → PlaybackSession

`PlaybackSession` 至少暴露：

- sessionId。
- runtimeEpoch。
- deliveryId。
- attemptId。
- segmentId。
- Started。
- Completion。
- RequestFadeAndStop。
- RequestImmediateStop。
- DisposeAsync。

`Started` 只完成一次并返回 `PlaybackStartedReceipt`；`Completion` 只完成一次并返回 `PlaybackTerminalReceipt`。停止命令和回执分离。

### 23.27 播放会话状态与 Started 定义

标准状态为：

    Created
    → Opening
    → Started
    → StopRequested
    → Releasing
    → Terminal

初始化失败、解码失败、设备不可用或开始前取消允许从 Created 或 Opening 直接进入 Terminal。Terminal 后不得重新 Started、改变终态或重新占用 CoachPlaybackLease。

不能在文件生成、设备初始化开始、任务进入线程池或仅调用 `speaker.Play()` 时报告 Started。`PlaybackStartedReceipt` 的最低条件为：

- 输出设备成功打开。
- 解码链与静音裁剪链初始化成功。
- `speaker.Play()` 成功返回。
- 第一个经过裁剪的有效音频缓冲已经提交到输出管线。

该定义不声称精确测到物理扬声器振膜时刻，但能证明真实输出管线已开始消费音频。

Started 回执至少包含 runtimeEpoch、sessionId、sessionEpoch、roundKey、adviceId、adviceRevision、deliveryId、attemptId、segmentId、deviceId、artifactId、semanticDigest、startedAtMonotonic 和 firstBufferAtMonotonic。

### 23.28 PlaybackTerminalReceipt

PlaybackSession 本地终止原因至少区分：

- CompletedNaturally。
- InterruptedByFade。
- InterruptedImmediately。
- CancelledBeforeStart。
- InitializationFailed。
- DecodeFailed。
- DeviceLost。
- TimedOut。
- ApplicationShutdown。
- ForcedClosedUnknown。

这些本地原因映射到 AdviceCoordinator 已有的 `DeliveryTerminalResult` 和 `reasonCode`，不新增互相竞争的父 Advice 状态。例如两种播放器 Interrupted 原因统一映射到 DeliveryTerminalResult.Interrupted，并保留细化 reasonCode；资源无法确认释放时可以映射为 TimedOut 或 Failed，并使父交付结果保留 ForcedClosedUnknown。

终态回执至少包含 runtimeEpoch、sessionId、sessionEpoch、roundKey、adviceId、adviceRevision、deliveryId、attemptId、segmentId、terminalAtMonotonic、audibleEndedAtMonotonic、resourceReleasedAtMonotonic、outputReleased、decoderReachedEndOfStream、stopIntent、exceptionCategory 和底层设备摘要。

### 23.29 自然 EOF 与停止意图仲裁

适配器必须记录：

- decoderReachedEndOfStream。
- stopIntent。
- stopIntentSequence。
- playbackStoppedSequence。
- backendException。

使用同一 PlaybackSession 内的单调递增事件序号确定先后：

1. 解码器先明确到达 EOF，且没有更早有效停止意图时，为 CompletedNaturally。
2. 物理停止意图先记录且尚未到 EOF 时，根据命令为 InterruptedByFade 或 InterruptedImmediately。
3. 初始化、解码或设备异常优先形成对应失败原因，并保留停止请求为 contributing reason。
4. EOF 与 Stop 的先后无法证明时为 ForcedClosedUnknown，绝不能伪装成 CompletedNaturally。

wall clock 只用于诊断显示，不用于决定竞态胜负。

### 23.30 资源释放与下一条开始门槛

声音停止和底层输出资源释放分别记录。`Completion` 在资源正常释放后完成；若超过有界关闭期限仍无法释放，则以 outputReleased=false 的 ForcedClosedUnknown 完成。

新的 CoachPlaybackLease 只有在旧 PlaybackTerminalReceipt 满足：

    outputReleased = true

时才可进入 Active。仅收到 Stop 命令、PlaybackStopped 回调或 audibleEnded 时间都不够。

若 outputReleased=false，Voice 通道进入 Degraded 或 Unavailable，不冒险开始下一条；Panel 与 Overlay 继续工作。

### 23.31 停止命令的幂等与身份隔离

同一 PlaybackSession 可能先后收到回合结束、Supersede、用户 Skip、应用退出和 watchdog 命令。锁定：

- 第一个有效停止请求决定 primary stopIntent。
- 后续请求只加入 contributing reasons。
- 底层 Stop、Fade 或 Dispose 不重复执行不安全操作。
- Completion 只能产生一个终态回执。
- 所有命令绑定 runtimeEpoch、sessionId、sessionEpoch、roundKey、adviceId、adviceRevision、deliveryId、attemptId 和 segmentId。
- 旧 runtimeEpoch、sessionEpoch 或 attempt 的停止命令与迟到回调不得影响新 PlaybackSession。

回执处理使用 receiptId、sourceSequence 与事件幂等表，重复回执不重复转移状态。

### 23.32 语义边界停止与播放器停止分离

P2 等软接管通常采用：

    标记 StopAfterCurrentSegment
    → 当前片自然到达 EOF
    → 不提交下一片

`StopAfterCurrentSegment` 是 GameVoiceScheduler 的 Delivery 级标记，不是 PlaybackSession 的物理停止命令，也不写入播放器 stopIntent。因此当前 Segment 是 CompletedNaturally，而整个 Voice Delivery 因未完成剩余必要片段终止为 Interrupted，reasonCode 为 InterruptedAtBoundary。

只有当前片过长、Advice 已危险失效或 P1 立即接管时，才调用 RequestFadeAndStop 或 RequestImmediateStop。这样不会把礼貌地停在语义边界误记成播放器故障。

### 23.33 有界关闭边界

PlaybackSession 关闭采用：

    请求停止
    → 等待 stopGrace
    → 请求底层释放
    → 等待 releaseGrace
    → ForcedClosedUnknown / Degraded

GameVoiceScheduler、AdviceCoordinator 和应用关闭流程都不得无界等待音频驱动。stopGrace、releaseGrace 与各类接管的具体毫秒值在下一项时序设计中统一确认，不得散落在播放器实现里。

### 23.34 测试、不变量与成本

使用可控 FakePlaybackBackend 验证正常 Started→EOF→Released、开始前取消、边界停止、淡出停止、立即停止、EOF/Stop 竞态、无异常 PlaybackStopped 但未到 EOF、设备丢失、解码失败、释放卡死、重复停止和旧 session 迟到回调。

必须满足：

- 一个 PlaybackSession 只有一个 Terminal。
- Terminal 后不会重新 Started。
- outputReleased=false 时不会开始下一条。
- Interrupted 永远不会映射为 Delivered。
- 旧 attempt 永远不能停止或关闭新 attempt。
- 相同输入事件顺序得到相同终态。

PlaybackSession 合同、NAudio 适配、停止意图仲裁、资源释放回执和确定性测试预计约 5～8 人日。默认采用进程内适配，不引入独立音频 Worker；若未来驱动卡死数据证明需要进程隔离，可保持本合同不变并替换后端。

### 23.35 已确认选择：真实终态驱动的自适应时序

不采用所有语音零间隔连续播放，也不采用统一固定等待。锁定：

    先收到旧 PlaybackTerminalReceipt
    +
    outputReleased = true
    +
    按语义关系与 priority 计算 deliberate gap
    +
    gap 到达时重新验证后再开始

安全交接等待与体验停顿必须分开。前者证明旧播放器已经释放；后者是在释放后有意保留的认知间隔。任何固定延迟都不能替代真实资源释放回执。

### 23.36 同一 Advice 的 pauseClass

专属表达 Skill 为语义边界输出 pauseClass，而不是任意毫秒值：

- None：初始目标 0～50ms，用于必须紧密连接的极短短语。
- Short：初始目标 80～120ms，用于同一句内短分句。
- Normal：初始目标 140～220ms，用于两个相关完整短句。
- Emphasis：初始目标 260～400ms，用于需要强调的风险结论。

普通片段默认使用 Short 或 Normal。Emphasis 必须由表达合同明确给出，播放器不得随机决定。

若制品末尾保留了可验证的可听停顿，可以从额外 gap 中扣除；不能假设 `SpeechSilenceTrimmer` 后仍存在尾部静音。计算以 audibleEndedAtMonotonic 为基准。

### 23.37 不同 Advice 的初始认知间隔

旧资源确认释放后，下一条不同 Advice 的初始额外停顿为：

- P1 EmergencySafety：0ms。
- P2 UserInitiated：80～150ms。
- P3 CriticalTactical：120～220ms。
- P4 OrdinaryCoach：300～500ms。
- P5 Companion：800～1200ms，并且只在资源仍空闲时播放。

这些是 `VoiceTimingProfile` 的初始校准范围，不是散落在实现中的常量。最终默认值通过自定义对局回放、不同语速与设备的主观听感测试确定。

### 23.38 可取消 gap 与重新仲裁

事件流为：

    旧 Completion
    → 验证 outputReleased
    → 选择当前最高 Pending
    → 计算 nextNotBefore
    → gap 期间继续处理事件
    → nextNotBefore 到达时重新验证
    → 启动新 PlaybackSession

gap 期间：

- 新 P1 到达时取消剩余 gap，并立即重新仲裁。
- 新 P2 或 P3 到达时重新选择候选并重算 nextNotBefore。
- 原候选被 Supersede 时不得播放旧 revision。
- scope 关闭时对应候选立即过期。
- Voice 通道 Degraded 时取消启动。
- 候选尚未 ReadyToPlay 时可以等待，但不得越过 expiresAt。
- P5 gap 期间出现任意 P1～P4 时，P5 不得抢先开始。

体验 gap 不是队列锁，也不保证原候选一定播放。

### 23.39 P1 接管时序

P1 到达时：

    取消尚未播放的低优先级片段
    → 对当前片请求短淡出
    → 淡出失败时请求立即停止
    → 等待真实 outputReleased
    → P1 不添加体验 gap

初始性能目标：

- 淡出目标 80～120ms。
- P1 抢占命令到可听声音结束：P95 约不超过 200ms。
- 抢占命令到资源确认释放：P95 约不超过 300ms。
- 资源释放后到 P1 启动调用：P95 不超过 20ms。

TTS 生成与底层设备首缓冲耗时单独计量。如果资源无法在后续 watchdog 门槛内确认释放，Voice 降级，P1 由 Panel 或 Overlay 继续交付，不能重叠播放。

### 23.40 P2/P3 与 P4 接管

P2 或 P3 到达时先标记 StopAfterCurrentSegment，并估算当前片剩余可听时间：

- 剩余时间不超过 800ms：允许当前片自然结束。
- 剩余时间超过 800ms：请求 80～150ms 淡出。
- 无法可靠估算：按超过软等待上限处理。
- 当前片自然结束后不再提交后续片段。
- 新 Advice 在等待期间过期时放弃 Voice，不迟到播放。

初始：

    softBoundaryMaxWait = 800ms

P4 接管 P5 时不强制淡出。P5 当前片自然结束后不再提交后续片；P4 若已过期则直接放弃语音。

### 23.41 mustStayWithNext 与假思考限制

mustStayWithNext 不能无限锁住播放器：

- P1 无条件忽略并立即接管。
- P2/P3 仅在组合剩余时间仍不超过 800ms 时尊重，否则淡出。
- P4 可以尊重当前语义组合，但自身可能因此过期。
- P5 不接管其他内容。

GameVoiceScheduler 不加入随机“像人类思考”的停顿。真正的分析窗口属于 AdviceCoordinator 和战略分析模块。表达 Skill 可以选择 pauseClass，但不能生成任意毫秒数；LLM 不能控制安全交接期限。

### 23.42 VoiceTimingProfile 与定时实现

所有参数集中到版本化 `VoiceTimingProfile`，至少包含：

- segmentPauseNone。
- segmentPauseShort。
- segmentPauseNormal。
- segmentPauseEmphasis。
- interAdviceGapP1～P5。
- softBoundaryMaxWait。
- fadeDuration。
- timingProfileVersion。

配置属于插件数据，保存到 `D:\AlifeData\TFTCompanion`，不得回退 C 盘。实现不得在多个类中散落 `Task.Delay` 常量。

定时使用统一 monotonic deadline heap 或 timer wheel：

- 不为每个 gap 建立长期线程。
- 不使用 wall clock 决定先后。
- 系统时间调整不改变时序。
- FakeMonotonicClock 可以精确推进测试。
- gap 取消、候选替换和优先级重算只执行内存状态转移。

### 23.43 测试、指标与成本

至少验证同 Advice 四种 pauseClass、P1 零额外 gap、P2/P3 800ms 边界、P4 不强停 P5、gap 期间 Supersede、scope 关闭、候选未 Ready、通道降级和系统 wall clock 跳变。

指标至少包括 audibleEndToRelease、releaseToNextStartCall、terminalToNextStarted、softBoundaryWait、fadeDurationActual、gapCancelled、candidateChangedDuringGap 和 adviceExpiredDuringGap。TTS generationLatency 单独统计。

VoiceTimingProfile、自适应 nextNotBefore、接管矩阵、可取消 gap 与 FakeMonotonicClock 测试预计约 3～5 人日。该设计增加的是有意的体验间隔，不是计算延迟；P1 不添加体验 gap。

### 23.44 已确认选择：截止时间感知的 Provider 路由

游戏教练可以配置：

    PrimaryGameTtsProvider
    +
    OptionalSecondaryGameTtsProvider

QQ 本地 TTS 永不进入游戏回退链。不是失败就循环重试，而是只有错误可恢复、仍有有效时间、费用允许且尝试次数有界时才再尝试。

### 23.45 latestUsefulVoiceStart 与总尝试预算

每个 VoiceSegment 共享一个总有效截止时间：

    latestUsefulVoiceStart
    = min(
        Advice.expiresAt,
        scopeEnd,
        decisionWindowEnd - playbackSafetyMargin
      )

每次物理 Provider attempt 前检查：

    nowMonotonic
    + providerEstimatedP95Generation
    + playbackStartBudget
    < latestUsefulVoiceStart

不成立时不再请求 Primary、Secondary 或重试，Voice 终止为 Expired 或 TimedOut，Panel 与 Overlay 继续。

所有 Provider 共享该总预算，不能给每个 Provider 重新分配一整套超时。latestUsefulVoiceStart 到达后返回的音频一律视为迟到制品。

### 23.46 按优先级的恢复矩阵

- P1：可以使用健康 Secondary；严格门控下允许一次受控并行 hedge。
- P2：Primary 失败后最多顺序尝试一次 Secondary。
- P3：只有剩余有效时间足够时，最多顺序尝试一次 Secondary。
- P4：默认只尝试 Primary 一次，失败后转视觉交付。
- P5：只在 Primary 健康时尝试一次，不重试、不 fallback。

P2/P3 默认不并行 hedge。任何 fallback 前都重新检查 revision、scope、deadline、费用预算、Provider 健康和队列候选身份。

### 23.47 P1 受控 hedge

仅当以下条件全部成立时可以启动 Secondary hedge：

- Primary 已超过 hedgeThreshold 且尚无可用音频。
- P1 仍有足够有效时间。
- Secondary 为 Healthy 或允许探测的 HalfOpen。
- Provider 套餐和费用预算允许。
- 该 segment 尚未使用 hedge 名额。
- 全局物理 TTS 并发仍不超过两个。

第一个返回且通过完整身份、格式与有效期验证的制品成为 Winner。Loser 收到取消；迟到 Loser 制品只进入 D 盘有界清理。

第 23.23 节的 P1 emergency bypass 与本节 hedge 共享同一个第二并发名额，不能叠加成三个物理请求。若一个取消中的低优先级 attempt 和 P1 Primary 已占满两个名额，则不能同时启动 Secondary hedge。

### 23.48 错误分类与默认处理

- AuthenticationInvalid：Provider 立即 Open，不重试。
- QuotaExhausted 或 PaymentRequired：Provider 立即 Open，不重试。
- InvalidRequest 或 UnsupportedVoice：配置错误，不重试。
- ContentRejected：不偷偷改写文本重试，转视觉交付。
- RateLimited：读取 Retry-After；只有仍在有效期内才考虑 Secondary。
- NetworkTransient：剩余预算允许时最多使用一次恢复机会。
- Provider5xx：剩余预算允许时最多使用一次恢复机会。
- Timeout：取消旧 attempt，并按 deadline 决定是否使用 Secondary。
- EmptyArtifact 或 CorruptArtifact：视为 Provider 失败，有时间才 fallback。
- CancellationRequested：不重试。
- AdviceExpired：不重试、不 fallback。
- Unknown：默认不盲目重试，只记录脱敏诊断。

错误类别由 Provider Adapter 规范化，GameVoiceScheduler 不解析供应商私有错误字符串来决定业务状态。

### 23.49 同 Provider 重试限制

默认不做通用“原 Provider 再试一次”。只有以下条件全部成立时，同一 Provider 才可安全重试一次：

- Provider 明确将错误标记为 retryable。
- idempotency key 能避免重复计费或重复生成。
- Provider 未进入 Degraded 或 Open。
- Retry-After 不超过剩余有效时间。
- 仍有完整生成与播放预算。
- 该 segment 尚未使用同 Provider 重试名额。

否则使用健康 Secondary 或直接视觉降级。不得对认证、余额、参数、内容拒绝和 AdviceExpired 做重试。

### 23.50 ProviderAttempt 身份与 Winner 仲裁

每个物理请求具有独立 providerAttemptId，至少绑定：

- runtimeEpoch。
- adviceId。
- revision。
- deliveryId。
- attemptId。
- segmentId。
- providerId。
- providerAttemptNumber。

每个返回必须验证当前 revision、当前 attempt、Provider attempt 是否仍是 Winner candidate、scope 与 deadline、是否已有 Winner、artifact 完整性和音频格式。

Winner 指针只能原子设置一次。重复、迟到或败选返回不得覆盖 Winner，也不得重新进入 ReadyToPlay。

### 23.51 Provider 熔断器

每个游戏 TTS Provider 独立维护：

    Closed
    → Degraded
    → Open
    → HalfOpen
    → Closed

初始可校准规则：

- 连续三次可归因 Provider 的失败时进入 Open。
- 最近十次请求失败率超过 50% 时进入 Open。
- AuthenticationInvalid、PaymentRequired 或明确 QuotaExhausted 时立即 Open。
- 初始 Open 冷却约 30 秒。
- HalfOpen 只允许一个探测请求。
- 探测再失败时指数增加冷却，上限约 5 分钟。
- 用户修改凭据或 Provider 配置后创建新的配置代次，再进行受控探测。

阈值集中配置并根据真实数据调整，不硬编码在 Provider Adapter。

Primary Open 时，P1～P3 可以考虑健康 Secondary；P4 只有 Secondary 健康且费用允许时才尝试；P5 默认静音并保留视觉内容。

### 23.52 TtsCostBudget 与降级顺序

`TtsCostBudget` 至少控制每局和每日估算费用或字符数、推测性浪费上限、P1 hedge 次数、Secondary fallback 次数、P4/P5 非关键预算以及 P1/P2 保留预算。

接近预算上限时按顺序降级：

1. 关闭 P5 Voice。
2. 减少 P4 Voice。
3. 禁止 P3 fallback。
4. 禁止 P1 hedge。
5. 保留仍有预算的 P1/P2 单次 Primary。
6. 完全耗尽后全部转视觉交付。

费用预算、估算和熔断状态只写 D 盘。插件不得为了保证每句话都发声而突破用户费用上限。

### 23.53 失败体验、测试与成本

所有 Provider 失败或时间不足时，Voice Delivery 终止为 Failed、TimedOut 或 Expired；Panel 与 Overlay 继续。不能再用语音播报“语音服务失败”，也不能在 Provider 恢复后补播故障期间的旧建议。

面板可以使用低打扰状态图标显示 Voice Degraded，但不得弹出阻挡游戏的错误窗口。

至少测试错误分类、deadline 不足、Primary/Secondary 顺序 fallback、P1 hedge Winner 竞态、全局并发上限、Loser 迟到、重复响应、熔断开闭、HalfOpen 单探测、预算降级和恢复后不补播。

指标至少包括 providerGenerationLatency、providerErrorCategory、fallbackAttempted、fallbackSucceeded、hedgeStarted、hedgeWinner、loserArtifactDiscarded、circuitState、estimatedRequestCost、estimatedWastedCost、expiredBeforeFallback 和 visualOnlyDueToTtsFailure。

deadline-aware 路由、错误分类、fallback/hedge、熔断、Winner 仲裁、费用预算与对应测试预计约 6～10 人日。该估算不包含具体 Provider SDK、API 鉴权、采购和套餐费用。

### 23.54 已确认选择：PhaseDeadlineRegistry

GameVoiceScheduler 不使用单一 Voice 总超时，也不让各异步方法散落 `Task.Delay`。它复用 AdviceCoordinator 已确认的 monotonic deadline heap 或 timer wheel，注册语音阶段期限：

    GameVoiceScheduler / TTS Adapter / PlaybackSession
    → PhaseDeadlineRegistry
    → 统一 monotonic deadline heap
    → DeadlineReached 事件
    → 单写者处理

这是轻量阶段期限注册表，不是外部消息队列或工作流引擎。

### 23.55 PhaseDeadline 身份与 generation

每条 PhaseDeadline 至少包含：

- watchdogId。
- runtimeEpoch。
- adviceId。
- revision。
- deliveryId。
- attemptId。
- providerAttemptId。
- playbackSessionId。
- segmentId。
- phase。
- expectedPhase。
- deadlineGeneration。
- deadlineAtMonotonic。
- graceDeadlineAtMonotonic。
- firstAction。
- forcedTerminalReason。

同一阶段重算期限时递增 deadlineGeneration。旧 heap 节点即使稍后到期，只要 generation、expectedPhase 或身份不匹配，就作为迟到事件丢弃。不能只按 deliveryId 判断，因为同一 Delivery 可以已有新的 Provider attempt 或 PlaybackSession。

### 23.56 两阶段 watchdog

所有可能卡住的外部阶段遵循：

    DeadlineReached
    → 验证 expectedPhase 与 generation
    → 发出取消 / 健康探测 / 停止 / 释放命令
    → 进入 WatchdogGrace
    → graceDeadlineReached
    → ForcedTerminal

watchdog 不能伪造 CompletedNaturally、outputReleased=true 或 Delivered；发出 Stop 命令不等于收到停止回执。它不等待外部 I/O，不无限重试，也不成为第二个业务调度器。

### 23.57 初始阶段表

以下是集中配置的初始可校准值：

| 阶段 | 初始期限 | DeadlineReached 动作 | Grace 后结果 |
|---|---:|---|---|
| Pending / Preparing 等待 | expiresAt 或 latestUsefulVoiceStart | 取消准备 | Expired |
| TTS Provider attempt | Provider hardTimeout 与 latestUsefulVoiceStart 较早者 | 取消请求、记录超时、交给路由评估 fallback | TimedOut |
| TTS CancelAck | 约 150ms | P1 可评估 emergency bypass；旧 attempt 隔离 | Quarantined |
| Playback Opening → first buffer | 约 750ms | 请求停止并释放 | InitializationFailed / TimedOut |
| SoftBoundaryWait | 已确认 800ms | 请求 FadeAndStop | 进入 FadeStop |
| FadeAndStop | fadeDuration + 约 200ms，上限约 350ms | 升级 ImmediateStop | 继续后续释放流程 |
| ImmediateStop | 约 250ms | 请求底层 Dispose / Release | TimedOut |
| ResourceRelease | 约 500ms | 隔离播放器实例、通道降级 | ForcedClosedUnknown |
| ApplicationShutdown voice close | 总预算约 1500ms | 停止新任务、取消并释放 | 本地强制闭环 |

性能目标和 watchdog 期限不同。例如 P1 资源释放的 P95 目标约 300ms，而 ResourceRelease watchdog 初始期限约 500ms。

### 23.58 动态 TTS deadline

TTS deadline 不能统一写死为两秒。每个 attempt 使用：

    ttsAttemptDeadline
    = min(
        now + providerProfile.hardTimeout,
        latestUsefulVoiceStart - playbackStartBudget
      )

Provider 的滚动 P95/P99 可用于估算是否值得尝试，但不能越过 Advice 有效期。若剩余预算小于最低有用生成时间，不启动 attempt，直接转 Expired 或 TimedOut 与视觉交付。

### 23.59 忽略取消的 Provider

TTS CancelAck 超时不要求协调器继续等待：

    旧 providerAttempt
    → Quarantined
    → 迟到返回由 providerAttemptId 拒绝
    → D 盘有界清理

P1 是否使用第二并发仍受第 23.23、23.47 和 TtsCostBudget 的共同限制。watchdog 只上报阶段超时；是否 fallback、hedge 或视觉降级仍由 Provider 路由与 AdviceCoordinator 决定。

### 23.60 TTS 与 Playback watchdog 的差异

TTS 卡住时没有声音正在输出，因此旧 attempt 本地终态化后可以继续评估其他候选。

Playback ResourceRelease 卡住时可能仍占用音频输出。因此：

    ResourceRelease watchdog
    → ForcedClosedUnknown
    → Voice channel = Degraded / Unavailable
    → 不启动下一条语音

本地状态机已强制闭环不代表扬声器已经安全可用。

### 23.61 deadline 失效、休眠与恢复

正常完成时不需要从 heap 随机删除所有节点；更新状态和 deadlineGeneration 即可。旧节点弹出后因身份、generation 或 expectedPhase 不匹配而丢弃。

Windows 休眠后多个 deadline 可能同时到期。恢复时：

1. 更新 monotonic 时钟基线。
2. 批量取出已到期节点。
3. 按 P0、P1、P2、P3、P4、P5 与 coordinatorSequence 确定性处理。
4. 旧回合、旧 scope、旧 Session 先终态化。
5. 不补播休眠期间错过的实时语音。
6. 重新验证设备和 Provider 健康。
7. 只允许新 Snapshot 产生新 Voice Advice。

### 23.62 诊断与测试

记录 watchdogPhase、deadlineGeneration、deadlineOverrunMs、firstActionIssued、graceExpired、forcedTerminalReason、lateCompletionIgnored、channelDegraded、recoveredAfterProbe 和 sleepResumeExpiredCount。日志脱敏、有界且只写 D 盘。

使用 FakeMonotonicClock 验证 deadline 前不触发、deadline 恰好只触发一次 first action、grace 前不强制终态、grace 后只强制一次、正常完成使旧 deadline 失效、generation 更新使旧节点无效、旧 attempt timer 不影响新 attempt、TTS 卡住仍可继续其他候选、Playback release 卡住不开始新音频、休眠恢复批量顺序确定，以及 watchdog 不伪造 Delivered。

### 23.63 本项成本边界

PhaseDeadlineRegistry 接入、分阶段期限、generation/expectedPhase 防迟到、两阶段 grace、休眠恢复、指标和 FakeMonotonicClock 故障注入预计约 4～7 人日。它复用既有 deadline heap，不需要外部队列、数据库轮询或每项独立后台线程，正常运行成本很低。

### 23.64 已确认选择：设备安全边界恢复

新增：

    AudioDevicePolicy
    ├─ FollowSystemDefaultAtSafeBoundary（默认）
    └─ PinnedDeviceId（高级选项）

默认跟随 Windows 系统默认设备，但不在已经 Started 的片段中途搬运或从头重播音频。QQ 语音不进入该策略。

### 23.65 默认设备变化

旧设备仍可用时：

    当前片继续在旧设备自然播放
    → 当前 PlaybackSession 正常终态
    → 应用新的默认输出设备
    → 下一条 PlaybackSession 使用新设备

不在中途切换设备，不从头重播当前片。

### 23.66 开始前设备丢失的一次重试

若制品已经 ReadyToPlay，但对应 PlaybackSession 尚未 Started：

    旧 PlaybackSession
    → CancelledBeforeStart / DeviceLost
    → 新设备可用且同一 artifact 仍有效
    → 创建一次新的 PlaybackSession

该重试只允许一次，不重新请求 TTS。新 session 使用新的 playbackSessionId 与 playbackAttemptNumber，并再次复检 runtimeEpoch、deliveryId、attemptId、segmentId、scope 和 expiresAt。第二次失败终态化为 DeviceLost；Advice 已过期时直接转视觉交付。

### 23.67 Started 后设备丢失

设备在 PlaybackSession 已 Started 后丢失时：

    当前 PlaybackSession
    → DeviceLost / ForcedClosedUnknown
    → 当前片不自动重播
    → 当前 Voice Delivery = Interrupted 或 Failed
    → Panel / Overlay 继续

系统无法知道用户已经听到多少，因此不从头补播当前片，也不补播剩余旧 Advice。

### 23.68 恢复后的选择

设备恢复不意味着恢复旧队列：

    Device recovered
    → Voice channel = Recovering
    → 重新解析设备并验证健康
    → 仅选择此刻仍有效、仍为 Current 的最新 Pending
    → 创建新的 PlaybackSession

休眠、蓝牙抖动或驱动重启期间错过的建议不补播。P1/P2/P3 只有仍在 latestUsefulVoiceStart 内时才可能重新获得 Voice；Started 后中断的片段永不自动重播。

### 23.69 设备状态与身份隔离

设备状态为：

    Healthy
    → Transitioning
    → Unavailable
    → Recovering
    → Healthy

每个 PlaybackSession 绑定 desiredDevicePolicy、resolvedDeviceId、deviceGeneration、playbackSessionId 和 attemptId。默认设备或指定设备变化时递增 deviceGeneration。旧设备的迟到回调身份不匹配时只记录脱敏诊断，不能关闭或污染新设备 session。

DeviceLost 表示底层明确报告设备故障；ForcedClosedUnknown 表示无法证明资源已释放。两者都不得直接启动下一条语音。

### 23.70 被动恢复、watchdog 与成本

设备策略使用 Windows 设备变更通知、默认端点变更通知和播放失败的规范化设备错误。恢复采用被动监听加懒启动：不播放测试音、不后台频繁打开设备；只有存在仍有效语音候选时才尝试打开当前设备。

设备打开受 Playback Opening deadline 约束。资源释放超时进入 Unavailable；Recovering 不得绕过 outputReleased=true 的安全门槛。

至少测试空闲设备变化、播放中旧设备仍可用、开始前丢失的一次重试、连续两次失败、Started 后断开、迟到 PlaybackStopped、蓝牙抖动、驱动重启、休眠恢复、固定设备移除、deviceGeneration 隔离和恢复后不补播。

设备策略、设备事件适配、generation 隔离、一次开始前重试、状态显示和故障注入测试预计约 4～7 人日。设备状态和诊断只写 D 盘，不建立 C 盘缓存。

### 23.71 已确认选择：统一用户控制入口

采用：

    Overlay / Side Panel / 可选本地热键
    → VoiceControlCommand
    → GameVoiceScheduler 单写者
    → VoicePreference + EphemeralVoiceControlState
    → Queue / TTS / PlaybackSession 的有界动作

UI 和热键只能提交命令，不得直接 Stop 播放器、清空队列或修改 Delivery 状态。

### 23.72 D 盘持久偏好

游戏教练不默认使用 Alife 通用 `ConfigurationSystem` 保存私有设置，因为它经 `StorageSystem` 写入 `AlifePath.StorageFolderPath`，不能自动证明满足插件不占 C 盘的约束。

使用独立 `TftCompanionSettingsStore`，根路径为：

    D:\AlifeData\TFTCompanion\settings\voice-preference.json

`VoicePreference` 至少包含：

- voiceMode。
- masterVolume。
- audioDevicePolicy。
- companionIntensity。
- hotkeyBindings。
- settingsVersion。

设置原子写入、版本化迁移并使用 D 盘短事务。不得写入 API Key、完整语音文本或额外音频副本。

### 23.73 VoiceMode 与陪伴强度

VoiceMode 为：

| 模式 | 允许自动 Voice |
|---|---|
| Off | 不自动播报；仅用户显式 RequestOneTimeReadAloud 时允许一次 P2 |
| Critical | P1 与用户明确请求的 P2 |
| Coach | P1～P4，不播 P5 |
| Companion | P1～P5 |

初始默认：

    voiceMode = Coach
    companionIntensity = Low

companionIntensity 为 Off、Low、Standard、Warm，只影响 P5 的准入、频率和长度，不改变 P1～P4 的战术优先级。

降低 VoiceMode 时，尚未开始且不再允许的自动语音立即取消；已开始的自动语音按第 23.40 节的边界或淡出规则结束，不得继续进入后续片段。

### 23.74 临时控制状态

`EphemeralVoiceControlState` 不跨应用重启持久化，至少包含：

- sessionMuteUntil。
- roundMuteUntil。
- skipSuppressions。
- cancelledDeliveryIds。
- currentControlGeneration。
- lastUserControlAtMonotonic。

支持：

- NoTemporaryMute。
- MuteUntilRoundEnd。
- MuteUntilGameEnd。
- MuteUntilManualResume。

临时静音不会在应用崩溃或重启后恢复旧 Voice，也不会补播旧 Advice；持久 VoiceMode 仍有效。skip suppression 只覆盖当前回合或当前局。

### 23.75 控制命令与回执

至少支持：

- SetVoiceMode。
- SetMasterVolume。
- SetCompanionIntensity。
- MuteUntilRoundEnd。
- MuteUntilGameEnd。
- MuteUntilManualResume。
- ResumeVoice。
- SkipCurrentAdvice。
- CancelAutomaticQueue。
- RequestOneTimeReadAloud。

每个 VoiceControlCommand 至少绑定 controlId、source（Overlay / Panel / Hotkey）、requestedAtMonotonic、controlGeneration、scope、targetDeliveryId 或 targetReplaceKey 与版本化 reasonCode。

reasonCode、source、scope 与命令种类均为白名单枚举；VoiceControlCommand 不接受自由文本 reason、用户问题、游戏状态或任意附加字段。Replay、诊断和持久化只能保存脱敏命令投影：命令种类、稳定 ID、枚举 reasonCode、scope 类别与终态，不保存用户文字。

GameVoiceScheduler 返回 Applied、NoOpAlreadySatisfied、RejectedInvalidScope、DeferredUntilBoundary 或 FailedInternal。重复 controlId 只能生效一次。

### 23.76 Skip、Cancel 与 Mute 语义

`SkipCurrentAdvice` 表示用户不想继续听当前 Advice：

    当前片 → 80～150ms 淡出或立即结束
    当前 Delivery 的后续片 → Cancelled
    相同 replaceKey + revision + semanticDigest → skip suppression

当前 Advice 的 Panel 与 Overlay 保留；同一 revision 不会因队列重算再次播报。新 revision 且语义实质变化后可重新取得 Voice。用户可以 Skip P1，用户拥有最终音频控制权。

`CancelAutomaticQueue` 表示本 DecisionWindow 或 Round 不再需要主动播报：取消 P3～P5 Pending，不取消用户主动 P2，也不取消未来尚未产生的 P1。当前片默认结束到语义边界；需要立即停止时使用 SkipCurrentAdvice。

`MuteUntil...` 是用户最高优先级的自动音频策略：

    当前自动 Voice → 淡出停止
    Pending / Preparing 自动 Voice → Cancelled
    新自动 TTS → 不启动
    迟到 Provider 结果 → 隔离并清理
    Panel / Overlay → 继续

Mute 覆盖包括 P1 在内的全部自动 Voice。用户主动 RequestOneTimeReadAloud 可在 Off 或临时 Mute 状态下放行一条一次性 P2，但不会解除整体 Mute。

### 23.77 音量、入口与热键边界

masterVolume 独立于 Mute，范围为 0～100。对未开始语音立即生效；对 Active 音频在约 100ms 内平滑过渡。音量变化不重新合成、不改变队列、不语音确认。

设为 0 不隐式改变 Mute 状态，UI 应清晰显示音量与自动 Voice 策略的区别。

入口可以是 Overlay 小型静音/跳过按钮、侧边面板详细设置和可选可配置本地热键。热键仅改变插件内部语音状态，不模拟游戏按键、不控制游戏、不注入游戏进程。默认不强占键位，首次启用由用户绑定或保持未绑定。

静态 UI 文案走本地化资源；游戏内自然语言仍由专属表达 Skill 决定，不硬编码在控制逻辑中。

### 23.78 控制与回执的状态边界

发出控制命令不等于已停止播放：

    SkipCurrentAdvice
    → RequestFadeAndStop
    → PlaybackTerminalReceipt
    → Voice Delivery = UserSkipped

Mute、Skip、Cancel 与 Supersede、watchdog 共用第 22 节的关闭原因仲裁和 receipt 幂等规则。控制命令只经 GameVoiceScheduler 单写者修改状态；Panel/Overlay 不因 Voice 被取消而自动关闭。

### 23.79 用户控制不变量与测试

至少验证：

- UI 不直接修改 PlaybackSession。
- 同一 controlId 只生效一次。
- Mute 后不启动新的自动 TTS。
- Skip 后相同 revision 与 semanticDigest 不重播。
- CancelAutomaticQueue 不取消 Panel 或 Overlay。
- 用户可 Skip P1。
- RequestOneTimeReadAloud 不解除整体 Mute。
- 应用重启不恢复旧 Voice 或补播旧 Advice。
- 所有设置和控制状态不回退 C 盘。

测试覆盖控制命令与 Supersede/watchdog 的并发、Active/Preparing/Pending 三种状态下的 Mute、跨回合自动恢复、热键重复、设置版本迁移、D 盘不可写降级、音量平滑和设备切换期间控制命令。

### 23.80 本项成本与第 23 节完成定义

TftCompanionSettingsStore、持久/临时状态分离、控制命令和回执、Skip/Mute/Cancel 联动、音量平滑、可配置热键与测试预计约 4～7 人日。不包括 Overlay 视觉工程细节。

GameVoiceScheduler 在第 23 节的完成定义为：语义切片、队列、准备、Provider、PlaybackSession、时序、watchdog、设备恢复、用户控制、D 盘边界、真实回执和确定性测试合同均已明确并已获用户审阅；下一步是继续 Overlay、IPC、隐私和全系统验证设计，而不是功能实现或版本规划。

## 24. Overlay 动态定位、缩放与混合式呈现设计（已确认）

本节记录透明 Overlay 在 PC《云顶之弈》中的定位、缩放恢复、置信度门控、交互边界与验收合同。它落实“陪伴与教练、只读、不过度干预”的产品定位：精确格位只在可靠时显示；不可靠时宁可收回棋盘内标注，也不得把玩家引向错误格子。

### 24.1 设计结论与产品承诺

采用：

    语义棋盘坐标
    +
    动态 ViewportTransformTracker
    +
    viewGeneration 身份隔离
    +
    自动几何锚点重估
    +
    人工校准兜底
    +
    High / Medium / Low 置信度门控
    +
    棋盘内层 / Edge Dock / Side Panel 三层降级

不采用：

- 一次四点校准后永久沿用像素坐标。
- 仅根据游戏窗口矩形、桌面分辨率或 DPI 推断棋盘位置。
- 在低置信度时继续显示看似精确的落格箭头。
- 为定位而持续全屏 60 FPS OCR、每帧识别全部弈子或保存连续视频。
- 自行注入游戏、读内存、模拟鼠标、键盘或点击。

产品只承诺：

    在受支持的窗口化或无边框窗口、常规显示器 DPI 与可见棋盘场景中，
    自动重新贴合当前棋盘；
    无法可靠贴合时，立即隐藏棋盘内精确标注，
    保留 Dock 与 Panel 的文字化建议和校准入口。

产品不承诺：

    任意游戏版本、任意视觉皮肤、任意第三方拉伸工具、
    独占全屏、遮挡、特效、系统放大镜或异常渲染环境下，
    无条件、永久、像素级贴合棋盘。

“发现不匹配后不误导”优先于“视觉层永不消失”。

### 24.2 窗口贴合与棋盘贴合必须分离

存在两个不同的问题：

1. Overlay 是否位于游戏的当前可见表面之上。
2. Overlay 内的一个语义棋盘格是否真的对应当前画面中的那个六边形格。

第一个问题可由官方 Overwolf in-game Overlay 能力、受支持游戏窗口信息、窗口尺寸变化和渲染表面变化处理。它可以可靠跟随游戏窗口、无边框窗口、分辨率切换与 Overlay 根视口变化，但不自动知道棋盘在画面内部的像素位置。

第二个问题必须由本节的定位层单独解决。TFT GEP 或其他合规数据源可在能力存在时提供棋子、棋盘格等语义数据；它们不能作为当前棋盘四角、相机缩放、UI 缩放、透视关系或 Overlay CSS 像素坐标的来源。

因此禁止把“Overlay 已跟随窗口”误判为“箭头已贴合棋盘”。

### 24.3 运行模式与基础显示边界

首版精确棋盘内提示只支持经过启动自检的 Overlay 可见模式：

- Overwolf 官方支持的 in-game Overlay 运行方式优先。
- 可见、可追踪的窗口化或无边框窗口模式。
- Overlay 根视口、游戏客户端可见区域和当前显示器 DPI 能被一致读取的模式。

若平台、游戏运行方式或独占全屏状态无法证明 Overlay 位于正确渲染表面：

    BoardInlay = Unavailable
    →
    不创建棋盘内 OverlayDelivery
    →
    Dock / Panel 继续

不得通过自行注入、进程修改或绕过平台机制来“强行覆盖”游戏。

### 24.4 三层坐标模型

站位分析、第三方站位模板和表达 Skill 只处理语义坐标；任何业务模块都不得直接输出屏幕像素。

完整投影链为：

    Advice / PositioningRecommendation
    → BoardCellId
    → CanonicalBoardCoordinate
    → H(viewGeneration)，当前棋盘平面到 Overlay 局部平面的 Homography
    → Overlay local CSS px
    → 当前实际渲染帧

在必须处理宿主窗口位置时，额外明确：

    Overlay local CSS px
    ↔ Overlay device px
    ↔ 游戏 client-content px
    ↔ Windows virtual-desktop physical px

各坐标对象至少携带：

- coordinateSpaceId。
- viewportSignature。
- monitorId。
- devicePixelRatio。
- viewGeneration。
- capturedAtMonotonic。

禁止隐式把下列坐标混在一起：

- Windows 虚拟桌面物理像素。
- 游戏逻辑或渲染像素。
- Overlay 窗口像素。
- 浏览器 CSS 像素。
- 屏幕捕获图像像素。
- 标准棋盘单位。

若使用 Overwolf 的 DPI 感知选项，整条链必须服从同一坐标体系；不得在已由宿主转换的窗口坐标上再次手动乘 DPI。多显示器时虚拟桌面坐标可能为负，主显示器左上角不得被假定为全局原点。

### 24.5 语义棋盘架构

《云顶之弈》棋盘按六边形而不是普通矩形方格建模。推荐、数据包和 Overlay 均使用稳定的 CellId 与六边形标准坐标：

    BoardCellId
    → TftCellSchema
    → (hexColumn, hexRow)
    → CanonicalHexCenter

TftCellSchema 是版本化内容数据，至少包含：

- 规则集和 GEP / 数据源版本。
- PlayerBoard 或未来独立的 OpponentBoardFrame。
- CellId 到语义六边形坐标的映射。
- 当前观察视角下的行列方向和翻转规则。
- 棋盘边界、格中心、可选备战席语义区域。
- schemaVersion 与兼容范围。

禁止以 cell_n = row × 7 + column 之类的算术关系替代版本化映射。己方、敌方和不同观察语义可能使用不同编号或方向；即使首版只在己方棋盘显示精确提示，也必须为 PlayerBoardFrame 与未来 OpponentBoardFrame 留出独立类型边界。

### 24.6 动态 ViewportTransformTracker

ViewportTransformTracker 是唯一能够发布 CurrentBoardProjection 的组件。它拥有：

- 当前窗口、Overlay 根视口、DPI 和显示器状态。
- 当前 ViewportSignature。
- 单调递增的 viewGeneration。
- 上一份已验证的 Homography H。
- 自动定位候选与人工校准候选。
- 定位置信度、残差、锚点数、稳定性和失效原因。

推荐状态机：

    Disabled
    → AwaitingSupportedSurface
    → Acquiring
    → CandidateTransform
    → Validating
    → StableHigh
    → Relocating
    → StableMedium / Unavailable
    → ManualCalibration

任何窗口、DPI、显示器、视口、游戏内缩放或视觉漂移导致棋盘几何可能变化时：

    viewGeneration += 1
    → 旧 H 立即降为不可用于精确渲染
    → 已显示棋盘内箭头和精确格子在下一渲染帧前隐藏
    → 异步获取、验证新的 H
    → 只有当前 generation 的 High 结果才恢复棋盘内层

异步检测任务、截图、锚点结果、渲染提交和回执都必须附带 expectedViewGeneration。旧 generation 的迟到结果直接丢弃，不能在缩放后把旧箭头重新画回屏幕。

### 24.7 重新定位的触发源

直接可观测的外层触发包括：

- 游戏分辨率、窗口模式或 Overlay 根视口变化。
- 受支持平台提供的 resolutionChanged 或等价窗口信息更新。
- Overlay 根尺寸、ResizeObserver 或画布实际尺寸变化。
- devicePixelRatio、Windows DPI、显示器或主显示器变化。
- 游戏窗口移动、最小化、恢复、失焦、重建渲染表面。
- Overlay 自身缩放或宿主重新创建。

必须特别处理“窗口矩形不变、棋盘内部变了”的触发。它们包括：

- 游戏内画面缩放。
- 相机或视角缩放。
- UI 缩放或画面比例变化。
- 黑边、超宽屏、渲染比例变化造成的 content rect 变化。
- 进入、退出或切换到棋盘画面时的布局变化。
- 入场动画、过渡动画、遮挡或视觉漂移。

后者通常不会被窗口 API 直接报告，因此只有轻量视觉锚点验证才能发现。

### 24.8 自动定位算法与频率控制

自动定位不依赖识别全部弈子或对整屏做持续 OCR。它按以下三层工作：

1. 缓存预测：存在上一个 High H 时，根据新 client-content rect 和 Overlay 视口产生 H_pred。预测只作为候选 ROI，不可直接恢复精确箭头。
2. 稳定几何锚点：仅在预测 ROI 或棋盘候选区检测棋盘外缘、六边形格方向、局部交点、棋盘角点或经版本适配的轻量棋盘锚点。锚点应尽可能独立于英雄数量、装备图标和文本。
3. 鲁棒拟合与时序验证：用不少于四个不共线锚点求解 H；正常运行优先使用六至十二个锚点，RANSAC 排除被弈子、特效、商店、弹窗遮挡的异常锚点，再检查重投影误差与连续帧稳定性。

H 是从标准棋盘平面到当前 Overlay 局部平面的 3 × 3 Homography。它可表达平移、非等比缩放、旋转、透视与常见显示比例变化；若视觉模型认为当前场景无法由合理的 H 解释，则进入 Medium 或 Low，不强行拟合。

频率策略：

- 无棋盘内精确提示、战斗阶段或棋盘不可见：停止或极低频验证。
- 备战阶段且存在 Active 精确站位提示：只截取棋盘 ROI，以约 10～15 FPS 做轻量漂移跟踪。
- 平稳、无提示的可见棋盘：约 2～5Hz 校验即可。
- 发生 resize、DPI、画面缩放或漂移：短时提高频率，完成两次稳定估计后回落。
- 不进行整屏 60 FPS OCR，不连续识别所有英雄、装备或伤害数字。

视觉帧只在内存中处理；默认不记录截图、视频或可复原的连续画面到磁盘。

### 24.9 置信度门控与视觉降级

置信度不是面向用户的模糊百分比，而是严格决定哪些视觉表达可以出现。

| 定位状态 | 可交付内容 | 禁止内容 |
|---|---|---|
| High | 精确六边形高亮、目标格、最多一至两个关键移动箭头、极短标签 | 无 |
| Medium | Dock 中的前后排、左右侧、角落等方向性提示；Panel 的完整站位说明 | 任何看似精确的格中心、移动箭头、逐格高亮 |
| Low / Unavailable | Dock 的“建议已更新，棋盘正在重新对齐”或低打扰状态；Panel 的完整建议和校准入口 | 所有棋盘内标注 |
| ManualCalibration | 侧边 Panel 中的校准预览 | 穿透到游戏的校准点击、未验证的精确箭头 |

High 至少同时满足：

- 足够数量的有效锚点及合理的 inlier ratio。
- 相对六边形直径归一化后的平均重投影误差不高于版本化 High 阈值。
- 关键锚点最大误差不高于版本化上限。
- 连续两个或更多短时间样本中的 H、棋盘中心和格直径稳定。
- 当前 viewportSignature、monitorId、devicePixelRatio 与 H 计算时一致。
- 投影后的关键格仍位于当前可见游戏内容区域。

初始工程阈值以格直径的相对误差而非固定屏幕像素定义。例如可从以下受测配置起步：

    mean reprojection error ≤ 0.06 cell diameter
    max key-anchor error ≤ 0.12 cell diameter
    adjacent stable-sample center drift ≤ 0.03 cell diameter

这些数值是版本化验证参数，不是散落在渲染代码中的硬编码常量；只有经过不同分辨率和 DPI 的实测后才能提高或放宽。

Medium 的核心纪律是：宁可说“左侧前排承伤”或“后排角落防刺客”，也不得画一个可能偏半格的落点箭头。

### 24.10 缩放、漂移与失效时序

在 StableHigh 状态，任何高优先级视口变化都按以下顺序执行：

    DetectViewportOrDrift
    → IncrementViewGeneration
    → HideExactBoardInlay
    → PublishProjectionInvalid
    → ReacquireAndValidate
    → RestoreHigh 或 KeepDegraded

HideExactBoardInlay 不等待视觉模型完成。旧箭头、旧高亮和旧标签应在不超过一个渲染帧内撤销；新的精确标注只有在新的 H 通过验证后才出现。

目标性能：

| 路径 | 目标 |
|---|---|
| 已验证 H 下的单 CellId 投影 | P95 ≤ 2ms |
| 建议结论可用到精确箭头首显 | P95 ≤ 100～150ms |
| 已知窗口、DPI 或分辨率变化后隐藏旧精确标注 | 不超过一个渲染帧 |
| Overlay 根视口重新贴合游戏表面 | P95 ≤ 100ms |
| 活跃 ROI 跟踪发现游戏内部缩放或漂移 | 约 67～100ms |
| 内部缩放后恢复 High 精确格位 | P95 约 200～300ms；失败则继续降级 |

性能目标强调“快速失效，安全恢复”，不要求在几十毫秒内不间断地强行重新画箭头。

### 24.11 首次校准、缓存与人工兜底

四点校准保留，但用途限定为：

- 首次布局的初始语义锚定。
- 自动定位失败时的恢复入口。
- 新版本、未识别画面布局或持续 Medium / Low 的 fallback。

推荐流程：

    自动粗定位
    → 计算并验证候选 H
    → High：直接启用
    → 失败或不稳定：侧边 Panel 显示静态棋盘预览
    → 用户在预览中点选四角或若干已知格中心
    → 计算 H
    → 对当前画面再次视觉验证
    → 验证通过才恢复精确棋盘内层

正常 Overlay 在所有状态、包括校准状态下，都必须完全鼠标穿透。校准只在独立 Panel 的预览中完成，点击不会到达游戏；不提供在游戏画面上接收或消费点击的校准模式。

可持久化的校准缓存仅写入：

    D:\AlifeData\TFTCompanion\settings\overlay-calibration\

单条记录最多包含：

- layoutSignature。
- 游戏逻辑 / 渲染分辨率与 client-content rect 特征。
- Overlay CSS 尺寸、DPI、devicePixelRatio、monitor 特征。
- 棋盘锚点、H、残差、TftCellSchema 版本、calibrationVersion。
- 创建和最后成功验证的单调或可审计时间信息。

默认不保存截图、视频、原始帧、敌方棋盘画面或 OCR 文本。缓存重载后只能作为 H_pred，必须重新经过当前画面的视觉验证，不能直接被视为 High。

### 24.12 混合式布局在不同置信度下的行为

混合式 C 布局有三层，且三层不共享“是否显示”的单一开关：

    棋盘内透明格子 / 箭头 / 极短标签
    +
    小型 Edge Dock
    +
    独立 Side Panel

High：

- 棋盘内显示最多一至两个关键移动、目标格或局部高亮。
- Dock 显示当前动作摘要、剩余有效期或“已按当前棋盘校准”的低打扰状态。
- Panel 保留理由、模板来源、替代方案和数据新鲜度。

Medium 或 Relocating：

- 立即收回棋盘内的精确表达。
- Dock 只保留方向性建议或“正在重新贴合棋盘”。
- Panel 持续显示语义建议，但清楚标注“精确落格暂不可用”。

Low、Unavailable、失焦、最小化、战斗阶段或 session 不可验证：

- 不显示棋盘内精确标注。
- 不因旧 Advice 自动恢复旧箭头。
- 根据 Advice 生命周期保留、降级或关闭 Dock；Panel 仅保存仍然有效或历史化的解释。

AdviceCoordinator 的 OverlayDelivery 只有在当前 adviceId、adviceRevision、viewGeneration 和定位状态均有效，且实际显示达到 minimumVisibleDuration 时才可报告 Delivered。投影失效、窗口不可用、战斗阶段撤回或 generation 不匹配必须产生明确的 OverlayProjectionInvalid、OverlayWindowUnavailable、HiddenForReacquire、Expired 或其他终态回执，不能伪造成功。

### 24.13 与 AdviceCoordinator、表达 Skill 和站位引擎的接口

站位引擎输出：

- PositioningRecommendation。
- source snapshot 与有效回合 / DecisionWindow。
- PlayerBoard 的 BoardCellId 目标与可选移动来源。
- 最多一至两个高价值动作。
- 置信度、前提、替代方案和失效条件。

表达 Skill 输出：

- 适合棋盘内层的短标签候选。
- Dock 的极短表达。
- Panel 的完整、陪伴式解释。

表达 Skill 不得：

- 生成像素位置。
- 绕过定位置信度强行要求绘制精确箭头。
- 把 Medium / Low 状态包装为“已精准定位”。

Overlay Host 在收到 Delivery 后必须二次验证：

    adviceId + adviceRevision 仍为 Current
    +
    scope / expiresAt 仍有效
    +
    expectedViewGeneration = Current
    +
    projection confidence 满足该视觉元素需求

任一条件不满足时，不能显示旧的精确内容。语义建议可以继续转交 Dock 或 Panel；同一个 semanticDigest 因缩放后重新投影时不得触发第二次语音。

### 24.14 D 盘、隐私、日志与资源限制

Overlay 相关的校准、设置、诊断、临时视觉制品、日志和可选崩溃报告均只能写入：

    D:\AlifeData\TFTCompanion\

不得因 D 盘不可用而把截图、缓存、日志或校准数据回退到 C 盘。D 盘不可用时：

    MemoryOnlyDegraded
    →
    不持久化新校准
    →
    可继续使用当前已验证的内存 H
    →
    重启后要求重新验证或重新校准

运行诊断默认只记录不可逆的数值与状态，例如：

- viewGeneration。
- viewportSignature 的哈希或脱敏摘要。
- 置信度状态、锚点数量、残差桶、失效原因。
- 定位耗时、渲染耗时、是否降级和是否人工校准。

默认不记录棋盘截图、视频帧、英雄名称、聊天内容、账号标识或可复原的对局画面。若未来提供用户主动导出的故障包，必须单独显示内容清单、有效期、脱敏策略和 D 盘占用上限，且不自动上传。

### 24.15 失败体验、成本与不做项

失败体验必须低打扰：

- 自动定位失败不弹出阻挡游戏的错误窗口。
- Dock 只用简短状态说明，不连续播报“定位失败”。
- Panel 提供“重新校准”入口和可理解的原因，例如“检测到画面缩放，正在重新贴合”。
- 定位层异常不能阻塞语音、Panel、经济建议或回合追踪。

初步工程工作量：

| 子项 | 粗略工作量 |
|---|---|
| 坐标类型、TftCellSchema、投影接口与 generation 隔离 | 1～2 人日 |
| 官方 Overlay 表面、DPI、多显示器和透明穿透适配 | 1～2 人日 |
| ROI 锚点、H 拟合、漂移跟踪与置信度状态机 | 3～4 人日 |
| Panel 校准预览、D 盘缓存、降级 UI 与回执 | 1～2 人日 |
| 自动化、黄金截图和人工显示配置验收 | 2～3 人日 |
| 合计 | 约 8～12 人日 |

这是设计复杂度和验证工作量，不是版本排期承诺。它不包括完整 Overlay 窗口工程、IPC、站位算法、第三方 Provider 或持续版本适配。

明确不做：

- 用密集全屏视觉识别替代棋盘锚点定位。
- 以低置信度的格子箭头“凑连续性”。
- 通过游戏输入、注入、内存或抓包修正坐标。
- 默认存储任何对局截图或视频。
- 因精确棋盘内层不可用而停止整个陪伴教练能力。

### 24.16 测试与验收合同

单元、属性、集成、黄金视觉和人工验收至少覆盖：

1. 坐标转换：CellId → CanonicalHexCenter → H → Overlay 坐标在不同分辨率、DPI、CSS 缩放和负虚拟桌面坐标下可复现。
2. schema 隔离：PlayerBoard 与未来 OpponentBoard 的 CellId 映射不会混用，不允许算术猜测编号。
3. generation 隔离：缩放后迟到的旧 H、旧截图结果、旧渲染提交和旧回执不能恢复任何精确箭头。
4. 外层变化：窗口拖动、大小变化、无边框切换、最小化恢复、100% / 125% / 150% / 200% DPI、跨显示器移动及 Overlay 重建。
5. 内层变化：游戏内缩放、UI 缩放、画面比例、黑边、棋盘入场、过渡动画、特效遮挡和棋盘短暂不可见。
6. 置信度门控：High 才出现精确格位；Medium 只出现方向性 Dock / Panel；Low 一律不画棋盘内标注。
7. 交互安全：Overlay 在正常、校准、取消、异常和超时状态下均完全穿透；Panel 校准点击不会到达游戏。
8. Advice 合同：投影失效会产生真实回执；semanticDigest 重投影不会重复语音；过期或 superseded Advice 不会在重新定位后复活。
9. 性能：满足第 24.10 节的投影、隐藏、漂移发现与恢复目标；无精确提示时不保留高频视觉循环。
10. 隐私与 D 盘：默认无图像持久化，所有插件可写数据只在 D:\AlifeData\TFTCompanion，D 盘故障不回退 C 盘。

本节完成定义为：精确棋盘内提示的安全边界、动态缩放恢复、坐标体系、置信度降级、手动校准、只读交互、D 盘与隐私边界、Advice 回执和验收条件均已明确。下一步只继续讨论 Overlay 的其余窗口工程、Overwolf Bridge / Alife IPC 与隐私验证，不进入实现或版本规划。

## 25. Overwolf Bridge、TftCompanionHost 与 Alife IPC 设计（已确认，含 PoC 门）

本节记录已确认的游戏事件采集、Overlay 渲染桥接、独立 Companion Host、与 Alife 的有限协作、实时 IPC、回执、恢复、安全与验证合同。它不实现、不启动服务、不开放端口，也不改变第 2 节的只读游戏边界。

### 25.1 设计选择

采用：

    Overwolf Background Bridge
    +
    独立 TftCompanionHost
    +
    两条物理 Loopback WebSocket
    +
    语义状态消息与声明式渲染模型
    +
    LiveStateReducer 与 AdviceCoordinator 的单写者边界
    +
    至少一次传输加应用层幂等
    +
    GapDetected 后快照重建
    +
    真实 Delivery 回执

不采用：

- 将实时 GEP 事件塞入 Alife 通用 AgentEventPipeline、LifeEventStream 或 WebBridge。
- 让 Overlay 窗口自行订阅 GEP、决定策略、持久化或调用 TTS。
- 直接复用桌宠 PetProcess、普通 SpeechService、QQ OneBot 队列或通用 VisionService。
- 文件投递、SQLite polling、HTTP 长轮询作为游戏实时路径。
- 未受限的本机 HTTP 控制 API、局域网监听、任意 HTML / JS / URL 渲染或任意脚本通道。
- 游戏输入、焦点控制、进程启动、内存读取、抓包、截图默认落盘或任何自动化游戏操作。

主架构选择是“可与 Alife 协作、但运行时独立”的 TftCompanionHost，而不是将游戏教练嵌入 Alife 的通用桌面、QQ、WebBridge 或语音生命周期。

### 25.2 现有 Alife 边界与不可直接复用项

D:\Alife 是权威 Alife .NET 源码；D:\FOXD\alife-service 是历史快照，不作为新 TFT 设计的权威实现依据。

以下既有组件可以借鉴边界思想，但不能直接作为实时游戏教练基础设施：

| 既有组件 | 可借鉴 | 不可复用原因 |
|---|---|---|
| AlifeManagementApiHost | 仅 loopback、Bearer、显式启用、健康快照 | 只读低频 HTTP 状态面，不是实时 Advice、棋盘帧、语音或 Overlay 总线 |
| WebBridgeService / WebApiClient | Bearer、取消、路径安全、包 hash | Web 配置、资产和安装流有秒级 timeout 与不同信任模型，不能承载对局实时数据 |
| AgentEventPipeline | 低频语义事件按优先级路由 | Handler 串行、无有界背压、无 transport epoch、无真实回执、无恢复合同 |
| LifeEventStreamService | 赛后或低频复盘摘要 | 为 LLM 上下文服务且会同步落盘，不适合高频游戏状态 |
| PetProcess / PythonPipeProcess | 子进程握手、命令与事件方向分离、重启经验 | 标准流 JSON 行缺少 session、sequence、ACK、lease、乱序隔离和有界队列；PetProcess 还会写相对路径日志 |
| OneBotClient / QChatService | correlation、请求与回包分离、单读者处理思想 | QQ 的连接、队列、失败域与游戏教练完全独立，不得共用 |
| DeskPet 透明窗口 | DPI 与透明窗口的经验 | 不具备永久鼠标穿透、游戏 client rect 跟踪、Per-Monitor DPI V2、viewGeneration 或置信度门控 |
| WindowCaptureHelper | 合规 HWND / Windows Graphics Capture 的接入经验 | 单次截图、GPU 到 CPU 拷贝与可能的窗口恢复行为，不适合低延迟持续定位 |
| SpeechService | 普通 TTS 与播放适配基础经验 | 不具备已确认的 GameVoiceScheduler、PlaybackSession、outputReleased、优先级和独立 Provider 合同 |
| AlifePath / ConfigurationSystem | 无 | 默认 Runtime / Temp 与安装根绑定，可能写入 C 盘；TFT 不得依赖 |

TftCompanionHost 可以把赛后摘要、低频陪伴事件或脱敏健康摘要异步适配给 Alife；这些信息不能反向控制实时状态、Advice、Overlay 或游戏教练语音。

### 25.3 组件职责和单向能力边界

整体拓扑：

    TFT 官方 GEP / 游戏窗口信息
        →
    Overwolf Background Bridge
        ├─ Overwolf 内部窗口消息 → Overlay Renderer
        ├─ /ingest WebSocket → TftCompanionHost
        └─ /render WebSocket ↔ TftCompanionHost
        →
    TftCompanionHost
        ├─ IPC Gateway
        ├─ LiveStateReducer
        ├─ Session / Round Tracker
        ├─ ViewportTransformTracker
        ├─ AdviceCoordinator
        ├─ GameVoiceScheduler
        ├─ PanelProjection Store
        ├─ Strategy / Provider adapters
        └─ D 盘审计、SQLite 与诊断
        →
    独立游戏 TTS / Alife Side Panel / Overwolf Overlay Renderer

Overwolf Background Bridge：

- 是唯一订阅 GEP 的组件。
- 在游戏会话开始后尽早注册所需 feature，并进行有限、带退避的注册重试。
- 只接收 GEP event、info update、当前 snapshot、窗口 / capability 状态并转为版本化语义事件。
- 不做策略、LLM 调用、第三方 Provider、TTS、SQLite 持久化或 Advice 关闭仲裁。
- 不把原始 GEP payload、截图或连续画面写入 console、日志或文件。
- 不因 Overlay 窗口隐藏、缩放、失焦或重建而停止 GEP 订阅。

Overlay Renderer：

- 只接收 Host 发出的声明式 OverlayRenderModel。
- 永久鼠标穿透、不可抢焦点、不可接收校准点击。
- 只做 Show、Update、Hide、ViewportStatus 与事实回执。
- 不调用 GEP、不保存策略、不拼接任意 HTML / JS / URL、不能调用游戏控制 API。

TftCompanionHost：

- 是实时状态和 Advice 的唯一业务宿主。
- IPC Gateway 是外部消息的唯一验证入口。
- LiveStateReducer 是游戏状态唯一写者。
- AdviceCoordinator 是 Advice、Delivery、revision、lease 关闭原因与最终结果唯一写者。
- GameVoiceScheduler 保持与 QQ、普通 SpeechService 完全独立的 TTS、队列、播放和故障域。
- Provider 网络凭据、内容包、版本规则、D 盘持久化和隐私策略只能位于 Host。

Alife Side Panel：

- 仅消费低频、结构化、已仲裁的 PanelProjection。
- 仅发送设置、静音、跳过、手动朗读、校准请求等受控命令。
- 不接触原始 GEP 流，不直接向 Overlay、TTS 或 AdviceCoordinator 写状态。

### 25.4 方案比较与选择

| 方案 | 优点 | 缺点 | 决定 |
|---|---|---|---|
| A. 直接嵌入 Alife 进程 | 组件最少 | 与 Alife 路径、UI、QQ、WebBridge、普通语音及故障域耦合；难隔离高频实时流 | 不采用 |
| B. Background Bridge + 双 Loopback WebSocket + 独立 Host | JS 与 .NET 都易接入；延迟低；支持推送、回执、回压、重连和会话隔离 | 需要验证 Native Runtime 的 WebSocket、Origin、CSP 与 manifest 行为 | 已选主路线，受 PoC 门约束 |
| C. Bridge + 本地 Sidecar，Sidecar 到 Host 使用 Windows Named Pipe | Host 不直接开放 TCP，本机进程隔离更强 | 多一个 native 进程、发布、配对、维护与兼容性成本 | 未来本机威胁模型升级时再评估 |
| D. 文件 / SQLite / HTTP polling | 实现表面简单 | 写盘残留、抖动、队头阻塞、丢失边界不清、回执不真实 | 明确不采用 |

选择 B 的原因是：当前是用户自用、只读、以低延迟陪伴教练为目标的场景；最需要的是数据与 UI 的隔离、可验证回执与可控恢复，而不是一开始就引入 Sidecar / Named Pipe 的额外发布复杂度。

### 25.5 双通道 Loopback WebSocket 合同

采用两条独立物理连接，而不是单条混合 WebSocket：

    /ingest
        Bridge → Host：GEP event、info update、snapshot、capability、health
        Host → Bridge：ACK、ResyncRequired、Rejected

    /render
        Host → Bridge：Show、Update、Hide、Invalidate、HideAll
        Bridge / Renderer → Host：Accepted、OverlayShown、OverlayDelivered、Hidden、Failed、ViewportStatus

两条通道均只能绑定：

    127.0.0.1

如未来需要 IPv6，必须单独、显式绑定 ::1 并独立验收；禁止 0.0.0.0、局域网 IP、公共 DNS 或外网转发。

双通道的目的：

- 防止 snapshot 或 GEP burst 阻塞 P1 的 Invalidate / Hide。
- 防止大量 ingress ACK 延迟 Overlay 的真实回执。
- 让 ingress 的事件背压与 render 的 lease / revision 丢弃策略独立。
- 让断线、重连、指标与协议错误可按角色单独诊断。

控制帧不压缩。普通消息、snapshot、分片与单连接内存缓冲均有版本化的字节与条数上限；不传原始截图、连续画面、任意 HTML、任意 JS、任意 URL 或任意二进制文件。

### 25.6 首个兼容性 PoC 门

方案 B 不是未经验证的实现承诺。进入实现前，必须先通过最小兼容性 PoC：

1. 当前 Overwolf Native Runtime 能否从 Background Bridge 建立到 127.0.0.1 的双向 loopback WebSocket。
2. 实际 Origin、CSP、manifest 的 overwolf.web / externally_connectable 配置与 token 传递方式是否可行。
3. Overlay 重建、隐藏、缩放、失焦期间，Background 到 Renderer 的内部消息和回执是否仍符合有界时序。
4. 游戏启动、GEP feature 注册、onNewEvents、onInfoUpdates2 与 getInfo snapshot 的实际顺序、失败与有限重试语义。
5. 目标机器上消息大小限制、P95 延迟、重连、断线和隐私日志策略是否达标。

PoC 必须只使用假的、最小化的语义 payload；不记录真实 GEP payload，不保存截图，不接入任何第三方 Provider 或 TTS。

PoC 未通过时：

    不用 HTTP polling、文件中转或 SQLite polling 临时替代
    →
    评估 C：小型本地 Sidecar + Windows Named Pipe
    →
    重新审阅配对、安全、发布和维护成本

### 25.7 通用 Envelope、握手与消息分类

所有跨 Bridge / Host 的消息均采用 UTF-8 JSON 与明确 schema。GEP 数据量不需要因为“看起来更快”而过早引入 protobuf、gRPC-Web、代理或不透明二进制协议。

通用 Envelope 至少包含：

    protocolVersion
    schemaVersion
    messageKind
    messageId
    correlationId
    causationId
    payloadHash

    bridgeInstanceId
    transportEpoch
    streamId
    streamSequence
    connectionEpoch

    sourceTime
    traceFlags

字段规则：

- messageId 使用可排序 UUID，例如 UUIDv7。
- bridgeInstanceId 每次 Bridge 进程启动重新生成。
- transportEpoch 在同一 Bridge 的传输重建时递增。
- streamId 区分 /ingest 和 /render 及其重建实例。
- streamSequence 在单 stream 内严格递增；不能跨 stream 比较。
- connectionEpoch 由 Host 握手后分配，旧 epoch 的任何回包无效。
- serverInstanceId 是 runtimeEpoch 的不透明 wire 表示；每次 Host 启动必须变化，不能形成独立于 runtimeEpoch 的第二套生命周期。
- sourceTime 仅用于诊断，不作为跨 JS / .NET 的排序依据。
- Host 接受每个外部 Envelope 后追加 ingestMonotonicTime 与严格递增的 hostIngressSequence；只有这对 Host 本地值可用于跨进程 ingress 排序。进入 AdviceCoordinator 后由其另行分配 coordinatorSequence，作为 Advice、Delivery 与 Voice 的最终仲裁序号。

Hello 至少包含：

    protocolVersion
    schemaVersion
    role
    bridgeInstanceId
    transportEpoch
    streamId
    lastAck
    gameId
    requestedFeatureHash
    capabilitySet
    nonce / pairing proof

Welcome 至少包含：

    serverInstanceId
    connectionEpoch
    allowedScopes
    channelBindingId
    resumeAccepted
    resyncRequired
    currentSessionEpoch

消息分类：

| 类型 | 方向 | 关键规则 |
|---|---|---|
| GepEvent / InfoUpdate | Bridge → Host | 保留 Present / Absent / Unknown，Bridge 不自行拼成完整真相 |
| StateSnapshot | Bridge → Host | 带 bridgeSnapshotId、捕获语义和 schema；Host 仅在验证并由 Reducer 分配 ingressStateSnapshotId 后才覆盖相应状态 |
| Ack | Host → Bridge | 仅确认收到和持久 / 内存接受，不代表 Advice 或 Overlay Delivered |
| GapDetected / ResyncRequired | 双向 | 立即停止依赖缺失事实的精确结论，走 snapshot 重建 |
| Capability / Health | Bridge → Host | 功能健康只作能力门控；外部 health 可能滞后，不能当实时事实 |
| OverlayCommand | Host → Bridge | 只有声明式 Show / Update / Hide / Invalidate / HideAll |
| OverlayReceipt | Bridge → Host | 只报告实际事实，不能直接修改父 Advice |
| ViewportStatus | Bridge → Host | 仅报告 observedViewportSignature、可见性、定位观察事实、诊断摘要及最后命令的 targetViewGeneration 回显；只有 Host 的 ViewportTransformTracker 可递增或发布 Current viewGeneration |
| UserControl / PanelProjection | Host ↔ Alife Panel | 低频、受控、无原始 GEP 流 |

### 25.8 会话、状态与跨进程时间边界

会话字段至少包括：

    gameId = 21570
    sessionId
    sessionEpoch
    matchFingerprint / pseudoMatchId（可用时，仅本机短期身份辅助）
    roundKey
    rulesetKey
    gepSchemaVersion
    capabilitySnapshotId

sessionId 与 sessionEpoch 均由 Host 的 SessionRoundController 创建，不能只依赖 pseudoMatchId 或“时间接近”。matchFingerprint / pseudoMatchId 不得持久化为跨局身份、不得进入 Panel、LLM、第三方网络或诊断正文。同一 Host 的 Bridge 重连只有在完整当前 snapshot 可靠证明仍为同一逻辑会话时，才可保留 sessionId / sessionEpoch 并从 Suspended 恢复；无法证明时旧 session 标记 Interrupted，且只有当前 snapshot 支持启动时才创建不合并旧 Advice 或旧视觉的 Starting(RecoveredPartial)。

跨进程禁止直接比较：

    JavaScript performance.now()
    与
    .NET Stopwatch 的绝对值

Host 的 `(ingestMonotonicTime, hostIngressSequence)` 是跨进程 ingress 的唯一排序权威；AdviceCoordinator 的 coordinatorSequence 是其内部 Fact、Command、Receipt 与交付冲突的最终决胜依据。跨进程 deadline 传递 remaining TTL 和可选 wall-clock 诊断值；最终过期、关闭、抢占与 scope 仍由 AdviceCoordinator 决定。

Bridge 可做轻量传输去重和 bounded ring；它不得决定“哪条游戏状态是真的”“哪条 Advice 还应播放”“是否关闭 Delivery”。

### 25.9 至少一次传输、背压与 gap 恢复

传输可靠性定义为：

    本地 IPC = 至少一次 + 应用层幂等
    GEP 源事件 = 可能丢失，不能事后承诺完整补齐

不得宣称 exactly-once，也不得把去重成功误说成“游戏状态完全无缺”。

Bridge 只在内存中为未确认消息保留有界 ring buffer，按条数和字节数双上限控制。不同类别的策略：

| 数据 | 队列 / 合并策略 |
|---|---|
| 游戏阶段、回合、金币、商店、己方棋盘语义状态 | 可合并为最新有效状态；过期中间态可丢弃 |
| viewport / 几何变换 | 只保留最新 viewGeneration |
| Overlay Show / Update | 相同 renderScope 只保留最新 renderGeneration |
| Overlay Hide / Invalidate | 高优先级，不得被快照或普通状态阻塞 |
| Advice / Voice Delivery | 不能按“已发送”视为完成，继续遵守 AdviceCoordinator 的真实回执合同 |
| 用户控制命令 | commandId 幂等，返回 Applied / NoOp / Rejected / Failed 等明确事实 |
| 诊断 | 有界环形缓冲，低优先级可丢弃 |

发现任何以下条件时：

- streamSequence 缺口。
- 未确认 ring 溢出。
- Bridge 或 Host 重启。
- resume 链不连续。
- schema 不兼容。
- GEP capability 失效或 snapshot 无法验证。

必须执行：

    GapDetected
    → ResyncRequired
    → Bridge 调 getInfo() 获取当前 snapshot
    → Host 验证 session / ruleset / schema
    → LiveStateReducer 创建新的状态 revision
    → 重新评估仍在有效期内的 Advice

在 gap 未被可靠重建期间，所有依赖该实时事实的精确站位、牌库、对手预警或棋盘内提示必须降级；不能猜测、插值、补播旧语音或复活旧 Overlay。

### 25.10 Render lease、真实回执与旧消息隔离

必须严格区分：

    CommandAccepted
    ≠ OverlayShown
    ≠ OverlayDelivered

每个 OverlayCommand 至少携带：

    runtimeEpoch
    serverInstanceId
    connectionEpoch
    sessionId
    sessionEpoch
    roundKey

    adviceId
    adviceRevision
    deliveryId
    deliveryAttemptId
    replaceKey

    renderScope
    renderLeaseId
    renderGeneration
    targetViewGeneration

    operationId
    remainingTtl
    declarativeOverlayRenderModel

Overlay Renderer 只有在以下条件同时成立时才可显示：

    sessionEpoch 仍有效
    +
    renderLeaseId 仍被当前 Host 持有
    +
    renderGeneration 是该 renderScope 的最新世代
    +
    adviceId / adviceRevision 仍为 Current
    +
    deliveryAttemptId 匹配
    +
    targetViewGeneration = Current viewGeneration
    +
    定位置信度满足该元素需求
    +
    remainingTtl 未耗尽

命令重试或重复到达时：

- 相同 operationId 不得重新绘制、重置 minimumVisibleDuration 或重复动画。
- 每个 OverlayReceipt 必须原样回显 runtimeEpoch、serverInstanceId、connectionEpoch、sessionId、sessionEpoch、roundKey、adviceId、adviceRevision、deliveryId、deliveryAttemptId、renderLeaseId、renderGeneration 与 targetViewGeneration。
- Renderer 只重发相同事实回执。
- 任一身份字段缺失、不匹配或过期时，Show / Update 一律拒绝或转为 HideAll；旧 renderGeneration 的 Show、Update、Hide 一律丢弃。

缩放、定位失效、session 失效、socket lease 失效或 Advice supersede 时：

    先 HideExactBoardInlay
    → 再发送 HiddenForReacquire / OverlayProjectionInvalid / Expired 等事实回执
    → 不保留旧箭头等待可能恢复

真正的 OverlayShown 必须意味着元素已经实际挂载、当前可见且投影有效。只有达到 minimumVisibleDuration 后，OverlayDelivery 才可报告 OverlayDelivered。Accepted 只能说明 Bridge 已完成协议校验。

重连首步固定为：

    HideAll
    → 新 renderLease
    → 当前 snapshot / session 验证
    → 仅交付仍有效、仍 Current 的 revision

不得自动恢复旧局、旧回合、旧 viewGeneration 的 Overlay、语音或 Advice。

### 25.11 最小权限、鉴权与威胁模型

Bridge ingress scope 只允许：

    PublishGepEvent
    PublishSnapshot
    ReportHealth

Renderer scope 只允许：

    ShowDeclarativeOverlay
    UpdateDeclarativeOverlay
    HideDeclarativeOverlay
    ReportReceipt
    ReportViewportStatus

明确禁止的 IPC 方法或等价能力包括：

    SendInput
    MouseClick
    KeyboardInject
    GameFocus
    RunProcess
    MemoryRead
    PacketCapture
    ArbitraryScript
    ArbitraryHtml
    ArbitraryUrl

Host 对每个连接校验：

- loopback 地址。
- role、protocolVersion、schemaVersion 与 allowedScopes。
- 每次启动的 nonce、pairing proof、channel binding 和有限时效 token。
- 精确 Origin allowlist；token 不进入 URL query、日志、诊断或导出。
- 消息字节上限、速率上限、类型白名单、payload schema 和 connection / session epoch。

Host 不向 Bridge 暴露 Provider 凭据、规则包原文、SQLite、文件系统、TTS 控制或 AdviceCoordinator 写入权限。第三方攻略、阵容、TTS 或教练 Provider 只能经 Host 的授权、白名单、费用与隐私门控访问。

需要如实限定安全承诺：

    Loopback、Origin、nonce 与前端可见静态 secret
    可防止局域网暴露、普通网页和意外本机访问，
    但不能构成对同一 Windows 用户下已被攻陷的恶意进程的强身份认证。

若威胁模型未来升级为必须防御同一用户下的恶意本机进程，重新审阅 C 方案：本地 Sidecar、Windows Named Pipe ACL、每启动随机管道名与额外令牌。不得把这种更强安全需求悄悄假装成普通 WebSocket token 已经满足。

### 25.12 D 盘、隐私、日志与健康面

TftCompanionHost、Bridge 的受控运行时状态、SQLite、规则包、设置、音频缓存、诊断、导出与临时制品只允许使用：

    D:\AlifeData\TFTCompanion\

Bridge 原始 GEP 数据保持内存化：

- 不写 raw payload。
- 不写 console payload。
- 不默认记录截图、视频、英雄名、聊天、账号标识或连续棋盘画面。
- 有界诊断只记录脱敏状态、版本、延迟、计数、错误码、epoch、残差桶和状态转换。

TFT 不得使用：

    AlifePath
    StorageSystem
    ConfigurationSystem
    通用 VisionService 临时截图路径
    桌宠 AppBase cache / log 路径

作为持久化或临时写入的默认来源。

D 盘不可用时：

    MemoryOnlyDegraded
    →
    不新建持久化 spool 或日志
    →
    不回退 C 盘
    →
    继续当前内存可安全完成的功能
    →
    重启后重新验证，不恢复旧实时内容

可选健康面可在明确启用、loopback、Bearer 保护下提供低频只读摘要，例如：

    GET /api/tft-companion/health
    GET /api/tft-companion/status
    GET /api/tft-companion/diagnostics/summary

健康面只能报告 Bridge、Overlay、定位、TTS、Provider 和 session 的脱敏状态；它不能承载 GEP 帧、Advice、用户控制、渲染命令、真实语音回执或任何游戏操作。

“插件可控制的数据只写 D 盘”与“Windows、Overwolf、Chromium、驱动等第三方运行时自身永远不写 C 盘”必须明确区分。前者是本设计硬约束，后者不是插件可以完全保证的承诺。

### 25.13 性能目标、故障域与验收

初始性能目标：

| 路径 | 目标 |
|---|---|
| Bridge 回调入有界内存队列 | P95 ≤ 2ms，禁止阻塞 GEP 回调 |
| 正常 /ingest 消息到 Host 接收与 ACK | P95 ≤ 15ms，不含上游 GEP 延迟 |
| Host 发 render command 到 Bridge Accepted | P95 ≤ 15ms |
| 已验证投影下 render command 到 OverlayShown | P95 ≤ 50ms，不含 Advice 计算与下一可用渲染帧的外部延迟 |
| 建议可用到棋盘箭头首显 | 继续服从第 24.10 节 P95 ≤ 100～150ms |
| 断线或 lease 失效后撤销精确棋盘内层 | 不超过一个渲染帧或第 24 节的更严格目标 |

这些是工程验证目标，不是对第三方 GEP、Overwolf、Windows 调度或远程 Provider 的绝对承诺。

故障隔离：

- Bridge 断线：实时事实降级、精确棋盘层撤回；Panel 与已验证的本地解释按 scope 保留；不补播旧 Voice。
- Overlay Renderer 断线：OverlayDelivery 终态化；Panel、语音和分析继续。
- Host 重启：生成新 runtimeEpoch / serverInstanceId，并由新 snapshot 创建新的 sessionEpoch；旧 Advice、旧 render lease 和旧回执不得恢复。
- QQ、普通 TTS、WebBridge、桌宠或外部 Provider 故障：不得阻塞 GEP ingress、LiveStateReducer、定位失效撤回或 GameVoiceScheduler。
- GEP capability health 仅是能力门控；若外部状态端点滞后，当前 payload 新鲜度、schema、round 与 gap 状态仍是实时真相。

验收至少覆盖：

1. Background Bridge 在 Overlay 隐藏、重建、缩放和失焦时持续保持 GEP 订阅。
2. /ingest 大 snapshot 或 burst 不阻塞 /render 的 P1 Hide / Invalidate。
3. Hello / Welcome / version 不兼容、role 错误、token / Origin 错误、过大消息、超频消息和无权类型均被拒绝且不影响 Host。
4. 重复、乱序、旧 bridgeInstanceId、旧 transportEpoch、旧 connectionEpoch、旧 sessionEpoch、旧 viewGeneration 和旧 operationId 都不能复活旧视觉或修改新状态。
5. ring 溢出、序号缺口、Bridge / Host 重启、snapshot 不匹配和 GEP feature 失效均进入 GapDetected / ResyncRequired，并正确降级。
6. Accepted、OverlayShown、OverlayDelivered、Hidden、Failed 的定义和 AdviceCoordinator 的 Delivery 终态一致。
7. 游戏教练 TTS 与 QQ / 普通 SpeechService 不共享队列、设备控制、回退链或故障域。
8. D 盘不可用、默认 Alife 路径位于 C 盘、WebView2 / Vision / PetProcess 现有路径存在时，TFT 自己不回退 C 盘。
9. 默认不生成 raw GEP 日志、截图、视频或未授权诊断包。
10. 最小 PoC 在目标 Overwolf Runtime 上通过第 25.6 节全部项目后，才可进入完整桥接实现计划。

### 25.14 成本与完成定义

粗略工作量：

| 子项 | 粗略工作量 |
|---|---|
| Overwolf GEP / loopback / manifest PoC 与兼容性记录 | 2～3 人日 |
| Envelope、握手、双通道、schema 与有界背压 | 2～3 人日 |
| Session / epoch / gap / snapshot / recovery 合同实现与测试 | 2～4 人日 |
| Render lease、真实回执、Host 与 Overlay 隔离 | 2～3 人日 |
| D 盘、诊断、鉴权、负向安全与长稳验收 | 2～3 人日 |
| 合计 | 约 10～16 人日 |

这是架构实现与验证的复杂度估计，不是版本排期承诺，也不包含第 24 节的棋盘定位、站位分析、第三方策略 Provider、完整 Overlay UI 或 GameVoiceScheduler 的实现。

本节完成定义为：主进程拓扑、职责、双通道、PoC 门、协议、会话、幂等、gap、真实回执、权限、D 盘和验收边界均已明确并获用户确认。下一步继续讨论隐私、诊断导出与第三方 Provider 授权整合，或讨论 Overlay 其余窗口工程；在所有设计完成并获最终审阅前，不进入实现或版本规划。

## 26. 本地攻略知识包、RAG 与 DataAgent 设计（已确认）

本节记录第三方攻略内容、本地知识检索、统计口径、版本兼容、本地 DataAgent 联动、无上传边界与验收合同。第三方攻略平台在本设计中是“授权知识内容来源”，不是实时策略服务、教练 API、当前局面分析器或游戏数据接收者。

### 26.1 设计选择

采用：

    经授权的公开 / 静态攻略知识包
    +
    用户显式启用的离线下载或手动导入
    +
    D 盘本地验证、版本化、切片和索引
    +
    LocalGuideRetrieval
    +
    TftDataAgent 的本地查找与保存
    +
    StrategicDecisionCoordinator 最终仲裁

不采用：

- 向第三方攻略平台发送当前棋盘、金币、血量、商店、牌库、账号、匹配标识、对手状态、截图、视频、语音、聊天、查询、诊断或推荐结果。
- 在对局中、战斗中或用户提问时将当前局面拼入第三方在线查询。
- 让攻略平台或 RAG 直接决定金币、D 牌、升人口、追三、站位像素、Overlay 或语音。
- 未经授权抓取网站私有 API、隐藏端点、完整网页内容流或绕过许可证。
- 把攻略正文当作系统提示、工具调用、HTML / JS 或可执行指令。
- 复用 DataAgent 的 HTTP Sidecar、外部 LLM Planner、External RAG、Postgres 或默认持久化路径。

相对第三方攻略来源，第三方只产生受授权的入站知识内容；当前局面检索、分析、决策和交付都在本机完成。可选的受限远程自然语言表达仅可按第 27 节的显式启用、最小化上下文与 Validator 合同进行，不能借由攻略来源出站。

### 26.2 知识流与零上传边界

完整知识流：

    授权公开攻略内容
    → KnowledgeAcquisition（仅产生受控的内存入站候选，不写文件）
    → TftDataAgent / KnowledgePackValidator（唯一知识数据持久化写者）
        ├─ D:\AlifeData\TFTCompanion\packs\（原始授权包与 Manifest）
        ├─ D:\AlifeData\TFTCompanion\dataagent\（staging、ledger、Projection、checkpoint、tombstone）
        └─ D:\AlifeData\TFTCompanion\knowledge\（规范化事实、chunk、索引、embedding）
    → 已验证 KnowledgeSnapshot 发布
    → Host 内存只读 KnowledgeFactView
    → StrategicDecisionCoordinator
    → AdviceCoordinator

对局事实流：

    GEP / 本地规则 / 本地棋盘定位 / 用户设置
    → TftCompanionHost 内存状态
    → 冻结 DecisionSnapshot + KnowledgeFactView
    → StrategicDecisionCoordinator
    → 本地分析与最终建议

两条流只能在 TftCompanionHost 内部汇合。禁止存在：

    GameSnapshot
    → 第三方攻略网站 / API

也禁止存在：

    用户问题
    + 当前阵容
    + 当前牌库
    → 第三方攻略在线搜索

允许的联网动作仅是用户已显式启用的知识源在非对局期获得固定、公开或正式授权的内容包。下载请求不得以 URL、body、header、cookie、telemetry、tag 或请求时机携带当前游戏状态、用户身份或本地查询。

普通内容下载不可避免会让来源站点或 CDN 看到网络层元数据，例如 IP、请求时间和通用 User-Agent；这不属于游戏数据上传。若用户未来希望连自动下载也不发生，可切换为只允许手动导入离线知识包的隐私模式。

### 26.3 知识包分类

知识包按职责分层，不能将规则事实、统计结果、编辑建议和视觉站位混为一个不可审计的文本库。

| 包类型 | 典型内容 | 使用边界 |
|---|---|---|
| OfficialRulesPack | Set、patch、弈子、费用、羁绊、技能、属性、装备、组件、合成、神器、辅助装、强化、机制 | 本地确定性事实与版本校验基础 |
| MetaMetricsPack | 热门阵容、选取率、平均名次、前四率、第一率、样本数、分段、地区、时间窗、常见变体 | 候选排序与风险信号，不直接替代当前局面分析 |
| StrategyKnowledgePack | 阵容思路、过渡、经济路线、连败 / 连胜、D 牌与升人口窗口、追三条件、转型和失败条件 | RAG 证据、解释和候选路径 |
| PositioningTemplatePack | 基础阵型、角色位、前后排、对刺客 / AOE / 控制调整原则、适用前提 | 只产生 BaselineFormation；最终格位由本地棋盘与定位层决定 |
| SourceAndCompatibilityPack | 来源、许可、署名、发布时间、Set、patchRange、地区、分段、数据窗、hash、签名、过期时间 | 兼容性、审计、来源展示和失效门控 |

一个知识快照可以引用多个包，但所有被同一次决策使用的内容必须共享可验证的：

    knowledgeSnapshotId
    rulesetKey
    patchRange
    locale
    sourceManifestHash

### 26.4 热门阵容与统计指标口径

热门阵容可以进入 MetaMetricsPack 和 StrategyKnowledgePack，但“热门”不等于“适合当前玩家”，更不等于“应立即强玩”。

每个阵容至少包含：

    compFamilyId
    coreUnits
    coreTraits
    carryRoles
    tankRoles
    coreItems
    alternativeItems
    transitionBranches
    recommendedLevelingWindows
    rerollConditions
    baselineFormation
    applicability
    failureConditions
    sourceEvidence

TFT 的“胜率”必须拆成明确指标，禁止只有语义含糊的 winRate：

    FirstPlaceRate
    Top4Rate
    AvgPlacement
    BattleWinRate
    PickRate

每一条统计至少带：

    metricDefinition
    metricValue
    sampleSize
    confidence / uncertainty（来源可提供时）
    rankBracket
    region
    queueOrRuleset
    observedFrom
    observedTo
    patchRange
    sourceId

Panel 显示时必须用精确名称，例如“第一率”“前四率”“平均名次”“选取率”，不得把不同口径一律显示为“胜率”。

本地统计门控：

    当前 Set 匹配
    +
    patchRange 匹配
    +
    数据时间窗有效
    +
    分段 / 地区 / 规则口径明确
    +
    样本达到版本化最低门槛
    →
    才可作为主动建议证据

统计过期、样本不足、口径未知或版本不匹配时，仅可作为标明“历史 / 低置信度”的 Panel 参考；不得推动关键经济、追三或精确站位建议。

### 26.5 弈子、装备、强化与规则事实

OfficialRulesPack 中的版本化弈子记录至少包括：

    unitId
    localizedName
    cost
    traits
    ability
    abilityParameters
    baseStats
    set / patch
    availabilityConditions
    assetReference
    lastVerifiedAt

装备记录至少包括：

    itemId
    localizedName
    itemType
    componentRecipe
    numericEffects
    specialRestrictions
    transferSemantics
    set / patch compatibility
    sourceEvidence

强化、机制、城邦和特殊生成规则必须同样携带版本、适用范围和失效条件。

规则事实与策略知识严格分开：

    规则事实
    → 装备是什么、怎样合成、数值和限制是什么

    策略知识
    → 哪些阵容、角色和局面通常更适合它

RAG 不能把“常见推荐”伪装成“游戏规则”，也不能把规则未覆盖的未知特殊效果编造成确定结论。

### 26.6 知识包 Manifest、授权与原子更新

每个知识包必须有独立 Manifest，至少包含：

    packId
    sourceId
    sourceDisplayName
    license / authorizationReference
    attributionRequirement
    acquiredAt
    publishedAt
    validFrom
    expiresAt
    locale
    set
    patchRange
    region / rankBracket（统计包适用时）
    dataKinds
    schemaVersion
    contentHash
    signature / verificationMethod（来源支持时）

知识更新只允许：

- 用户显式启用来源后，在启动后、大厅或手动刷新执行。
- 下载独立于当前对局，不带当前局面参数。
- 先进入 D 盘 staging，校验许可、Manifest、hash、schema、Set、patch 和大小上限。
- 完成索引与自检后原子发布新的 knowledgeSnapshotId。
- 活跃 Session 固定使用开始时的 knowledgeSnapshotId；更新不会在同一局中途改变事实基础或重写历史建议。

下载失败、许可证变化、包损坏、hash 不一致或版本不兼容时保留最后一个已验证快照；若无兼容快照，则只使用本地 CompPack 和规则，不阻塞游戏。

### 26.7 本地索引与检索

首版采用“结构化过滤优先、文本检索其次、可选本地语义检索最后”的混合方式：

    Set / patch / locale / packType / applicability filter
    →
    术语、弈子、装备、羁绊、阵容标签的本地词法检索
    →
    可选本地 embedding / rerank
    →
    版本、来源、样本和前提复核

不调用外部 embedding API。若启用向量检索，embedding、索引、chunk 与缓存也只能在 D:\AlifeData\TFTCompanion\knowledge\ 下本地生成和存储。

LocalGuideRetrieval 输出的是 EvidenceChunk，而不是自由文本答案。每个 chunk 至少带：

    chunkId
    knowledgeSnapshotId
    packId
    sourceId
    patchRange
    retrievalReason
    applicability
    freshness
    confidence
    textOrStructuredFact

检索需要有界：

- 每类问题限制最大 chunk 数、字符数和处理时间。
- 不在对局关键窗口因复杂检索阻塞本地规则结论。
- 无结果或低置信度时明确返回 Unknown，不触发远程搜索。
- 不能用网页 HTML、脚本、URL 或长原文直接作为 Overlay / Voice 内容。

### 26.8 TftDataAgent：只查找与保存，不参与分析

用户明确要求 DataAgent 不要过度干涉游戏教练，因此本设计将其收紧为 TftDataAgent：一个本地、类型化、无智能仲裁的数据仓储服务，而不是分析 Agent、对话 Agent 或建议 Agent。

TftDataAgent 是 `packs\`、`dataagent\` 与 `knowledge\` 的唯一知识数据持久化写者；KnowledgeAcquisition 只能通过类型化 AcquisitionCommand / 入站候选请求它获取、校验、保存或删除，不能直接取得这些目录的写入权限。这只落实“查找与保存”的边界，不赋予 DataAgent 任何游戏分析、建议或交付权限。

TftDataAgent 只拥有以下写入职责：

- 保存已授权、已验证的知识包、Manifest、规范化规则事实、统计数据、站位模板和本地索引。
- 原子激活或回滚 knowledgeSnapshotId。
- 保存知识包版本、校验状态、删除标记和受限的来源审计。
- 删除用户关闭或清除的本地知识内容。

TftDataAgent 只拥有以下读取职责：

- 按 Set、patch、locale、packType、来源、统计口径和适用条件查找本地事实。
- 查询弈子、装备、强化、羁绊、阵容、变体、指标、模板和来源 Manifest。
- 返回带版本、来源、新鲜度、样本和适用条件的 EvidenceRecord / EvidenceChunk。
- 返回 Unknown、NotFound、Stale、Incompatible 或 PermissionDenied 等确定状态。

TftDataAgent 明确不做：

- 分析当前局面。
- 解释、追问、复盘、陪伴表达或自然语言回答。
- 给阵容、经济、追三、装备、站位或优先级打分。
- 生成 Advice、Overlay、Voice、Panel 文案或任何用户可见结论。
- 订阅 GEP 高频事件、控制任何状态机、持有 Session 或 Advice 的写权限。
- 调用 LLM、embedding API、HTTP Sidecar、外部 LLM Planner、AgentExternalRagService、Postgres 或第三方攻略实时 API。
- 读取截图、连续视觉帧、账号、麦克风、QQ 内容或任意游戏控制接口。

TftDataAgent 的 SQLite 查找、索引与 PinnedEvidenceBundle 固定只可发生在大厅、启动后、知识预取、维护操作或用户主动 RichPath 的异步路径。它可以接受类型化 DataLookupRequest，例如：

    LookupCompFacts
    LookupItemFacts
    LookupMetricFacts
    LookupPositioningTemplate
    LookupSourceManifest

活跃 FastPath / DecisionWindow 不得同步发出 DataLookupRequest、等待 SQLite、等待 DataAgent 或等待证据落账；它只能读取随 DecisionSnapshot 已冻结的内存只读 KnowledgeFactView。若该视图缺少已验证事实，策略必须返回 Unknown、使用本地 RulesPack 或安全降级，不得补查数据库或远程搜索。

TftDataAgent 返回数据，不返回判断。StrategicDecisionCoordinator、BoardStrength、经济模型和站位系统各自决定如何使用已经冻结的事实；DataAgent 不知道某条数据最终是否被采纳。

现有 Alife DataAgent 的本地 JSON 知识包、术语匹配和结构化存储模式可以作为实现参考；其分析会话、HTTP Sidecar、外部 Planner、External RAG、Postgres 与自然语言分析能力不纳入 TFT Profile。

### 26.9 RAG 防注入与内容安全

攻略内容一律视为不可信知识数据，不是控制指令。导入和检索时必须：

- 将正文、表格、标签和来源元数据分离。
- 删除或禁用脚本、HTML、嵌入指令、外链执行语义和不可验证的动态内容。
- 在任何模型上下文中明确标记为 ExternalKnowledge / UntrustedContent。
- 只允许固定的结构化输出 schema，不允许知识 chunk 调用工具、改变系统约束或扩展权限。
- 对每个输出重新验证 Set、patch、数据新鲜度、来源、前提、数值槽位和 Advice 行动边界。
- 在 Panel 中保留来源、版本和新鲜度；Overlay 与 Voice 只使用已经通过合同验证的短结论。

来源授权与署名要求是硬门槛。公开可见网页不自动等于可下载、可再分发、可建立检索索引或可商用；没有正式授权、公开 API、许可声明或用户手动导入许可时，不创建知识包。

### 26.10 D 盘、隐私、诊断与清除

知识数据按第 7 节的规范存储命名空间分区写入，不能以“知识相关”名义绕过唯一 owner：

| 数据类别 | 唯一路径与 owner |
|---|---|
| 已授权原始内容与 Manifest | `D:\AlifeData\TFTCompanion\packs\`；TftDataAgent（经 KnowledgeAcquisition 的受控入站候选） |
| 规范化事实、chunk、索引与本地 embedding | `D:\AlifeData\TFTCompanion\knowledge\`；TftDataAgent |
| 获取 / 校验 / 发布审计、Evidence Ledger、Projection、checkpoint、staging 与用户删除 tombstone | `D:\AlifeData\TFTCompanion\dataagent\`；TftDataAgent |

上述路径中的任何嵌套文件仍必须先通过 StorageRootPolicy 的最终卷、符号链接和目录联接点校验；不得把 ledger、审计或 tombstone 写入 `knowledge\`，也不得把 Pack / Manifest 写入 `dataagent\`。

默认诊断只记录：

- packId、sourceId 的脱敏引用。
- knowledgeSnapshotId、schema、Set、patch、freshness 和验证结果。
- retrieval latency、chunk 数、拒绝原因、来源健康和索引容量。

默认不记录：

- 当前 GameSnapshot。
- 原始 GEP payload。
- 截图或视频。
- 账号、匹配标识、用户查询全文。
- 原始攻略正文的大段复制。
- 外部请求 body、token 或完整 URL query。

用户可单独：

- 启用或关闭每个知识源。
- 手动刷新。
- 查看来源、许可证、适用版本和最后验证时间。
- 清除单一来源、单个 Pack 或全部本地索引。
- 切换“自动下载已启用来源”与“只允许手动离线导入”隐私模式。

清除知识包只影响未来检索；当前活跃 Advice 保留已绑定的 evidence 摘要直到其 scope 正常结束，不会重新联网或重新生成旧建议。

### 26.11 DataAgent 与游戏教练 LLM 的隔离

TftDataAgent 与未来 GameCoachLlmProfile 不共享调用链、凭据、上下文、缓存、队列、预算或故障域。

    TftDataAgent
    = 本地数据查找与保存

    GameCoachLlmGateway
    = 可选的受限远程自然语言表达

TftDataAgent 不会因为用户配置 Grok 4.5 而获得任何模型调用能力；GameCoachLlmGateway 也不会获得 TftDataAgent 的任意读写权限。专属 LLM 的完整合同见第 27 节。

### 26.12 失败、测试与成本

知识来源、索引、DataAgent 或未来 LLM 任一失败时：

    本地规则 / 已冻结 CompPack
    → 继续提供可验证建议
    → Panel 标记知识增益不可用或过期
    → 不阻塞 Advice、Overlay、语音、回合追踪或 GEP ingress

测试至少覆盖：

1. 当前对局、用户问题、截图、账号、查询、推荐结果和遥测不会出现在第三方攻略请求中。
2. 对局中不存在攻略来源网络请求；断线、检索失败或无结果不会触发在线搜索。
3. 旧 patch、错误 Set、未知地区、样本不足、许可证缺失和篡改 hash 被拒绝或降级。
4. FirstPlaceRate、Top4Rate、AvgPlacement、BattleWinRate、PickRate 不会在 UI 中混写为模糊“胜率”。
5. 新知识包原子发布；活跃 Session 的 knowledgeSnapshotId 不变；回放可复现。
6. RAG chunk 不能执行脚本、调用工具、改写系统约束或绕过数值 / Advice 门控。
7. TftDataAgent 的 HTTP Sidecar、外部 LLM Planner、External RAG、Postgres、自然语言分析和默认 Alife 路径在 TFT Profile 中被拒绝。
8. 所有知识、索引和诊断写入 D 盘；D 盘不可用不回退 C 盘。
9. 用户关闭来源或清除知识包后，后续检索不能使用已删除内容。
10. GameCoachLlmProfile 处于 Disabled、缺少有效专属凭据、未通过 endpoint / TLS PoC 或上下文校验失败时，不能产生任何远程模型请求。

粗略工作量：

| 子项 | 粗略工作量 |
|---|---|
| 知识包 Manifest、许可、验证、原子发布与 D 盘存储 | 2～4 人日 |
| 规则 / 统计 / 策略 / 站位知识规范化 | 3～5 人日 |
| LocalGuideRetrieval、版本过滤和可选本地索引 | 3～5 人日 |
| TftDataAgent 查找 / 保存边界、版本查询与 RAG 防注入 | 2～4 人日 |
| 隐私、删除、诊断、回放和负向网络测试 | 2～4 人日 |
| 合计 | 约 12～22 人日 |

这是知识能力与安全验证的复杂度估计，不是版本排期承诺，也不包含获得第三方授权、内容制作、完整 GameCoachLlmProfile、站位算法或完整产品 UI。

本节完成定义为：第三方攻略仅作本地知识内容、更新时机、零上传、知识分类、统计口径、版本门控、本地 RAG、TftDataAgent 的查找 / 保存边界、RAG 防注入、D 盘、删除和验收边界均已明确并获用户确认。游戏教练专属 LLM 的最小化远程上下文与 FastPath / RichPath 合同见第 27 节；在最终规格审阅前，不进入实现或版本规划。

## 27. GameCoachLlmProfile、FastPath 与 RichPath 设计（已确认）

本节记录游戏教练专属 LLM 的配置、数据最小化、FastPath 无 LLM、RichPath 可选 LLM、表达验证、费用、故障隔离和验收合同。它不会改变第三方攻略知识源的零上传边界。

### 27.1 设计选择

采用：

    GameCoachLlmProfile
    +
    preferredModelFamily = Grok 4.5
    +
    专属 GameCoachLlmGateway
    +
    最小化结构化上下文
    +
    FastPath 无 LLM
    +
    RichPath 可选远程 LLM
    +
    ExpressionContractValidator
    +
    本地模板和 Panel 降级

不采用：

- 在 FastPath、P1 风险、数值计算、实时站位或回合临界决策中等待或调用 LLM。
- 让 TftDataAgent 调用 LLM、解释局面或生成建议。
- 复用 Alife 全局 LLM、QQ LLM、普通 SpeechService、第三方攻略 Provider 的凭据、预算、队列或故障域。
- 将游戏 Profile 注册进通用 ChatActivity、ChatBot、XmlFunctionCaller、SkillService 或任何可获得工具 / 聊天历史 / 人格记忆的通用模型链。
- 把原始 GEP 流、截图、视频、账号、匹配标识、完整历史、完整敌方信息、语音、QQ 内容、诊断或本地文件直接提交给远程模型。
- 让模型直接调用 Overlay、TTS、网络工具、文件系统、Provider 或游戏控制。

Grok 4.5 是用户预期的模型族，不是硬编码的 API 名称或可用性承诺。实际配置使用 providerId、configuredModelId、endpointProfile、credentialRef 和 capabilitySnapshot；在用户配置和正式 PoC 中验证稳定 model id、区域、套餐、上下文上限、速率限制与计费。专属客户端可采用经 PoC 验证的 OpenAI-compatible 抽象，但不复用现有通用 OpenAI 客户端的 TLS、fallback、reasoning 展示或全局配置行为。

### 27.2 FastPath：没有任何 LLM 支撑

FastPath 的定义是：

    已验证本地事实
    → StrategicDecision
    → render-tft-companion-advice 内的 FastPathExpressionSkill 模板层
    → 版本化语言模板与锁定槽位
    → ExpressionContractValidator
    → AdviceCoordinator
    → Overlay / Panel / GameVoiceScheduler

FastPath 不调用：

- 远程 LLM。
- 本地生成式 LLM。
- TftDataAgent 的分析或解释能力。
- 网络搜索、第三方攻略查询、embedding、RAG rerank 或长数据库查询。

这不等于把提示语言硬编码在业务代码中。FastPathExpressionSkill 是 `render-tft-companion-advice` 内已编译、版本化、经合同验证的模板层，不是第二个独立 Skill 或第二条表达链；它维护：

    messageKey
    languageProfile
    slotSchema
    permittedVariants
    attentionPolicy
    scopeRules
    fallbackTemplates

业务模块只提交语义与锁定数值，例如：

    HIGH_COST_UNIT
    OBSERVED_COPIES
    ALERT_THRESHOLD
    GOLD_FLOOR
    ACTION

Skill 决定以何种简短、低打扰的语言表达；它不能改写数值、动作、优先级、有效期、站位或停止条件。模板选择可使用可复现的 messageId / semanticDigest 规则，不使用随机 LLM 生成。

因此 FastPath 保持已确认的：

    FastPath 表达 P95 ≤ 10ms
    +
    无网络依赖
    +
    无远程费用
    +
    数据断线时可安全降级

### 27.3 RichPath：可选的受限 LLM 表达

RichPath 的路径是：

    已验证 StrategicDecision
    +
    已冻结本地 EvidenceRecord
    +
    用户主动问题或非关键解释需求
    → AdviceExpressionRequest
    → render-tft-companion-advice 的 RichPath 表达政策
    → GameCoachContextSanitizer
    → GameCoachLlmGateway
    → StructuredExpressionDraft
    → ExpressionContractValidator
    → AdviceExpression 或 Invalid
    → AdviceCoordinator

RichPath 适用：

- 用户主动追问“为什么”“有哪些替代方案”“这套为什么不适合我”。
- 回合总结、复杂转型解释、装备 / 经济权衡。
- 赛后复盘与陪伴式解释。
- 在本地结论已经存在后，生成较自然的、符合专属 Skill 风格的长解释。

RichPath 不适用：

- P1 高风险、时效极短或错误成本高的提醒。
- 需要实时精确数值、站位像素、牌库计算或优先级抢占的决策。
- 战斗阶段仍有有效期的普通主动建议。

RichPath 超时、费用耗尽、模型不可用、熔断、上下文不合规或输出校验失败时，只有同一 `render-tft-companion-advice` 已加载且通过验证时，才可回退为其本地模板或结构化 Panel 表达；Skill 缺失、失效或无法验证时，语言通道显式 absent，不以其他硬编码文案绕过第 16 节。不等待重试而错过 DecisionWindow，不补播旧语音。

### 27.4 已确认的最小化远程上下文

用户已确认使用最小化结构化上下文。远程模型最多可接收：

- 用户当前的主动问题，长度与敏感字段经过限制。
- 当前 Session 的非身份化阶段、回合、等级、血量 / 经济区间等摘要。
- 己方阵容的角色化、最小化摘要，例如关键单位角色、关键装备类别、羁绊方向和已验证的局面前提。
- 已由本地规则产生的 StrategicDecision、允许动作、禁止动作、锁定数值槽位、风险、过期时间和不确定性。
- 已经版本过滤的本地 EvidenceRecord 摘要、来源显示名、patch、统计口径与短引用。
- 用户设定的表达风格、陪伴强度和语言偏好。

远程模型默认不得接收：

    Riot ID / 账号 / 昵称 / 匹配 ID / pseudoMatchId
    原始 GEP payload
    截图、视频、OCR 原文或连续视觉帧
    原始商店、完整牌库、完整对局历史
    原始敌方棋盘、其他玩家身份或可关联行为数据
    QQ 内容、麦克风、语音制品
    API Key、token、完整诊断、文件路径
    原始第三方攻略正文或未经裁剪的知识包

只要远程模型调用被禁用、未授权或不可用，任何上述数据都不得离开本机。

### 27.5 输出合同与不可越权边界

GameCoachLlmGateway 只接受固定输入 schema，只返回 StructuredExpressionDraft。输出至少包括：

    summary
    voiceCandidate
    overlayCandidate
    panelExplanation
    citedEvidenceIds
    uncertaintyLanguage

模型输出不能包含或修改：

- 金币底线、牌库计数、费用权重、站位格、冒险阈值等锁定数值。
- 新的游戏行动、点击、快捷键、文件或网络指令。
- Advice priority、replaceKey、scope、expiresAt、renderLease、viewGeneration 或语音调度状态。
- 未经本地 EvidenceRecord 支持的版本、统计、阵容或装备事实。

ExpressionContractValidator 必须验证：

    所有锁定槽位未被改写
    +
    EvidenceId 可追溯
    +
    版本 / patch / source 仍兼容
    +
    文本长度与通道预算合法
    +
    不含工具、输入、网络、文件或越权指令
    +
    绑定同一 adviceId、adviceRevision、semanticDigest、SkillVersion、messageKey 与锁定槽位
    +
    每个通道均具有明确的 present / absent 状态

Validator 不得把 StructuredExpressionDraft 原样转发给 AdviceCoordinator；它只能产出完整的 AdviceExpression 或 Invalid。对于 Voice，`voiceCandidate` 默认不具备 TTS 资格：只有它能由同一 Skill 的 `voiceSafe` messageKey 与可追溯的本地语义槽位 / 已验证 Evidence 摘要重建时，才可变为 VoiceSegment。任何复述或包含用户自由文本、敏感字段、QQ 内容、未经批准知识原文或其他非白名单 span 的候选都必须拒绝为 Voice absent；可继续保留已独立通过合同的 Panel 表达，但绝不把该文本发给 TTS Provider。

验证失败时丢弃模型输出，不进行“半信半疑”的文字修补；仅在同一专属 Skill 已加载且验证通过时使用其本地 fallback，否则所有语言表达保持 absent。

### 27.6 专属设置、密钥、成本与故障域

GameCoachLlmProfile 独立保存于：

    D:\AlifeData\TFTCompanion\settings\game-coach-llm-profile.json

配置只保存非秘密元数据，例如：

    profileId
    providerId
    configuredModelId
    endpointProfileId
    enabledMode
    contextPolicyVersion
    timeoutBudget
    concurrencyLimit
    dailyCostBudget
    richPathPolicy

credentialRef 只指向由 GameCoachCredentialStore 管理的匿名凭据引用；明文 API Key 不写入配置、日志、诊断、知识包、Panel、Overlay、语音文本或导出文件。凭据与预算不复用 Alife 全局、QQ 或第三方攻略来源。

GameCoachCredentialStore 是游戏教练 TTS 与 LLM 凭据的唯一 owner，并使用相互隔离的 provider / profile 命名空间。插件控制的加密凭据 blob、引用元数据和删除 tombstone 只允许位于：

    D:\AlifeData\TFTCompanion\settings\credentials\

它是 ProfileAndStorageStore 内部唯一可写的 `settings\credentials\` 子端口；ProfileAndStorageStore 只拥有非秘密 Profile、路径策略与 credentialRef，不直接读取或写入 blob、密钥材料或 tombstone。

blob 使用当前 Windows 用户的 DPAPI 保护；DPAPI 的系统密钥材料可能由 Windows 自身维护在用户配置文件中，但这不是插件可控写入，插件不得写入 C 盘、Windows Credential Manager、AppData 或旧 Alife 配置路径。DPAPI 不可用、解密失败、路径校验失败或凭据引用无效时，对应 Provider 保持 Disabled。credentialRef 不得包含明文、绝对路径、endpoint、Token 或可导出的秘密。

专属客户端必须：

- 使用默认系统 TLS 证书校验；禁止接受任意证书或绕过证书错误。
- 默认 fallback = Disabled；不得因 408、429、5xx、网络错误或超时，将同一请求静默转发给第二个 endpoint、模型、账号或 Provider。
- 若允许重试，只能在同一 provider、同一 endpoint、同一 profile 且 remaining deadline 仍足够时进行一次受控重试。
- 不请求、不展示、不持久化 provider reasoning / thinking 字段；只接受最终的严格结构化表达草稿。
- 在密钥存储方案尚未完成专属安全审阅前保持 Profile Disabled；不得用 Alife ConfigurationSystem、普通 JSON 或角色配置保存 API Key。

推荐 enabledMode：

    Disabled
    → 仅本地 FastPath / Panel

    UserQuestionAndReview
    → 仅用户主动追问与赛后复盘使用 RichPath

    CompanionRichPath
    → 在注意力预算允许时加入非关键陪伴解释

初始默认必须为 Disabled。只有专属安全审阅完成、凭据有效、endpoint 与 TLS 验证通过且用户显式启用后，UserQuestionAndReview 才是推荐的已启用模式；仍不得自动对每个 DecisionWindow 调用远程 LLM。正常并发最多一个 RichPath 请求；P1、FastPath 和游戏语音抢占不等待它。

### 27.7 隐私、审计、测试与完成定义

远程调用审计只记录：

- profileId、configuredModelId 的脱敏标识。
- contextPolicyVersion、请求类别、字符 / token 桶、延迟、费用桶、结果状态和拒绝原因。
- 不记录完整 prompt、完整响应、原始游戏事实、账号或凭据。

测试至少覆盖：

1. FastPath 在网络断开、LLM 未配置、限流和异常时仍无 LLM 调用，并满足本地表达延迟预算。
2. TftDataAgent 只接受查找 / 保存请求，不能触发 LLM、解释、Advice、Overlay 或 Voice。
3. GameCoachContextSanitizer 不会发送身份、原始 GEP、截图、完整历史、QQ、密钥或原始敌方信息。
4. RichPath 只能在配置的 enabledMode、用户授权、预算和有效期内请求。
5. LLM 输出不得改写锁定数值、行动、优先级、scope、版本事实或视觉坐标。
6. 远程超时、失败、熔断或费用耗尽时只回退本地表达，不重试过期建议或阻塞语音。
7. 诊断、设置、GameCoachCredentialStore 的插件控制 blob、凭据引用和预算状态均不回退 C 盘；DPAPI 不可用时保持 Disabled。
8. 无效 TLS 证书、未允许 endpoint、未知 Provider、fallback endpoint、reasoning 字段或试图转发完整 prompt 的响应路径均被拒绝并审计。

本节完成定义为：FastPath 无 LLM、专属表达 Skill 的模板边界、RichPath 可选模型职责、Grok 4.5 配置抽象、最小远程上下文、DataAgent 与 LLM 隔离、锁定输出合同、D 盘、费用、故障与测试边界均已明确并获用户确认。下一步继续讨论 Overlay 其余窗口工程、全系统回放 / 故障注入 / 性能预算与最终一致性复核；在最终规格审阅前，不进入实现或版本规划。

## 28. TftDataAgent 原生证据链与维护状态设计（已确认）

本节记录 TftDataAgent 为知识包、索引和版本化查询保存的原生证据链。目的不是让 DataAgent 分析游戏，而是让维护者可重建“数据从哪里来、经过哪些真实校验、为何被接受或拒绝、当前快照为何使用它、一次本地查找依赖了哪些事实”。

### 28.1 定位与严格边界

采用：

    TftDataEvidenceLedger
    +
    单写者追加式原生事件
    +
    可重放的状态 Projection
    +
    内容 hash、父子关系、版本快照
    +
    有界保留、检查点和删除 tombstone
    +
    D 盘 SQLite WAL 短事务

不采用：

- 记录原始 GEP、当前 GameSnapshot、账号、匹配标识、截图、视频、语音、QQ 内容或玩家行为。
- 记录 LLM prompt、LLM reasoning、完整模型响应或让 LLM 解释证据链。
- 用证据链替代 Strategy、Advice、Overlay、Voice 或语音播放状态机。
- 无界文本日志、工作目录日志、C 盘临时文件、云端审计或第三方遥测。
- 让 Panel、LLM、策略模块或外部来源直接修改 ledger。

记录域严格分离：

    TftDataEvidenceLedger
    = 知识包与本地数据流程

    Strategy / Advice audit
    = 决策和 Delivery 生命周期

    GameCoachLlm audit
    = 脱敏的远程表达调用桶

不同记录可通过 ID 引用，但不得互相复制敏感载荷。

### 28.2 原生证据关系与状态机

知识数据的真实链路为：

    SourceDescriptor
    → Acquisition
    → StagedArtifact
    → ManifestValidation
    → CanonicalRecord
    → IndexBuild
    → KnowledgeSnapshotPublished
    → TypedLookup
    → EvidenceRecordReturned
    → Superseded / Retired / Deleted

每条边都是事实关系，不是模型生成的解释。任何已返回的 EvidenceRecord 都应可反向追到：

- 来源、许可证、Pack、Set、patch、locale 与内容 hash。
- 获取、验证、规范化、索引和发布的真实时间与结果。
- 接受、拒绝、过期、替换、删除或降级的 reasonCode。
- 对应的 knowledgeSnapshotId、indexVersion 和父 EvidenceRecord。

每个 Pack 使用：

    Discovered
    → Staged
    → Validated
    → Canonicalized
    → Indexed
    → Published
    → Superseded / Retired / Deleted

失败路径必须留下 Rejected 或 Failed 与明确 reasonCode；禁止“失败后静默忽略、之后又不明原因可用”。

### 28.3 Ledger Entry 与完整性字段

每条 TftDataEvidenceLedgerEntry 至少保存：

    ledgerSequence
    eventId
    operationId
    attemptId
    causationEventId
    writerEpoch
    storeEpoch
    occurredAtMonotonic
    wallClockDiagnostic

    eventKind
    evidenceId
    parentEvidenceIds
    packId
    knowledgeSnapshotId
    schemaVersion

    sourceId
    sourceArtifactHash
    canonicalPayloadHash
    indexVersion
    stateBefore
    stateAfter
    reasonCode

    prevEntryHash
    entryHash

字段规则：

- ledgerSequence 只能由 TftDataAgent 单写者分配。
- operationId 标识一次导入、验证、索引、发布、删除或证据固定操作；同一操作的重试必须使用新的 attemptId。
- causationEventId 只在某个原生 Entry 确实触发后续操作时填写；无因果关系不得伪造关联。
- storeEpoch 标识当前 DataAgent 持久化运行代际；ledgerSequence 才是持久化顺序权威。
- parentEvidenceIds 建立来源、规范化、索引、发布和查找结果的有向关系。
- sourceArtifactHash 指向已授权且已保存在 `packs\` 的原始包；ledger 不重复长正文。
- canonicalPayloadHash 指向规范化的规则、统计或模板事实。
- reasonCode 必须来自版本化枚举，例如 LicenseMissing、HashMismatch、PatchIncompatible、SampleTooSmall、Published、Superseded、DeletedByUser。
- prevEntryHash 与 entryHash 用于发现断链、乱序、篡改或错误恢复；它不是外部公证或区块链承诺。

“原生”指真实输入、真实验证结果、真实状态转换和真实 hash。不得把“模型认为可信”“模型总结的原因”写成证据链事实。

### 28.4 类型化查找与最小化查询链

TftDataAgent 只接受类型化查找：

    LookupCompFacts
    LookupItemFacts
    LookupMetricFacts
    LookupPositioningTemplate
    LookupSourceManifest

普通 Lookup 只在内存返回本地事实，不产生 ledger 写入。只有调用方明确请求将一次真实、本地查询固定为可维护的 PinnedEvidenceBundle 时，才产生 LookupStarted / LookupReturned 原生 Entry。

PinnedEvidenceBundle 只保存：

    lookupId
    operationId
    attemptId
    callerKind
    knowledgeSnapshotId
    querySchemaVersion
    nonSensitiveFilterDigest
    resultEvidenceIds
    resultState
    indexVersion
    elapsedBucket

允许记录的非敏感筛选信息包括 Set、patch、locale、实体 ID、packType、统计口径和来源 ID。禁止记录：

- 用户原始自然语言问题。
- 当前棋盘、商店、金币、血量、牌库、敌方数据或完整 GameSnapshot。
- 账号、匹配标识、Riot ID、用户行为或语音。
- 任意 LLM prompt、reasoning、聊天、QQ 内容、截图或原始攻略请求。

PinnedEvidenceBundle 只能由 TftDataAgent 在成功返回真实本地记录后创建；下游模块不能传入一串“我用了这些证据”要求 DataAgent 盖章。它可诊断“为何某弈子查不到”“为何某统计是 Stale”，但不足以重建完整 GameSnapshot、账号身份、完整用户问题或完整对局过程；不得通过连续记录当前局面相关筛选值来尝试重建它们。entity ID 仅限静态公共规则 / 知识实体，绝不记录棋盘格、数量、商店、金币、血量、对手或连续查询轨迹语义。

### 28.5 Projection、重放与维护权限

Ledger 是追加式事实源。为避免维护界面每次扫描完整历史，TftDataAgent 在同一 SQLite WAL 短事务中维护：

    LedgerEntry
    +
    EvidenceStateProjection
    +
    KnowledgeSnapshotProjection
    +
    PackValidationProjection
    +
    LookupDependencyProjection
    +
    RetentionProjection

异常恢复使用：

    最后有效 LedgerCheckpoint
    → 校验 sequence、hash、父引用和状态转移
    → 重放之后的 LedgerEntry
    → 重建 Projection

维护 Panel 对 Projection 和有限证据详情只读。它只能发出：

    RefreshRequested
    DisableSourceRequested
    DeletePackRequested
    VerifyPackRequested

这些是命令，不是数据库写权限。TftDataAgent 验证成功后才写入新的原生 Entry；Panel、LLM 和策略模块不能改写旧 Entry、Projection 或成功状态。

### 28.6 保留、删除、D 盘与失败降级

采用版本化 EvidenceRetentionPolicy：

- 当前 Published snapshot、其必要父证据和被活跃 Session 固定引用的 snapshot 不可清理。
- 每个来源只保留可配置数量的已验证历史版本；不能无限增长。
- Superseded、Retired 或 Rejected 内容在满足保留策略后，仅保留最小元数据、hash、reasonCode 和检查点，不复制大段授权正文。
- 用户删除来源时先删除原始内容、规范化数据和索引，再保存最小 ContentDeleted tombstone，解释引用为何不可解析。
- 超出条数、容量或时间预算时，先生成 LedgerCheckpoint，再安全裁剪没有活跃引用的旧 Entry。

所有 ledger、Projection、checkpoint、staging 和维护诊断只写：

    D:\AlifeData\TFTCompanion\dataagent\

D 盘不可用、SQLite 损坏或事务失败时：

    PersistUnavailable / IntegrityDegraded
    →
    不声称证据链已保存
    →
    不回退 C 盘
    →
    不发布新的持久化 knowledgeSnapshotId
    →
    实时游戏核心继续使用仍在内存且可验证的本地规则

ledger 故障不能阻塞 AdviceCoordinator、Overlay 撤回、GameVoiceScheduler 或 GEP ingress。

### 28.7 验收与成本

验收至少覆盖：

1. 任一 Published EvidenceRecord 可回溯到来源、Manifest、许可、hash、规范化、索引和 knowledgeSnapshotId。
2. Rejected、Stale、Incompatible、Deleted、Superseded 都有真实 reasonCode 和合法状态转移。
3. 普通 Lookup 不产生无用写盘；需要维护追溯时，PinnedEvidenceBundle 与 knowledgeSnapshotId、indexVersion 和 EvidenceRecord 对应，且不记录原始游戏局面或用户问题。
4. 重启后可由 checkpoint 和 ledger 重建 Projection；断链、乱序、hash 不符或父引用缺失进入 IntegrityDegraded。
5. Panel 无法直接篡改 ledger 或 Projection；维护操作只能产生新的原生事件。
6. 内容删除会移除原始授权内容和索引，仅保留最小 tombstone。
7. D 盘不可用时不写 C 盘、不伪造成功且不阻塞实时游戏链。
8. ledger 不含 LLM prompt、reasoning、账号、GameSnapshot、截图、QQ 内容或第三方攻略请求载荷。
9. operationId、attemptId、causationEventId、storeEpoch、ledgerSequence 和状态转移能够区分重试、恢复、重复命令和真实完成；不能把半完成操作伪装为成功。
10. FastPath 不等待 TftDataAgent、SQLite 落账、PinnedEvidenceBundle 或 RAG；证据固定失败只影响需要可追溯来源的 Panel / RichPath 解释，不阻塞关键本地规则提示。

粗略工作量：

| 子项 | 粗略工作量 |
|---|---|
| Ledger schema、hash 链、状态机与单写者 | 2～3 人日 |
| Projection、checkpoint、重放与完整性检查 | 2～3 人日 |
| 保留、删除、容量与 D 盘降级 | 1～2 人日 |
| 维护只读视图、故障注入与回放测试 | 2～3 人日 |
| 合计 | 约 7～11 人日 |

## 29. 全系统确定性回放、黄金场景、故障注入与性能验收（已确认）

本节把第 22 至第 28 节的生命周期、隔离、隐私和安全降级合同放进同一套可重复验证的框架。它不是“录制真实玩家对局”的功能，也不授权默认收集游戏数据；其目的是在合成、脱敏、可控的输入下，证明系统不会因迟到回调、缩放、断线、存储或外部服务故障而误导用户、复活旧内容或写错位置。

### 29.1 已确认的验证分层与边界

采用四层互补验证，而非把其中任意一层夸大为全部真实环境保证：

    1. 状态机 / 属性测试
       → 纯领域状态、幂等、epoch、deadline、合法转移和不变量

    2. Replay Capsule 集成回放
       → 受控 fake adapter 下走真实 Reducer、Coordinator、Scheduler 与 gate

    3. 合成黄金视觉与实际 IPC 集成
       → 验证棋盘投影、render lease、Overlay receipt 和 Loopback 行为

    4. 自定义局 smoke test 与长时 soak
       → 仅人工、只读、无自动操作地验证真实 Overwolf / Windows 环境

第 1、2 层证明给定规范化输入时的领域正确性；第 3 层证明进程边界、渲染和视觉合同；第 4 层发现真实运行时兼容性问题。它们不能相互替代。

明确不将下列内容作为默认回放输入或黄金制品：

- 原始 GEP payload、完整 GameSnapshot、玩家账号、Riot ID、昵称或匹配标识。
- 真实截图、连续画面、视频、OCR 结果、音频波形或 TTS 制品。
- 商店、完整棋盘、完整牌库、完整敌方信息、用户提问、QQ 内容或玩家行为历史。
- LLM prompt、completion、reasoning、API key、Cookie、Header、URL query、绝对路径、异常全文或 stack trace。

仓库中的测试夹具只能是人工构造、匿名、最小化的语义数据。用户未来若显式导出一次诊断或回放样本，只能写入：

    D:\AlifeData\TFTCompanion\diagnostics\replay\

且必须经过独立的脱敏 schema、大小上限、保留期和显式删除；不默认采集、不自动上传、不回退 C 盘。

### 29.2 Replay Capsule 合同

每个可复放场景定义为一个版本化 TftReplayCapsule：

    TftReplayCapsule
    ├─ ReplayManifest
    │  ├─ caseId / schemaVersion / replayContractVersion
    │  ├─ buildVersion
    │  ├─ rulesetKey / rulesHash
    │  ├─ knowledgeSnapshotId / knowledgeSnapshotHash
    │  ├─ render-tft-companion-advice SkillVersion / hash
    │  ├─ FastPathTemplateSetVersion / hash
    │  └─ GameCoachLlmProfile mode
    │
    ├─ SemanticTimeline
    │  ├─ virtualMonotonicAt
    │  ├─ normalized ingress snapshot / increment / capability event
    │  ├─ session and round boundary
    │  ├─ viewport / DPI / ROI / confidence / viewGeneration event
    │  ├─ 脱敏 VoiceControlCommand / Panel command projection（仅命令种类、稳定 ID、枚举 reasonCode 与 scope）
    │  └─ injected receipt
    │
    ├─ FixtureReferences
    │  ├─ minimal rules and knowledge facts
    │  ├─ synthetic board geometry and anchor result
    │  └─ versioned strategy / expression profile
    │
    ├─ FaultPlan
    │  ├─ target adapter
    │  ├─ injection point
    │  ├─ stable faultCategory
    │  └─ expected degradation / recovery
    │
    └─ ExpectedOutcome
       ├─ typed transition assertions
       ├─ forbidden side-effect assertions
       ├─ canonical outcome projection hash
       └─ first-divergence diagnostic contract

回放中，外部语义时间线以 Host 的 virtual monotonic time 加 hostIngressSequence 为权威；进入 AdviceCoordinator 后必须记录并按 coordinatorSequence 复现最终状态转移与冲突胜者。墙钟只可作为人类诊断字段，不得参与跨进程的事实排序、deadline、重试或回放结果。

同一 Capsule 必须固定 Rules、Knowledge、Expression Skill、Profile、随机种子和 Adapter 行为。活跃对局中知识包更新不应改变已经固定的 knowledgeSnapshotId；回放同样不得因本机当前包版本不同而悄然改变预期。

### 29.3 专属回放内核与受控边界

TFT 不直接复用现有 DataAgent / QChat 的审计、会话、字符串 marker 或 wall-clock task runner。可借鉴“固定夹具 + 真实核心链路 + fake 外部依赖 + fail-closed 差异门”的模式，但必须建立专属回放内核：

    TftReplayHarness
    ├─ VirtualMonotonicClock
    ├─ DeterministicTimerScheduler
    ├─ fixed RandomSource / deterministic IDs
    ├─ FakeGepIngress and FakeBridgeTransport
    ├─ FakeRendererReceiptPort
    ├─ FakeViewportAnchorDetector
    ├─ FakeGameCoachTtsProvider
    ├─ FakeCoachPlaybackAdapter
    ├─ FakeKnowledgePackStore / FakeTftDataEvidenceLedger
    ├─ FakeGameCoachLlmGateway
    └─ real LiveStateReducer
       + real StrategicDecisionCoordinator
       + real AdviceCoordinator
       + real GameVoiceScheduler
       + real Viewport confidence gate

回放中不能让 ThreadPool 调度顺序、Task.Delay、真实网络、随机 UUID、真实 TTS、真实 LLM 或真实窗口时序决定领域结果。每一个异步外部回调都必须携带并复检其身份边界：

    runtimeEpoch
    bridgeInstanceId / transportEpoch / connectionEpoch
    sessionId / sessionEpoch / roundKey
    ingressStateSnapshotId / decisionSnapshotId / stateRevision
    adviceId / adviceRevision / deliveryId / attemptId
    viewGeneration / renderLeaseId
    timerGeneration

回放框架至少为下列边界提供可注入接口：IGepIngress、ICompanionBridgeTransport、IRendererReceiptPort、IViewportAnchorDetector、IGameCoachTtsProvider、ICoachPlaybackAdapter、IKnowledgePackStore、ITftDataEvidenceLedger、IGameCoachLlmGateway、IMonotonicClock、ITimerScheduler 和 IRandomSource。

### 29.4 强类型结果、稳定散列与首个分歧点

Replay 的主要断言是强类型 ExpectedOutcome，而不是“报告文本包含某个字符串”。至少包括：

    ExpectedStateTransition
    ExpectedGapOrResync
    ExpectedAdviceCurrentRevision
    ExpectedDeliveryTerminal
    ExpectedRenderReceipt
    ExpectedVoiceOutcome
    ExpectedDegradeState
    ExpectedNoSideEffect

每次回放产生一个有序的 CanonicalOutcomeProjection，并计算 OutcomeProjectionHash。散列只覆盖稳定业务事实：

- session、round、capability、state revision、Gap / Resync 和健康状态。
- StrategicDecision 的结构化身份、Advice revision、replaceKey、semanticDigest、优先级、scope 和关闭原因。
- 每个 Delivery 的真实 receipt、终态、Overlay 显示 / 撤回、Voice 启动 / 中断 / 过期事实。
- Panel projection、knowledgeSnapshotId、版本化 Skill / Profile 引用、reasonCode 和可允许的降级状态。

散列明确排除 wall-clock、设备 GUID、音频字节、日志行号、运行实例 ID、绝对路径、异常原文和 LLM 自然语言措辞。它用于快速发现回归，不能替代字段级断言；发生差异时必须输出脱敏的第一条不一致事件、前后状态摘要、身份代际和 reasonCode，而不是输出原始载荷。

### 29.5 全系统安全降级 Oracle

“不误导”必须由所有黄金场景共享的门控合同保证，而不是依赖文案提醒。精确棋盘内格、箭头和像素标签仅在以下条件全部满足时允许显示：

    Current Advice revision
    + current session / round / render lease
    + current viewGeneration
    + OverlayHighConfidence
    + no gap and fresh realtime facts
    + compatible knowledgeSnapshot
    + non-expired TTL

任一条件失效时使用下列 Oracle：

| 失效条件 | 可以保留 | 必须禁止 |
|---|---|---|
| GEP sequence gap、重同步中或实时事实过期 | 明确标示不确定的历史 / 通用 Panel 解释 | 精确站位、精确牌库、追三计数和基于残缺事实的高确定性建议 |
| viewGeneration 变化，或定位为 Medium / Low | Edge Dock 的方向性提示与 Panel | 棋盘内精确格子、移动箭头、像素标签 |
| Advice 被 supersede、scope 结束或 TTL 到期 | 新 revision 与最小审计事实 | 旧 Panel / Overlay 恢复，旧语音开始或补播 |
| TTS 或 LLM 不可用 | 本地 FastPath、Panel 与 Overlay | 等待远程服务后补播过期消息，或让外部服务改写决策 |
| D 盘不可写 | 内存中仍可验证的实时核心 | 回退 C 盘，伪造“已保存 / 已发布” |
| 知识包过期或 patch 不兼容 | 明确标记为历史参考的内容 | 把它当作当前版本 P1 / P3 关键策略事实 |

必须具有“僵尸回调穷举”属性测试：在每个异步状态转换点，故意让旧 attemptId、viewGeneration、renderLeaseId、sessionEpoch、runtimeEpoch 的 completion 在新状态建立后返回。唯一允许结果是被忽略，或以 late / obsolete 的脱敏事实终态化；它绝不能改变 Current 状态、开始播放、显示箭头、关闭新 Delivery 或越过用户控制。

### 29.6 首批端到端黄金场景

下表的 P0 失败意味着不得进入自用验收或后续版本范围讨论；P1 是核心可靠性与体验门槛；P2 留作长期兼容性与回归覆盖。这里的 P0 / P1 / P2 是测试严重级别，不是第 22 节 Advice priorityClass 的 P1 至 P5。每一场景均使用合成、匿名语义输入。

| ID | 场景与输入 | 必须观察到 | 绝不允许 | 优先级 |
|---|---|---|---|---|
| G-01 | 冷启动，磁盘上存在上次异常退出留下的非终态记录 | 新 runtimeEpoch；先 HideAll / 失效旧 lease；收到当前局新快照后才可创建新 Advice | 自动重播旧语音、恢复上局箭头或把旧 Panel 当作当前结论 | P0 |
| G-02 | 正常备战期，GEP 完整、知识兼容、定位 High | Bridge → Reducer → Strategy → Advice → Panel / Overlay / 可选 Voice；BoardCellId 正确投影且收到真实 OverlayShown / Delivered | 仅因命令已发送就标为视觉成功；业务层直接写像素坐标 | P0 |
| G-03 | 同一 replaceKey 内连续状态变化，旧 Advice 已在渲染或合成准备 | adviceRevision 和 semanticDigest 正确替换；旧 Pending Voice 取消；旧 render lease 失效 | 旧 ACK、旧 TTS 制品或旧 render 在新建议之后复活 | P0 |
| G-04 | 游戏缩放、UI 缩放、窗口 / DPI / 显示器变化或 ROI 漂移 | IncrementViewGeneration → HideExactBoardInlay → Reacquire；仅 High 后重画，Medium / Low 保留 Dock / Panel | 缩放后旧箭头仍贴在错误格位；低置信度画精确箭头 | P0 |
| G-05 | streamSequence 缺口、ring overflow、Bridge 重启、snapshot 不匹配或 feature health 降级 | GapDetected → ResyncRequired → getInfo snapshot → 新 state revision；实时依赖建议降级 | 猜测补齐缺失事件；基于残缺牌库 / 棋盘继续给精确结论 | P0 |
| G-06 | 回合结束、对局结束或新局切换，旧 TTS / LLM / Render 回调晚到 | 旧 sessionEpoch、roundKey、attemptId、viewGeneration 均失效；旧内容终态化 | 上局语音在新局播放；上局 Overlay 在新局显示 | P0 |
| G-07 | 本地已验证的高费弈子接近三星等 FastPath 事件 | 本地规则加 `render-tft-companion-advice` 内 FastPath 模板层在零 LLM、零 DataAgent、零网络下产生短提示；语音失败时视觉继续 | FastPath 调用 Grok、RAG 或外部网络，或因远程慢错过窗口 | P0 |
| G-08 | MuteUntil、SkipCurrentAdvice、CancelAutomaticQueue、RequestOneTimeReadAloud | 命令经 VoiceControlCommand 进入 Scheduler；Mute 不影响 Panel / Overlay；Skip 不使同 revision 再播 | UI / 热键直接操控播放器；静音后仍自动发声；旧内容稍后补播 | P0 |
| G-09 | TTS 超时、取消后仍返回、设备丢失或 outputReleased 不返回 | latestUsefulVoiceStart 到期即丢弃；迟到制品按 attempt 隔离；资源释放故障使 Voice 降级 | 过期音频播放、无限重试，或把 QQ TTS 当游戏语音 fallback | P1 |
| G-10 | Overlay Renderer 或 render 通道断线、重建、lease 失效 | 精确棋盘层撤回；Delivery 真实终态化；重连先 HideAll，后仅投递仍 Current 内容 | 断线后保留可能错位箭头；重连自动恢复旧 revision | P0 |
| G-11 | 大厅 / 启动后导入知识包，校验、索引失败、重复操作或崩溃恢复 | staging → validation → normalize → index → publish 原子切换；失败保留旧有效 snapshot | 半完成包被激活；旧 patch 统计伪装为当前；DataAgent 产出推荐 | P1 |
| G-12 | 普通 Lookup 与 PinnedEvidenceBundle 查找 | 普通查找不写无用账；Pinned evidence 可回溯 source / manifest / hash / index / snapshot | 当前棋盘、用户问题、LLM prompt 或策略结论写入 DataAgent ledger | P1 |
| G-13 | 用户主动 RichPath 追问，Grok 超时、迟到、越权或预算耗尽 | 本地已有结构化结论；LLM 仅受限表达；Validator 通过才交付，失败回到本地表达 | LLM 修改金币阈值、行动、优先级、站位格，或补播过期建议 | P1 |
| G-14 | D 盘只读、满盘、SQLite 失败或根路径异常 | PersistUnavailable / IntegrityDegraded；不发布新的持久快照；实时撤回与 FastPath 仍可运行 | 向 C 盘或默认 Alife 路径写入；把失败记为已保存 | P0 |
| G-15 | 对局中网络审计，分别触发攻略源、DataAgent、RichPath 与独立 TTS；另注入含用户问题复述的 RichPath voiceCandidate | 攻略包只在启动后 / 大厅 / 手动刷新下载；对局中攻略源零上传；RichPath 只走授权专属通道；TTS 仅发送白名单 VoiceSegment；含用户自由文本的 voiceCandidate 被拒绝为 Voice absent | GameSnapshot、截图、RAG query、账号、原始敌方数据、用户问题或诊断载荷发往攻略平台或 TTS Provider | P0 |
| G-16 | 大 snapshot、事件 burst 与 P1 Hide / Invalidate 同时发生 | ingest 有界、低价值增量可合并；render 仍优先处理撤销；内存和落盘队列有界 | 大包阻塞精确箭头撤回或阻塞 GEP callback | P1 |
| G-17 | 长时运行、休眠恢复、多回合和多次缩放 | 休眠期间过期 Voice 不补播；恢复后仅由新 snapshot 创建 Advice；资源 / 句柄无泄漏 | 旧 timer、deadline、Provider callback 污染恢复后的状态 | P1 |
| G-18 | 校准、取消校准或超时 | Overlay 始终鼠标穿透；校准只在独立 Panel 预览中完成 | Overlay 点击落入游戏，或插件模拟游戏输入 | P0 |

### 29.7 故障注入矩阵

每项故障至少在状态机 / 属性测试、确定性回放、实际 IPC / 桌面集成中的一个层级验证；P0 项至少覆盖两个层级。异常信息以稳定 faultCategory 和 reasonCode 表示，不保存原始异常文本。

| 故障域 | 注入方式 | 期望安全结果 |
|---|---|---|
| GEP / Bridge | 重复、乱序、缺序、旧 bridgeInstanceId、旧 transportEpoch、feature health 降级 | 幂等去重；旧消息无效；缺口强制 Resync，不猜事实 |
| GEP / Bridge | 超大 payload、schema 不兼容、非法 role / token / Origin、超频 | 拒绝并记录脱敏 reasonCode；不拖垮 Host，不扩大权限 |
| IPC | 单向断线、ACK 丢失或重复、重连、Bridge / Host 轮流重启 | 至少一次传输加应用幂等；重连先清视觉，后仅重建 Current 状态 |
| LiveStateReducer | snapshot 与增量不一致、round / session 跳变、Reducer 异常 | 不产生精确决策；进入明确降级 / 重同步，不做部分提交 |
| AdviceCoordinator | 并发 supersede、重复 semanticDigest、scope 到期、用户软接管 | 单写者保持 Current revision；旧 Delivery 不复活、不重播 |
| ViewportTracker | DPI / 窗口 / 内部缩放、遮挡、棋盘短暂不可见、过渡动画、迟到 High | 先隐藏精确层；仅 High 恢复；错误 High 不得驱动箭头 |
| Overlay Renderer | lease 失效、Renderer 崩溃、Accepted 未显示、显示后遮挡 | Accepted ≠ Shown ≠ Delivered；未可见不计成功；精确层可撤回 |
| GameVoiceScheduler | Provider 卡死、取消不响应、late artifact、设备丢失、release 超时 | 强制终态化或通道降级；旧制品丢弃；不补播、不阻塞其他通道 |
| Voice 控制 | Mute / Skip 与 P1 或新 revision 并发 | 用户控制优先且幂等；Mute 覆盖自动播报；新内容重新仲裁 |
| 知识包 | hash、签名、许可证、patch 不匹配，索引损坏、激活中崩溃、重复导入 | 不激活半成品；保留最后已验证 snapshot；证据链留下真实失败原因 |
| TftDataEvidenceLedger | SQLite 锁、WAL / I/O 异常、checkpoint 中断、hash 链断裂 | IntegrityDegraded；不伪造证据或持久化成功；FastPath 不等待它 |
| RichPath LLM | 无网络、超时、限流、预算耗尽、prompt 注入、越权输出或迟到 | 仅回退本地表达；拒绝越权草案；不记录 prompt / reasoning；不补播 |
| 存储 | D 盘拒绝权限、满盘、根路径错误、默认 C 盘路径可见 | 所有 TFT 可控写入 fail-closed；绝不 C 盘 fallback；内存核心继续 |
| 时间 / 资源 | 墙钟跳变、休眠恢复、GC / CPU 长暂停、线程池拥塞 | 仅单调时钟裁决 deadline；休眠后过期内容不恢复；单次事件循环有上限 |
| 隐私 / 网络 | 对局中抓取出站连接、知识源刷新、未授权 LLM 调用、TTS 出站载荷 | 攻略源无上传；FastPath 零 LLM；RichPath 仅有已授权最小化上下文；TTS 仅有白名单 VoiceSegment 与受限配置 |

建议的稳定故障类别至少包含：TransportDisconnected、SequenceGap、RingBufferOverflow、SchemaRejected、SnapshotUnavailable、ViewportConfidenceLost、RendererAckTimedOut、OverlayResourceReleaseTimedOut、TtsPrepareTimedOut、TtsCancelAckTimedOut、PlaybackStartFailed、PlaybackDeviceLost、PlaybackReleaseTimedOut、StorageUnavailable、StorageIntegrityDegraded、KnowledgeSnapshotIncompatible、LlmTimeout 和 LlmBudgetDenied。

### 29.8 FastPath、RichPath 与 shadow-diff 边界

FastPath 回放不调用 TftDataAgent、SQLite、RAG、网络、远程 TTS 或远程 LLM。它应验证：

    StrategicDecision
    → FastPathExpressionSkill
    → versioned messageKey + locked slots
    → Validator
    → AdviceCoordinator

RichPath 只能使用固定 CoachExpressionDraft 夹具、明确拒绝或超时夹具，绝不在回放中访问真实 Grok。对于可选 LLM 表达和知识解释，使用 shadow-diff gate：

    DeterministicDecision
    versus
    OptionalLlmExpression / OptionalKnowledgeExplanation

允许比较的仅是表达草案是否合规、证据解释是否覆盖和是否应提示人工审阅。任何草案不得改动 FastPath 数值、牌库门槛、经济下限、Advice priority、replaceKey、revision、精准站位格、viewGeneration 或版本 / 置信度门控。若 default deterministic result 被外部建议改变，回放必须失败关闭。

### 29.9 性能 Harness 与初始验收口径

虚拟时间回放证明正确性，不证明真实延迟。性能 Harness 必须使用真实高分辨率单调计时、合成脱敏 workload、分阶段 histogram 和 P50 / P95 / P99 / max 输出；不默认落盘连续对局数据，若保存诊断仅允许 D 盘私有路径。

初始验收目标如下。它们是本机验证口径，不是对所有硬件、Windows 合成器、Overwolf 上游或远程 Provider 的绝对承诺：

| 路径 | 初始目标 |
|---|---|
| Bridge callback 入有界内存队列 | P95 ≤ 2ms；不得阻塞 GEP callback |
| GEP 事件进入本地 Reducer | P95 ≤ 10ms；不含 Overwolf 上游延迟 |
| ingest 到 Host 接收并 ACK | P95 ≤ 15ms |
| AdviceCoordinator 状态转移 | P50 ≤ 2ms，P95 ≤ 5ms，P99 ≤ 15ms |
| 本地规则分析与候选仲裁 | P95 50～150ms |
| FastPath 表达 | P95 ≤ 10ms；零 LLM、零 DataAgent、零网络 |
| StrategicDecision 可用到 Panel 首结果 | P95 ≤ 100ms |
| Host render command 到 Bridge Accepted | P95 ≤ 15ms |
| 已验证投影下 render command 到 OverlayShown | P95 ≤ 50ms |
| 已校准、High 置信度下建议可用到精确箭头首显 | P95 ≤ 100～150ms |
| 已验证 High 的单格投影 | P95 ≤ 2ms |
| 已知窗口 / DPI / 分辨率变化后的旧精确层撤回 | 不超过一个渲染帧 |
| Overlay 根视口重新贴合游戏表面 | P95 ≤ 100ms |
| ROI 发现内部缩放 / 漂移 | 约 67～100ms |
| 内部缩放后恢复 High 精确格位 | P95 约 200～300ms；失败时继续降级 |
| Supersede 到取消未播放 Voice | P95 ≤ 20ms |
| 单次事件循环不可中断占用 | ≤ 20ms |

另设一个端到端复合预算：

    有效、完整、非 gap 的本地状态变化
    → 首个 High-confidence 视觉建议
    = P95 ≤ 250ms

该预算不含 Overwolf 上游事件延迟、内部缩放重新定位、远程 TTS 和远程 LLM。缩放情形的首要指标不是强行维持箭头，而是“先在一个渲染帧内撤回旧精确层；仅在重新获得 High 后，于约 200～300ms P95 恢复；无法恢复则维持安全降级”。

语音另行计量：外部合成时间不归因给 AdviceCoordinator；验收重点是 latestUsefulVoiceStart 前是否仍有效、取消 / 过期 / 失败后是否绝不播放、outputReleased 前是否绝不启动下一条，以及 P1 是否因不必要的等待而延迟。

### 29.10 最小发布验收门与成本边界

在进入任何自用功能版本或实施范围讨论前，以下 P0 必须能由规范化回放轨迹、受控故障注入或真实集成回执给出证据，而非人工“看起来没问题”：

1. 任意旧 runtimeEpoch、sessionEpoch、adviceRevision、viewGeneration、attempt 或 lease 的迟到回调都无法复活内容。
2. GEP gap、知识版本失配或低定位置信度下，不输出貌似精确的建议。
3. 缩放或窗口变化后，旧箭头先消失，之后才可能重新出现。
4. 重启、休眠恢复、断线重连不补播旧语音、不恢复旧 Overlay。
5. FastPath 在断网、未配置 Grok、DataAgent / SQLite 失败时仍可运行，且不调用 LLM。
6. D 盘故障绝不回退 C 盘，且不伪造持久化成功。
7. 第三方攻略源对局中零上传；TTS Provider 仅可收到白名单 VoiceSegment；DataAgent ledger 不含对局、账号、截图、LLM 内容或策略结论。
8. ingest 大包 / burst 不得阻塞 render 的 P1 Hide / Invalidate。
9. Overlay 永远鼠标穿透；不存在输入模拟或校准点击落入游戏。
10. 回放夹具、诊断和性能制品均遵守字段白名单、脱敏、D 盘根路径和有界保留合同。

这是一项专属基础设施，粗略工作量仅用于未来范围评估，不构成版本排期：

| 子项 | 粗略工作量 |
|---|---|
| VirtualMonotonicClock、DeterministicTimerScheduler、强类型 Fixture / Result | 3～5 人日 |
| Replay Harness、fake adapter、身份代际与迟到回调测试 | 3～5 人日 |
| P0 / P1 Capsule、黄金视觉夹具与 fault matrix | 3～5 人日 |
| IPC / Overlay / TTS 集成回执回放与真实自定义局 smoke 工具 | 2～4 人日 |
| 性能 histogram、隐私 / 路径审计与长期 soak 基线 | 2～3 人日 |
| 合计 | 约 13～22 人日 |

该成本不包含 Overwolf Native Runtime 兼容性 PoC、具体策略算法、第三方 TTS / LLM 接入、真实攻略知识包授权或任何游戏操作能力。回放框架永远服务于陪伴型、只读教练边界，不能成为收集真实对局、反向控制游戏或绕过平台规则的通道。

## 30. 总体架构一致性、运行拓扑与生命周期设计（已确认）

本节把第 3、6、7、22 至第 29 节已确认的组件装配为一个可关闭、可恢复、可回放的运行时。它不新增游戏功能，也不改变已经确认的物理拓扑；目的只是防止实现阶段因“方便”重新引入全局路径、QQ 语音、通用事件总线、循环依赖、C 盘回退、后台残留或高延迟等待。

### 30.1 已确认的组织选择

采用：

    一个独立 TftCompanionHost 进程
    +
    一个 TftCompanionRuntime 组合根
    +
    一个 RuntimeSupervisor 生命周期监督者
    +
    少量进程内、状态边界明确的领域
    +
    类型化 Fact / Command / Receipt / Snapshot 端口
    +
    只在单写者或隔离边界处使用有界 mailbox

保持既定物理拓扑：

    Overwolf Background Bridge
        ⇅ 双物理 Loopback WebSocket
    独立 TftCompanionHost
        ⇅ 低频 Projection / 受控命令
    Alife Side Panel
        ⇅ 声明式 render / 真实 receipt
    Overwolf Overlay Renderer

不采用：

- 把所有模块直接堆进一个大 Host 并互相调用的扁平组织方式。
- 让所有模块任意订阅、任意发布的通用事件总线。
- 将 State、Strategy、Voice、Knowledge、Overlay 分拆为多个常驻微服务或本机 broker。
- 首版新增 Sidecar、HTTP polling、文件中转、SQLite polling 或额外守护进程。

选择原因：

- 扁平大 Host 初始文件数最少，但会让关闭、重连、缩放、语音迟到回调和持久化逐渐交叉，无法证明状态唯一性。
- 多进程 / broker 方案会增加 IPC、重连、队列、日志、发布、进程残留和延迟，在用户自用、只读、陪伴型插件中属于过度设计。
- 受监督分域 Runtime 保持单 Host 的低延迟和无额外常驻进程，同时让每类状态可被单独理解、测试、故障注入和关闭。

旧 Alife 的全局 AlifePath、普通 SpeechService、DataAgent Session / Audit、通用 WebBridge 和默认路径均不能直接接入本 Runtime。未来若 Overwolf PoC 迫使采用最小 Sidecar + Named Pipe，只能借鉴“专属 Job Object 负责关闭受控子进程”的思想；不得接入旧 Alife 的全局进程、路径或语音管理。

### 30.2 最终运行拓扑与热冷路径

    TftCompanionRuntime
    │
    ├─ RuntimeSupervisor
    │  ├─ runtimeEpoch
    │  ├─ 启动 / 关闭 / 崩溃恢复
    │  ├─ D 盘根校验与全局降级
    │  └─ 可选 Sidecar 的专属 Job Object
    │
    ├─ IngressAndStateDomain
    │  ├─ IPCGateway / BridgeLinkSupervisor
    │  ├─ SessionRoundController
    │  ├─ CapabilityMatrix
    │  ├─ LiveStateReducer
    │  └─ ViewportTransformTracker
    │
    ├─ KnowledgeAndProfileDomain
    │  ├─ Rules / Pack / KnowledgeSnapshot
    │  ├─ TftDataAgent / TftDataEvidenceLedger
    │  └─ TTS / LLM / 用户偏好 Profile
    │
    ├─ DecisionDomain
    │  ├─ DecisionSnapshotBuilder
    │  ├─ Candidate 分析模块
    │  └─ StrategicDecisionCoordinator
    │
    ├─ DeliveryDomain
    │  ├─ InterventionPolicy / AdviceSemanticCompiler
    │  ├─ ExpressionContractValidator
    │  ├─ AdviceCoordinator
    │  ├─ GameVoiceScheduler
    │  ├─ Overlay render lease
    │  └─ PanelProjection Store
    │
    └─ 外部边界
       ├─ Overwolf Background Bridge / Overlay Renderer
       ├─ Alife Side Panel
       ├─ 独立游戏 TTS Provider / Playback adapter
       └─ 可选 GameCoach LLM Provider

实时热路径唯一允许为：

    Bridge
    → IPCGateway
    → LiveStateReducer
    → Capability / DecisionSnapshot gate
    → 本地策略与仲裁
    → AdviceCoordinator
    → Panel / Overlay / 可选 Voice

热路径不得等待：

- SQLite、DataAgent 查找、证据固定、知识下载、索引、清理或导出。
- 第三方攻略平台、远程 LLM、远程 embedding、RichPath 或任何外部 RAG。
- 长时间视觉搜索、完整 Replay、性能报告、日志压缩或 D 盘维护。
- 普通 QQ SpeechService、QQ 队列或其他 Alife 组件。

冷路径为：

    大厅 / 启动后 / 用户手动请求
    → 知识获取、验证、索引、ledger、保留清理、导出、诊断
    → 发布一个已验证的 knowledgeSnapshotId

冷路径只能发布后续可选用的知识快照，不能在活跃 DecisionSnapshot 中途改变事实基础。D 盘不可写时，冷路径进入 PersistUnavailable 或 IntegrityDegraded；热路径仅使用仍在内存且可验证的本地规则继续工作，且不回退 C 盘。

RichPath 是用户主动追问或复盘时的可选异步表达 lane，不属于 FastPath，也不属于会阻塞热路径的维护冷路径。它只读取当前仍有效的 DecisionSnapshot、只读 KnowledgeFactView 与最小化上下文；其 LLM / 本地检索结果必须经 Validator，并且仅在 scope、runtimeEpoch、sessionEpoch 与 adviceRevision 仍有效时才可成为新的受限表达。PinnedEvidenceBundle 的固定和任何 SQLite 写入始终异步，不能阻塞它。

### 30.3 状态唯一所有者

| 状态或事实域 | 唯一写者 | 可读取者 | 明确禁止 |
|---|---|---|---|
| runtimeEpoch、启动/关闭门、全局取消围栏、可选 Sidecar 生命周期 | RuntimeSupervisor | 全部 Host 内部域 | 业务模块自行重启 Host、杀进程或继续使用旧 epoch |
| Host 侧连接、握手、协议版本、transport epoch、连接健康 Projection、hostIngressSequence | BridgeLinkSupervisor / IPCGateway | Ingress、RuntimeSupervisor、诊断 | 策略、Panel、TTS 或 DataAgent 接收原始 WebSocket 帧或分配 ingress 序号 |
| sessionId、sessionEpoch、roundKey、会话阶段、重连归属 | SessionRoundController | Reducer、Decision、Advice、诊断 | 仅凭时间接近把旧局并入新局；Panel 或 Bridge 直接改会话 |
| 当前局游戏事实、来源、新鲜度、事实 revision | LiveStateReducer | Capability、SnapshotBuilder、分析模块 | 分析模块、LLM、DataAgent、Overlay 或 Panel 修改游戏事实 |
| 字段可用性、Schema 兼容、gap / resync、实时能力门 | CapabilityMatrix | Decision、Advice、Viewport、Panel | 仅凭攻略包、LLM 或 UI 假设字段存在 |
| 棋盘几何、viewGeneration、High / Medium / Low 置信度、投影结果 | ViewportTransformTracker | Delivery、Overlay、Panel | 策略模块自行推导像素坐标；低置信度仍请求精确箭头 |
| 已验证知识包、knowledgeSnapshotId、证据链、索引与持久化 Projection | TftDataAgent | Decision、RichPath、维护 Panel | DataAgent 生成策略、Advice、优先级、语音或 Overlay 状态 |
| 用户持久偏好、专属 TTS / LLM Profile、许可状态、D 盘根路径策略 | ProfileAndStorageStore | Runtime、Voice、LLM、Panel | 使用旧 AlifePath 默认目录；把密钥或设置传播给 QQ / 通用模型 |
| 当前回合冻结的 DecisionSnapshot 与 StrategicDecision | StrategicDecisionCoordinator | Expression pipeline、Advice、Panel | LLM、第三方攻略、分析模块或 UI 改写已仲裁决策 |
| EligibilityResult、ChannelPlan、AdviceExpression 的纯计算结果 | InterventionPolicy、AdviceSemanticCompiler、专属 Skill 与 ExpressionContractValidator | AdviceCoordinator | 直接创建 Delivery、改写 StrategicDecision 或自行标记 Advice 终态 |
| Advice、adviceRevision、replaceKey、scope、coordinatorSequence、Delivery 终态与关闭原因 | AdviceCoordinator | Voice、Render、Panel Projection、诊断 | TTS、Renderer、Panel 或分析模块自行标记 Advice 已完成或分配协调器序号 |
| 游戏插件语音队列、播放租约、语音临时控制、真实播放终态 | GameVoiceScheduler | Advice、受控 VoiceControlCommand、诊断 | QQ SpeechService、Panel 热键或 Provider 直接操控播放器 |
| Host 所期望的 render lease、Overlay Delivery 状态、Panel Projection revision | DeliveryDomain 的 AdviceCoordinator 与 PanelProjectionStore | Bridge、Renderer、Panel | Renderer 把 Accepted 当作业务交付成功；Panel 反向修改 Advice |
| Overlay 的物理窗口、鼠标穿透、实际 Show / Hide、可见性事实 | Overwolf Bridge / Overlay Renderer | Host 通过真实 receipt 读取 | Host 假定已显示；Renderer 读取 GEP、做策略或接收游戏点击 |
| 脱敏、有界的运行诊断 Projection | DiagnosticsSink | 维护 Panel、Replay Harness | 诊断系统反向影响实时决策或保存原始载荷 |

三个不可混淆的边界：

1. AdviceCoordinator 是交付语义唯一写者。OverlayShown、PlaybackStarted、DeviceLost 等是事实回执；只有 AdviceCoordinator 可以把父 Advice 或 Delivery 标记为 Delivered、Interrupted、Superseded 或其他终态。
2. ViewportTransformTracker 只回答“能否可信地把逻辑格位投影到屏幕”，不决定“应该让哪个弈子站哪里”。策略格位和像素投影绝不互相污染。
3. TftDataAgent 可以提供固定知识事实，但不拥有当前局，也不位于 FastPath 等待链上。当前回合只使用被冻结的 knowledgeSnapshotId。

### 30.4 生命周期阶段、健康度与关闭顺序

不把生命周期阶段和故障 / 能力健康度混成一个巨大的枚举。采用三条正交状态：

    RuntimePhase
    + SessionPhase
    + Health Facets

RuntimePhase：

    Stopped
      → Bootstrapping
      → Ready
      → Stopping
      → Stopped

- Bootstrapping：生成新的 runtimeEpoch，验证 D 盘私有根，加载已验证的设置、规则和知识快照；可恢复 DataAgent 维护状态，但绝不恢复旧 Advice、旧语音或旧 Overlay。
- Ready：Host 可等待 Bridge 双通道连接或停留在大厅 / 无游戏状态；没有完整 snapshot 前不得生成实时建议。
- Stopping：拒绝新 ingress 进入决策，失效所有实时 scope，撤回 Overlay，取消自动语音，完成有界清理。
- Stopped：没有可继续生效的 render lease、自动补播语音、挂起实时任务或不受监督的子进程。

SessionPhase：

    Idle
      → Starting
      → Active ───────↔ Suspended
      ↓                    ↓
    Ending ←──────── Interrupted
      ↓
    Finalized
      → Idle

RecoveredPartial 不再作为 SessionPhase 的平行主状态，而是 Starting 的恢复处置标签：

    Starting(Fresh)
    或
    Starting(RecoveredPartial)

- Starting(Fresh)：当前 snapshot 提供了足够的新 sessionId、sessionEpoch 和能力事实。
- Starting(RecoveredPartial)：Host 重启后，或同一 Host 内无法可靠保留原会话但当前 snapshot 仍支持保守启动时使用；它不继承旧 Advice / Voice / Overlay，并生成新的 sessionEpoch。若同一 Host 的 Bridge 重连能可靠证明仍为同一逻辑会话，则走 Suspended → Active，而不是 Starting(RecoveredPartial)。
- Active：可按当前 CapabilityMatrix 生成建议；精确站位仍需 Viewport High。
- Suspended：出现 gap、snapshot 失配、关键事实过期或会话身份暂时无法验证；所有实时 Advice 过期，未播自动语音取消，精确 Overlay 撤回，等待 ResyncRequired 与新 snapshot。
- Ending：收到 match end、确认的新局身份、用户停止插件或需要终止当前会话；先关闭实时 scope，再做冷路径收尾。
- Interrupted：无法可靠证明仍是同一局，或恢复失败；禁止仅靠时间接近重新拼接。它只表示进入强制收尾的原因，必须立即建立旧 scope / Voice / render lease / Delivery 的失效围栏并转入 Ending，随后按同一条 Ending → Finalized → Idle 链完成；新的 Starting(Fresh 或 RecoveredPartial) 只能在该实时失效围栏已经建立后创建。
- Finalized：完成当前 session 的内存清理和有界、最小化 D 盘收尾，随后进入 Idle。

Health Facets 至少独立包含：

    StorageHealth
    = Available / MemoryOnlyDegraded / PersistUnavailable / IntegrityDegraded

    BridgeHealth
    = Disconnected / Negotiating / Online / ResyncRequired

    ViewportHealth
    = High / Medium / Low / Unknown

    KnowledgeHealth
    = Verified / Stale / Incompatible / Unavailable

    VoiceHealth
    = Ready / Degraded / DeviceUnavailable / ProviderUnavailable

例如 D 盘不可用只影响 StorageHealth 与依赖持久化的能力；Viewport Low 只禁止棋盘内精确层；LLM 失败只禁用 RichPath。它们均不得自动结束一个仍可安全运行的会话。

关键生命周期顺序：

| 触发点 | 先做什么 | 允许什么 | 明确禁止 |
|---|---|---|---|
| Host 启动 | 新 runtimeEpoch、验证私有 D 盘根、加载已验证静态快照、默认无有效 lease | Panel 显示等待连接 | 恢复旧语音、旧箭头、旧 Advice |
| Bridge 连入 | 双通道握手、验证 token / Origin / epoch、先建立 HideAll 基线 | 接收最小化语义事件 | 未验证连接直接推送事实或渲染 |
| 游戏发现 | 取得完整 snapshot，建立 sessionId、sessionEpoch、CapabilityMatrix 与 Starting | 显示等待事实的低频状态 | 未有完整事实就生成精准建议 |
| 回合决策窗口 | 冻结 DecisionSnapshot、knowledgeSnapshotId 与 capability revision | 本地策略、FastPath、Advice、Panel、受限 Overlay / Voice | 等待 DataAgent 落账、知识下载、LLM、远程攻略 API |
| gap / Bridge 断线 | 关闭当前 Advice scope，取消未播语音，撤回精确层，请求 resync | Panel 显示受限状态或通用解释 | 猜测补齐实时数据；保留旧精确箭头 |
| viewGeneration 变化 | 使旧 render lease 失效，隐藏棋盘内精确层 | Dock / Panel 保留方向性信息 | 让旧像素坐标继续显示 |
| match end / 新局 / 用户停止 | 锁定决策入口；终止当前 scope；HideAll 与自动 Voice Cancel 并行 | 有界结束记录 | 新 Advice、旧 callback 重新交付 |
| Host 正常关闭 | 停止新 ingress；失效全部 epoch / lease；有界等待资源释放；关闭 socket 与 D 盘句柄 | 最小化维护收尾 | 无限等待 TTS / Renderer；向 C 盘写恢复文件 |
| Host 崩溃后的下一次启动 | 新 epoch；检查知识 / 证据账本完整性；只接受新 snapshot | Starting(RecoveredPartial) 的保守恢复 | 恢复正在播放或正在显示的旧实时状态 |

关闭使用固定顺序：

    1. Runtime / Session Gate 拒绝新的实时决策
    2. AdviceCoordinator 使当前 scope 终态化
    3. Overlay HideAll 与自动 Voice Cancel 并行发出
    4. 仅在有界 deadline 内等待真实 receipt / outputReleased
    5. 清空实时内存 Projection 与过期制品引用
    6. 冷路径异步完成最小化 D 盘收尾
    7. 关闭 IPC、资源句柄与可选受控子进程

第 4 步超时只能产生脱敏 reasonCode 与通道降级，不能卡住整个退出流程。Bridge / Renderer 还必须具备自保规则：render 连接断开、Host epoch 过期或 render lease 超时后自行 HideAll；Host 不得假定断线时一定还能发出撤回命令。

### 30.5 领域端口、方向与反循环合同

内部不引入任意发布 / 任意订阅的全局事件总线。每个领域只暴露少量类型化端口，消息只分为四类：

| 类型 | 含义 | 必须具备 | 不能做什么 |
|---|---|---|---|
| Fact | 已观察到、已验证或已发生的事实 | 来源、epoch、revision、单调时间、reasonCode | 隐含应该采取何种行动 |
| Command | 发给唯一状态所有者的请求 | commandId、目标 owner、scope、deadline | 跨越 owner 直接修改内部状态 |
| Receipt | 某条命令产生的实际结果 | 关联 command / advice / delivery / attempt 身份 | 被当作业务终态本身 |
| Snapshot | 某时刻冻结、只读的输入视图 | version、freshness、compatibility、固定引用 | 被消费者原地修改或反写上游 |

允许的主流：

    外部观察
    Bridge / GEP / Window / Renderer
            ↓ Fact
    IngressAndStateDomain
            ↓ Immutable DecisionSnapshot
    DecisionDomain
            ↓ StrategicDecision
    InterventionPolicy / AdviceSemanticCompiler / 专属表达 Skill
            ↓ 已验证 EligibilityResult + ChannelPlan + AdviceExpression
    AdviceCoordinator
            ├─ DeliveryIntent → GameVoiceScheduler → TTS / Playback
            ├─ RenderIntent   → Bridge / Overlay Renderer
            └─ PanelProjection → Alife Side Panel

    TTS / Playback / Renderer
            ↑ Receipt
    AdviceCoordinator

知识与用户控制只使用受限旁路：

    KnowledgeAndProfileDomain
            ↓ 已验证、冻结的 KnowledgeSnapshot
    DecisionDomain / RichPath

    Panel / Hotkey
            ↓ 受控 Command
    ControlRouter
            ↓
    唯一状态所有者

| 发送方 | 允许发送给 | 允许内容 | 不允许内容 |
|---|---|---|---|
| Overwolf Bridge | IPCGateway | 规范化 ingress 事实、snapshot、capability、窗口状态、Renderer receipt | 原始 GEP 落盘、策略、LLM、TTS、Advice 终态 |
| IPCGateway | IngressAndStateDomain | 已验证 NormalizedIngressEvent、连接 / 协议事实 | 对策略、Overlay、Voice 的直接调用 |
| IngressAndStateDomain | DecisionDomain | 冻结 DecisionSnapshot、Capability、Viewport 状态 | 原始 transport 帧、任意 UI 命令 |
| KnowledgeAndProfileDomain | DecisionDomain | 当前已验证的 Rules / Knowledge Snapshot 引用 | 当前对局事实、Advice、语音队列控制 |
| 分析模块 | StrategicDecisionCoordinator | ActionCandidate、RiskSignal、ObservationCandidate | TTS、Overlay、Panel、数据库写入 |
| StrategicDecisionCoordinator | InterventionPolicy / AdviceSemanticCompiler | 已验证 StrategicDecision 与有效期 / 风险边界 | 像素坐标、播放器操作、直接 Provider 调用 |
| Expression pipeline | AdviceCoordinator | 已验证 EligibilityResult、ChannelPlan、AdviceExpression 或 Invalid | 直接创建 Delivery、覆盖当前 Advice 或改写锁定数值 |
| AdviceCoordinator | Voice / Render / Panel | 已切分 Delivery Intent、render lease、Projection | 直接改写物理 Renderer 或播放器内部状态 |
| Voice / Renderer | AdviceCoordinator | 实际 Receipt、终态、reasonCode | 新 Advice、新策略、重放旧内容 |
| Panel / Hotkey | ControlRouter | 静音、跳过、手动朗读、校准请求、持久偏好变更 | GEP、像素 render、TTS 直接播放、策略覆盖 |
| ControlRouter | 对应唯一 owner | VoiceControlCommand、CalibrationRequest、PreferenceChange | 广播式修改多个领域 |
| RichPath LLM | ExpressionContractValidator | StructuredExpressionDraft | 决策、数值、优先级、站位、语音播放或 Overlay 命令 |
| DiagnosticsSink | 只读 Projection | 脱敏、白名单化诊断事实 | 反向影响实时决策或保存原始载荷 |

反循环不变量：

1. 上游事实只能向下游传播；下游若需改变上游行为，必须向上游唯一 owner 发送明确 Command，不能持有其可写引用。
2. Receipt 只能回到发出对应命令的协调者。例如 OverlayShown 只能回到 AdviceCoordinator，不能自行更新 Panel、生成语音或触发策略重算。
3. DecisionSnapshot 一经冻结不可修改。新事实、patch、gap、用户控制或 viewport 变化只能生成新 revision，旧决策经 Supersede / scope 终止失效。
4. 运行时不存在万能上下文对象；任一模块不得同时持有 LiveState、Knowledge、Advice、Voice、Panel 与 SQLite 的可写服务集合。
5. 只有 RuntimeSupervisor 可以广播 BeginShutdown、InvalidateEpoch、EnterMemoryOnlyDegraded 一类生命周期 Command；各 owner 自行终态化后回报 Quiesced。
6. 冷路径只能接收已脱敏 Projection，不能让 LiveStateReducer、StrategicDecisionCoordinator 或 AdviceCoordinator 等待它。
7. Host 只拥有希望显示什么和 lease 是否有效；Renderer 才拥有实际屏幕状态。连接中断、lease 失效或 Host epoch 变化时，Renderer 必须优先隐藏。

### 30.6 PoC 依赖、失败降级与总体一致性验收

以下是依赖门，不是版本排期：

    G0 私有运行边界
        ↓
    G1 Overwolf / Loopback 兼容性
        ↓
    G2 最小实时链路与真实回执
        ↓
    G3 Viewport 定位与安全降级
        ↓
    G4 Advice / Voice 生命周期
        ↓
    G5 本地知识与证据链
        ↓
    G6 可选 RichPath / Grok Provider
        ↓
    G7 全链回放、性能与自定义局验收

| 门 | 要验证什么 | 通过标准 | 失败时唯一处理 |
|---|---|---|---|
| G0：私有运行边界 | D 盘根校验、配置、缓存、SQLite、诊断、清理与 MemoryOnlyDegraded | 所有插件可控写入均解析到 D:\AlifeData\TFTCompanion\；D 盘失败不回退 C 盘 | 不启动依赖持久化能力；不得临时使用 %TEMP%、AppData 或旧 Alife 路径 |
| G1：Overwolf / Loopback | Bridge 到 Host 双通道 loopback、Origin、CSP、manifest、握手、重连、GEP 注册顺序 | 最小化假语义 payload 可稳定双向传输；Renderer 重建期间 Hide / receipt 有界 | 不采用 polling、文件或 SQLite 中转；重新评估最小 Sidecar + Named Pipe |
| G2：最小实时链路 | Bridge → IPC → Reducer → Advice → Render Intent → 真实 receipt | gap、重复、乱序、旧 epoch、ACK 丢失不使旧内容复活 | 保留 Panel / Dock 安全降级；不启用精确棋盘层 |
| G3：Viewport 与 Overlay | DPI、窗口、游戏缩放、ROI 漂移、置信度门控、透明穿透 | 旧精确层先隐藏；仅 High 恢复；Overlay 永远鼠标穿透 | 永久降级 Edge Dock + Panel；不强行贴棋盘 |
| G4：Advice 与 Voice | adviceRevision、scope、replaceKey、语音队列、真实 Started / outputReleased、设备丢失、TTS 出站白名单 | 旧制品不播放；用户静音优先；关闭不被 TTS 卡住；QQ 的队列 / Provider / 缓存 / 预算逻辑完全隔离 | 关闭游戏语音通道，保留 Panel / Overlay / FastPath；不声称插件可验证跨来源物理播放互斥 |
| G5：知识与证据链 | 本地 Pack、patch 兼容、原子发布、DataAgent ledger、D 盘故障、冻结快照 | FastPath 不等待 DataAgent；半完成包不可用；无当前局隐私写入 | 使用已验证静态 RulesPack；禁用依赖知识包的增强解释 |
| G6：RichPath / Grok | 专属 Profile、TLS、最小上下文、结构化草案、费用 / 超时 / 禁用状态 | 不改变确定性决策；超时、越权或未配置均可本地回退 | RichPath 保持 Disabled；FastPath 与陪伴基础不受影响 |
| G7：全链验收 | 第 29 节 Capsule、故障注入、性能 Harness、实际自定义局 smoke、长时运行 | P0 验收门全部有证据；延迟、残留、隐私与恢复合同满足 | 不进入自用功能版本或实施范围讨论 |

所有 PoC 输入与产物必须继续遵守：

- 不保存原始 GEP、截图、视频、账号、聊天、LLM prompt 或音频。
- 不接入游戏操作、内存读取、抓包、模拟输入或安卓模拟器控制。
- 不向第三方攻略平台上传当前对局信息。
- PoC 产物、诊断与临时数据只允许在 D 盘私有根中有界保存。
- 真实自定义局 smoke 仅验证已确认的只读能力，不能变成排位辅助或自动化能力。

在把本文称为最终规格前，文档级一致性验收至少要求：

1. 每一个可写状态均可在第 30.3 表中找到唯一 owner。
2. 每一条跨领域边均可在第 30.5 表中找到允许的 Fact、Command、Receipt 或 Snapshot 类型。
3. 每一个外部依赖均可在 G0 至 G7 中找到 PoC、失败降级和明确禁止的替代路径。
4. 每一条热路径都明确不等待哪些冷路径资源。
5. 关闭、断线、缩放、D 盘故障、设备丢失和 LLM 失败均不会复活旧 Advice、旧语音或旧 Overlay。
6. 跨进程 OverlayCommand / OverlayReceipt 均回显并验证完整身份元组；缺失或过期身份只允许拒绝或 HideAll。
7. 没有组件隐式依赖旧 Alife 的全局路径、QQ 语音、普通 SpeechService、DataAgent Session、通用 WebBridge 或默认 C 盘缓存。
8. 第 29 节 Replay Capsule 可以对上述架构不变量做自动验证，而非只验证少量单元函数。

粗略工作量仅用于未来范围评估，不构成版本排期：

| 子项 | 粗略工作量 |
|---|---|
| RuntimeSupervisor、生命周期 gate、D 盘 / 进程清理边界 | 2～3 人日 |
| 分域端口、单一 owner 封装、架构依赖测试 | 2～3 人日 |
| shutdown / recovery、故障注入、Replay 连接与诊断 Projection | 2～4 人日 |
| 合计 | 约 6～10 人日 |

该估算不包含第 25 节 Overwolf PoC、实际 TTS / LLM Provider、知识包授权、策略算法、Overlay 视觉实现或第 29 节完整回放基础设施的独立成本。首版无额外常驻 Sidecar；只有 G1 失败时才重新评估最小 Sidecar + Named Pipe，并重新打开本节审阅。

本节完成定义为：独立 Host 内受监督分域 Runtime、唯一状态 owner、身份 / revision 关系、生命周期、热冷路径、端口方向、PoC 依赖、出站边界和总体一致性验收均已明确。本文现在进入最终规格用户审阅；在用户审阅通过前，不进入实现或版本规划。
