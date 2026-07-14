# TFT Companion 完整体系版本路线图实施计划

> **For agentic workers:** 在获得某一版本的明确实施授权后，必须先使用 `superpowers:writing-plans` 创建该版本单独的、逐文件/逐测试实施计划。此路线图只定义范围、依赖、成本、Gate 和停点；它不授权创建代码、工作树、服务、账号、网络请求、构建或发布。

**目标：** 将已确认的完整陪伴型 TFT 教练体系从 `v0.0.1` 的运行兼容性验证，逐步推进到 `v2.0.0` 的完整、仍严格只读且可验证的产品集成；同时允许在每个有价值的闭环后安全停止。

**架构：** 首先建立 D 盘私有运行边界、Overwolf/Loopback、Panel-first FastPath 和 High-only Overlay；随后增加本地确定性策略质量；最后才逐步完善 Advice/Voice/Overlay 生命周期、知识/证据、可选 RichPath 和全链质量平台。快速本地路径从不等待网络、RAG、SQLite、DataAgent、TTS 或 LLM，所有外部能力均为独立且可关闭的旁路。

**技术边界：** PC《云顶之弈》、Overwolf TFT GEP、独立 `TftCompanionHost`、D:\AlifeData\TFTCompanion、确定性本地规则/内容包、可选独立 TTS、可选独立 GameCoach LLM Profile、合成脱敏 Replay。

---

## 1. 规划状态与不可协商边界

本路线图吸收并保留既有 [首期路线图](2026-07-14-tft-companion-version-roadmap.md) 的 `v0.0.1` 至 `v0.4.0` 范围；它只补齐原始完整设计在该阶段之后的路径。`v0.0.1` 的详细计划仍是唯一已经细化到文件/测试粒度的版本计划。

所有版本永远保持：

- 无游戏输入、ADB、进程内存读取、注入、抓包、协议绕过或安卓模拟器控制。
- Overlay 永久鼠标穿透，所有控制位于独立 Panel；不检测排位/匹配/自定义队列。
- 所有插件可控写入只在 `D:\AlifeData\TFTCompanion\`；D 盘失败不回退 C 盘/AppData/Temp。
- FastPath 不等待网络、LLM、RAG、SQLite 或 TftDataAgent；DataAgent 不分析、不推荐、不拥有当前局、Advice、Voice 或 Overlay。
- 第三方攻略来源对局中零网络、零上传；TTS/LLM 默认 Disabled，启用后也只发送各自白名单的最小载荷。
- 旧 epoch/session/revision/attempt/view/lease 绝不复活旧 Panel current、Voice 或 Overlay。
- 每个版本只增加一个用户可感知闭环，或一个阻止后续风险累积的必要质量闭环；Gate 未通过时，不以新功能掩盖问题。

`Disabled` 表示实际调用路径不存在，不是把 UI 按钮藏起来。

## 2. Gate 与依赖总览

```text
G0 D 盘私有运行边界
  → G1 Overwolf / Loopback PoC
  → G2 最小实时事实 + 真实回执
  → G3 Viewport High-only 与降级
  → G4 Advice / Voice 生命周期
  → G5 知识包、证据、授权与删除
  → G6 可选 RichPath / GameCoach LLM Provider
  → G7 Replay、性能、故障与自定义局验收

v0.0.1 → v0.1.0 → v0.2.0 → v0.3.0 → v0.4.0
   → v0.5.0 → v0.6.0 → v0.7.0 → v0.8.0 → v0.9.0 → v1.0.0
   → v1.1.0 → v1.2.0 → v1.3.0 → v1.4.0 → v1.5.0 → v1.6.0 → v1.7.0 → v2.0.0
```

这一顺序不是“必须把所有版本做完”的承诺。`v0.3.0`、`v1.0.0` 和 `v2.0.0` 是三个有意义的停止点；任何版本只有在前序 Gate 已有证据、并获得新的用户实施授权后才可进入详细计划。

## 3. 估算口径

| 术语 | 含义 |
|---|---|
| 净人日 | 一名经验工程师约六小时有效工程时间；不含账号审批、内容授权、平台审核、游戏不可用、用户评审或外部等待。 |
| 版本增量 | 该版本尚未被前序版本覆盖的工程范围；路线图刻意不把同一测试/集成工作机械重复相加。 |
| 自然时间 | 一名近全职开发者在目标机诊断、显示/DPI 复测、自定义局验证和正常审查缓冲后的区间；不等同于日历承诺。 |
| Gate 失败 | 暂停后续能力或安全降级，不授权 HTTP polling、文件中转、C 盘回退、未授权抓取或游戏自动化等捷径。 |

`v0.0.1`–`v0.4.0` 沿用既有估算。后续版本是根据已确认的 §22–§30 合同、已有模块复杂度与尚未覆盖范围给出的保守规划预留；不是对算法精度、内容质量或外部 Provider 可用性的保证。

## 4. 版本路线

### v0.0.1 — 运行兼容性 PoC

**唯一闭环：** 在目标机证明私有 D 盘、Overwolf Background Bridge、双 Loopback、最小 session/round/freshness/gap 事实和真实 `HideAll`/Renderer Receipt 可以共存。

**范围：** G0–G2 最小子集、脱敏 PoC 状态、D 盘无回退、最小自定义局语义事实。

**Gate：** Loopback/Origin/CSP/manifest/Renderer 重建可验证；D 盘失败不写 C 盘；缺失字段被 CapabilityMatrix 显式禁用。

**不做：** 策略、Panel 建议、定位、语音、DataAgent、RAG、LLM、Sidecar 实现。

**估算：** 6–10 净人日，约 2–3 周。

### v0.1.0 — Panel-first 本地陪伴教练

**唯一闭环：** 已验证 GEP → 冻结 Rules/有限 CompPack → 窄本地策略 → FastPath 表达 → AdviceCoordinator → Panel。

**范围：** 当前/过期/未知 Panel 状态、gap/supersede 失效、断网仍可用的受限本地建议。

**Gate：** 自定义局只能显示由受支持事实证明的结论；断线、切局和过期不保留旧 current。

**不做：** 精确 Overlay、游戏 Voice、知识下载/DataAgent 查找、RAG、RichPath、完整 ledger/replay。

**估算：** 7–11 净人日，约 2–3 周。

### v0.2.0 — 安全的 High-only Overlay

**唯一闭环：** 将 v0.1 已有 Advice 在支持的单一显示配置中安全投影为鼠标穿透棋盘提示；不增加策略种类。

**范围：** 受限 Viewport profile、High/Medium/Low、render lease、真实 `OverlayShown`、缩放/窗口/ROI 变动先隐藏、Panel/Dock 降级。

**Gate：** Overlay 永不接收鼠标；Low/过期/viewGeneration 变化后精确层绝不残留。

**不做：** 连续 OCR、截图/视频持久化、全显示环境保证、观战棋盘持续分析、Voice、LLM。

**估算：** 8–13 净人日，约 2–4 周。

### v0.3.0 — 首个自用发布（窄游戏语音）

**唯一闭环：** Panel/Overlay 独立可用的基础上，附加可关闭、单 Provider、过期即丢弃的短游戏语音。

**范围：** 一个 `IGameCoachTtsProvider`、一个 Active 加最多一个 Pending、最终 Mute/Skip、语音故障不阻塞其他通道、首批隐私/路径/过期回归。

**Gate：** Voice Disabled 时产品仍完整；启用时 TTS 超时、取消、切局、Mute/Skip 均不能播放旧段或阻塞 Panel/Overlay。

**不做：** 多 Provider、预取、hedge、费用/熔断、复杂 timing、设备恢复、RichPath、完整回放。

**估算：** 8–14 净人日，约 2–4 周；TTS 账户和计费是外部等待。

### v0.4.0 — 单来源本地知识维护

**唯一闭环：** 在启动后/大厅/手动刷新时，将一个已授权来源或用户离线包安全地 `staging → validation → publish` 为未来局可用的知识快照。

**范围：** Manifest/hash/patch/license、单来源或手动导入、最小 `TftDataAgent` 保存/查找、原子 `knowledgeSnapshotId`、最小维护证据和删除。

**Gate：** 对局中无来源网络；半成品/不兼容/无许可 Pack 永不激活；当前 DecisionSnapshot 不被新包改写；D 盘失败不假装发布成功。

**不做：** 多来源、embedding/RAG、查询级 evidence、hash-chain/DAG、RichPath/LLM、维护大面板。

**估算：** 6–10 净人日，约 2–3 周。

### v0.5.0 — 战略状态、连败与阵容路径

**唯一闭环：** 用户能获得以连败/连胜、启动窗口、最低止血板、转型和失败回退为核心的本地阵容路径建议，而不是把低即时战力一律判为失败。

**范围：** `StrategicObjective`/`PlanState`/`UserIntent`、用户可见锁定/临时高危覆盖、完整 CompFamily/BoardNode/Transition/Fallback 路径、启动预算与停止条件、首次 10–20 个完整阵容家族。

**Gate：** 计划内连败、可控延迟启动、错过安全窗口和必须止血四种情形在相同本地事实下稳定可区分；用户锁定不能被静默解除；patch/包不匹配时安全降级。

**不做：** 多回合随机优化、LLM 策略、实时第三方教练 API、精确胜率。

**估算：** 14–24 净人日，约 3–5 周。

### v0.6.0 — 经济、牌库、复制器与追三降噪

**唯一闭环：** 用户可通过智能推荐/当前阵容/四费/五费/指定弈子入口，得到低噪声的追三、经济预算和高费升星风险提醒。

**范围：** `VisiblePoolLedger` 的可见下界/上界/未知保留、复制器唯一分配、追三类别/价值函数、对手升星阈值、金币底线/停止/回退、Panel 优先提醒。

**Gate：** 从不声称精确剩余牌数；零持有和低价值低费不进入主动追三；同一复制器不双重分配；4/5 费风险受关注强度/相关性/注意力预算门控。

**不做：** 完整全局卡池真相、在线数据补齐、自动刷新/购买、LLM 数值计算。

**估算：** 12–22 净人日，约 3–5 周。

### v0.7.0 — BoardStrength、承伤、装备与强化

**唯一闭环：** 用户获得可解释的阶段相对战力/宽风险带以及装备、强化、止血和转型支持结论。

**范围：** `BoardStrengthVector`、Low–LethalRisk、有限 4–6 个合法候选、角色需求与有限装备分配、强化已选建模/三选一能力门控/手动回退。

**Gate：** 只输出宽风险带，不伪装成精确胜率/伤害；未知高影响强化降级；装备特殊规则白名单；建议带机会成本和转移性。

**不做：** 完整战斗模拟、在线训练、全行动空间搜索、强化 OCR 或自动点击。

**估算：** 27–45 净人日，约 6–10 周；其中 BoardStrength 已有粗略复杂度基线为 21–39 净人日。

### v0.8.0 — 逻辑站位与回合快照对手信息

**唯一闭环：** 在不频繁观战、不预测下一对手的前提下，基于 BaselineFormation、己方角色映射和可信快照威胁，给出最多两个关键移动并由 v0.2 的 High-only Overlay 安全显示。

**范围：** 4×7 逻辑棋盘、坐标版本化映射、有限阵型变体、`PositioningScoreVector`、玩家人口模板缩减、回合结算快照、可信时的对手棋盘微调和 Panel 完整阵型。

**Gate：** 没有已验证 `opponent_board_pieces` 时退回本地 BaselineFormation；不显示候选对手身份；逻辑站位与像素投影完全分离；每次移动后重新验证，显示不超过 1–2 箭头。

**不做：** 实时观战切换、完整棋盘排列搜索、完整战斗模拟、OCR 作为真相、热力图默认展示。

**估算：** 35–68 净人日，约 8–15 周；这是逻辑站位子系统预留，不含无限显示环境承诺。

### v0.9.0 — 本地确定性教练整合

**唯一闭环：** 将 v0.5–v0.8 的策略域纳入同一 DecisionSnapshot、候选仲裁、注意力预算、Advice/Panel/Overlay 失效路径，使不同建议不相互打架。

**范围：** 共享资源冲突、支配剪枝、滞回、被抑制原因、跨策略建议的有效期/停止/回退、综合本地回归和自定义局确认。

**Gate：** 同一输入生成稳定的主行动/支持行动/观察；策略模块不能越权交付；小波动不造成连续播报；缺失事实返回 Unknown 而非拼凑结论。

**不做：** 完整 Advice/Voice 高级生命周期、RAG/LLM、全回放平台。

**估算：** 13–24 净人日，约 3–5 周。

### v1.0.0 — 完整本地确定性教练稳定发布

**唯一闭环：** 将前述本地策略、Panel、High-only Overlay、可选窄 Voice 与单来源知识闭合为可重复自用的只读陪伴教练基线。

**范围：** 版本/内容冻结、跨策略回归、用户偏好/陪伴强度、首批完整内容质量检查、目标机多轮 smoke、首期发布回滚/清理说明。

**Gate：** v0.0.1–v0.9 的安全约束都有实际证据；在不启用 Voice、知识下载或外部 Provider 时仍提供完整本地体验；不因完成此版本自动打开任何远程能力。

**不做：** 完整 Advice 状态机、多 Provider Voice、RAG、RichPath、完整 ledger/replay/性能实验室。

**估算：** 12–20 净人日，约 3–5 周。

### v1.1.0 — 完整 AdviceCoordinator 与 Delivery 生命周期

**唯一闭环：** 所有类型的建议、替换、关闭、用户软接管、Panel/Overlay/Voice Receipt 都由一个可回放的单写者 Advice 协调器一致管理。

**范围：** 父 Advice/Delivery 状态、scope/replaceKey、两阶段 supersede、semanticDigest 去重、三通道 Delivered 定义、关闭原因仲裁、runtimeEpoch 恢复与 watchdog 健康。

**Gate：** `Accepted ≠ Shown ≠ Delivered`；任何旧 Receipt 不能改变 Current；用户主动请求只软接管必要语音；退出有界且不恢复旧内容。

**不做：** 启用 LLM、扩展策略、跨来源播放仲裁。

**估算：** 12–20 净人日，约 3–5 周。

### v1.2.0 — 完整独立游戏语音系统

**唯一闭环：** 对时间敏感的游戏语音具备可证明的短段落、优先队列、准备、真实播放终态、Provider 路由/预算、deadline、设备与用户控制，而不污染 QQ。

**范围：** 1 Active + 4 Pending、PreparingCandidate、有限预取、PlaybackSession、真实 `outputReleased`、自适应语义停顿、P1 受控 hedge、错误/熔断/费用、PhaseDeadlineRegistry、设备恢复和统一控制。

**Gate：** 旧制品不播放、Mute/Skip 最终优先、关闭不被 TTS 卡住、所有缓存 D 盘有界、QQ 队列/Provider/凭据/缓存/预算完全隔离。

**不做：** QQ 与游戏的物理扬声器并发检测/仲裁，或把 QQ 当游戏 TTS fallback。

**估算：** 28–50 净人日，约 6–12 周；这是在 v0.3 窄链已经存在后的完整化增量。

### v1.3.0 — Host/Overlay 兼容硬化

**唯一闭环：** 在明确支持的显示/Windows/Overwolf 配置矩阵中，Host、Bridge、Renderer 和动态 Viewport 能长期安全恢复、校准和撤回精确层。

**范围：** 更完整的窗口/DPI/多显示器/游戏缩放校准、Renderer 重建和断线恢复、受限的人工校准、Host 生命周期/健康面、IPC 背压/重连、真实目标机兼容性矩阵与发布适配。

**Gate：** 显示变化永远先 Hide；不支持的配置永久退回 Dock/Panel；无后台残留、无 C 盘写入、无因重连恢复旧 render。

**不做：** 保证所有分辨率/皮肤/显示器组合、连续 OCR 或游戏交互。

**估算：** 15–28 净人日，约 4–7 周。

### v1.4.0 — 多来源本地知识与受限检索

**唯一闭环：** 允许多个已授权的本地知识包在局外维护，并为用户主动解释提供版本/来源/样本可见的本地结构化检索。

**范围：** 多来源兼容矩阵、统计口径/分段/地区/时间窗、LocalGuideRetrieval、可选本地 embedding/rerank、来源控制与删除、局内冻结。

**Gate：** 快速路径仍不查库；无授权/不兼容/过期内容不推动关键结论；对局中无攻略网络；本地 embedding 不出 D 盘。

**不做：** 自由网页搜索、外部 embedding API、当前局在线查询、LLM 自动启用。

**估算：** 8–16 净人日，约 2–4 周。

### v1.5.0 — 原生证据链与维护恢复

**唯一闭环：** 用户/维护者能在本地解释一个知识包为何被获取、拒绝、规范化、索引、发布、替换或删除，并可在异常后保守恢复维护状态。

**范围：** hash-chain/DAG、状态机、Projection/checkpoint/replay、受限查询级 `PinnedEvidenceBundle`、保留/删除/tombstone、完整性降级和只读维护视图。

**Gate：** 证据链不记录当前局、用户问题、LLM/语音内容或策略结论；链损坏不伪造成功；删除后不可由 checkpoint/backup 复活已清除内容。

**不做：** 让 ledger 进入实时决策链或把维护数据上传第三方。

**估算：** 7–11 净人日，约 2–3 周。

### v1.6.0 — 可选 RichPath / GameCoach LLM

**唯一闭环：** 用户主动追问时，可从一个专属、最小化、可禁用的 LLM Profile 获得更自然的解释；本地 FastPath 及最终决策完全不变。

**范围：** `GameCoachLlmProfile`、CredentialStore、endpoint/TLS/能力 PoC、ContextSanitizer、StructuredExpressionDraft、Validator、预算/超时/审计桶、本地回退和 Disabled fail-closed。

**Gate：** Provider/model/地区/套餐/限流/计费均先经 PoC；无配置、超时、越权、预算拒绝时零远程调用或安全本地回退；模型不能调用工具、文件、网络、TTS、Overlay 或改变锁定结论。

**不做：** 实时策略 LLM、全局 Alife/QQ 模型复用、自动上传攻略/当前局、把 Grok 4.5 名称硬编码成可用 API。

**估算：** 10–18 净人日，约 2–4 周，外部账号/endpoint 等待不计入工程日。

### v1.7.0 — 完整回放、故障与性能平台

**唯一闭环：** 对完整系统的正确性、降级、隐私、时序、性能和长时稳定性有可重放、可比较、可发布前审计的证据。

**范围：** `TftReplayCapsule`、虚拟时钟、fake adapter、CanonicalOutcomeProjectionHash、P0/P1 场景库、故障矩阵、IPC/Overlay/TTS/LLM 回放、性能 histogram、路径/网络审计、长时 soak 与自定义局 smoke 工具。

**Gate：** P0 必须有回放/集成证据；僵尸 callback 穷举通过；性能和隐私审计不保存真实原始对局；失败 Gate 关闭能力而不降低安全要求。

**不做：** 用测试平台收集真实玩家数据、游戏控制能力或替代平台合规审阅。

**估算：** 13–22 净人日，约 3–5 周。

### v2.0.0 — 完整体系集成与受控发布

**唯一闭环：** 将完整本地决策、完整交付、知识/证据、可选 RichPath 和质量平台在明确的支持矩阵内组合为可维护、可恢复、可安全关闭的完整原始设计实现。

**范围：** 跨域集成、升级/回滚/清理、支持矩阵、关键外部配置复验、全链 P0/P1 证据、用户可见限制说明、发布前故障收敛和长期运行基线。

**Gate：** G0–G7 全部有可复查证据；完整系统仍能在外部能力 Disabled 时退回本地只读陪伴；不存在未受监督 Sidecar/后台残留；所有声称“支持”的显示/Provider/知识范围都被明确证实。

**不做：** 多市场自动发布、自动创建 GitHub Release 或自动提交 Alife.PluginMarket JSON、跨进程插件、无限内容来源、复杂权限中心、任何游戏控制或本路线图外的新产品方向。

**估算：** 15–28 净人日，约 4–7 周。

## 5. 累计工程量与自然时间

| 阶段 | 版本 | 增量净人日 | 累计净人日 |
|---|---|---:|---:|
| 运行基础与首期知识 | v0.0.1–v0.4.0 | 35–58 | 35–58 |
| 完整本地确定性教练 | v0.5.0–v1.0.0 | 113–203 | 148–261 |
| 交付/宿主完整化 | v1.1.0–v1.3.0 | 55–98 | 203–359 |
| 知识、证据、LLM 与质量平台 | v1.4.0–v1.7.0 | 38–67 | 241–426 |
| 完整集成发布 | v2.0.0 | 15–28 | **256–454** |

以一名近全职工程师每月约 18–20 个有效净人日计算，完整路线的纯工程自然时间约为 **13–25 个月**。考虑目标机/显示配置反复、Overwolf GEP 字段差异、Provider 设备/账号和内容维护，实际经过时间应预留 **16–30 个月**，其中外部等待不是可以靠增加代码日压缩的工作。

该总量不包括第三方来源授权谈判、持续内容编辑与 patch 运营、TTS/LLM 账号审核/套餐、法律或平台许可审阅、远端服务故障等待，也不包括独立的跨宿主插件分发工程。完整路线显著大于首期 `v0.0.1–v0.3.0` 的 29–48 净人日，这是刻意将“先可用、再完整”分开的结果。

## 6. 硬停止与外部风险

| 条件 | 停止规则 |
|---|---|
| G1 Loopback/manifest/Origin/CSP 不成立 | 停在 v0.0.1；不使用 polling/文件/SQLite 中转。是否走 Sidecar + Named Pipe 必须重新设计。 |
| GEP 无法稳定提供某个关键字段 | 该字段相关策略/站位/牌库能力保持 Disabled；不靠 OCR 或猜测补齐。 |
| Overlay 不能在目标显示配置保持 High/先撤回 | 精确棋盘层停在 Dock/Panel，不增加 OCR/输入捷径。 |
| TTS Provider/设备不可靠 | Voice 关闭或保持窄链，Panel/Overlay 不受影响；不回退 QQ。 |
| 知识来源授权或许可不清楚 | 只允许用户手动离线导入，或完全不启用该来源；不抓取私有端点。 |
| LLM PoC/凭据/TLS/预算未通过 | RichPath 保持 Disabled；FastPath 继续本地运行。 |
| D 盘、完整性、隐私或僵尸回调 P0 失败 | 不进入后续功能版本或发布；先修不变量。 |
| 第二个内容源/显示配置/Provider 需要破坏既有合同 | 不将其塞进当前版本；单独评审范围和成本。 |

## 7. Alife.PluginMarket 外部发行 Gate

正式市场发行采用独立路径：`hushu1232/plugin` 主仓库保存代码和版本，固定 GitHub Release 制品用于下载，`BDFFZI/Alife.PluginMarket` 只接受指向该制品的、与最新 schema 匹配的 JSON 描述。该路径不计入本路线图的 `256–454` 净人日，因为其 schema、权限、安装器和远端状态必须在实际发布时重新核验，且创建 Release、Pull Request、提交或 push 都需要单独授权。

市场发行只能在完成[PluginMarket 发行规格](../specs/2026-07-14-tft-companion-plugin-market-distribution-engineering-spec.md)的 D0（最新契约核验）、D1（制品/目标机安装验证）和 D2（JSON/市场验证）后进行。不得把 `v2.0.0` 完成解释为自动对外上传。

## 8. 详细计划与实施授权

当前只允许保留本路线图和已有的 `v0.0.1` 详细计划。以后用户明确选择某一版本时，下一份文档必须只针对该版本，列出准确工作树、文件、测试、目标机证据、失败出口和回滚。不得为了“提前准备”创建下一版本的接口、数据库、Provider 凭据或后台服务。

若实施涉及 `D:\Alife`，必须首先检查并保护用户未提交的 `sources\Alife\Alife.Platform\AlifePath.cs` 改动。若实施涉及本地/上游双宿主插件注册，则先遵循独立的跨宿主兼容路线图，而不是在 TFT 版本中临时修改核心加载机制。
