# TFT Companion 知识、LLM 与证据工程规格

> **状态：** 已确认设计的工程规格；定义本地知识与可选远程表达的隔离，不授权获取第三方授权、创建账号、下载内容、创建凭据或调用任何 Provider。

**目标：** 让攻略、版本、阵容、统计、装备、强化和站位模板成为可验证、本地、可删除的知识事实；让 `TftDataAgent` 仅负责查找和保存；让未来 Grok 4.5 等 LLM 只承担可选的受限表达，不进入实时决策。

**架构：** 授权来源或用户手动导入产生候选 Pack；`TftDataAgent` 作为唯一持久化写者执行 staging、验证、规范化、索引、发布和删除。FastPath 只读取已冻结内存 `KnowledgeFactView`；异步检索和 RichPath 仅在大厅、维护或用户主动问题中使用，所有远程草案须通过本地 Validator。

**依赖：** [工程文档索引](2026-07-14-tft-companion-engineering-document-index.md)、[运行时与决策规格](2026-07-14-tft-companion-runtime-decision-engineering-spec.md)、[宿主/存储/IPC 规格](2026-07-14-tft-companion-host-storage-ipc-engineering-spec.md)、[质量与发布规格](2026-07-14-tft-companion-quality-release-engineering-spec.md)。

---

## 1. 外部知识的零上传边界

第三方攻略平台只能是**正式授权、本地知识包来源**，不能是实时教练 API。公开可见网页不自动等于可下载、索引、再分发、商用或调用其私有端点。

每个来源必须具备至少一种可审计依据：正式书面授权、公开且明确可用 API/许可、或用户自行提供有权使用的离线包。没有许可时，唯一正确行为是拒绝创建 Pack；不抓取私有端点、不模拟网页、不以“仅供测试”绕过。

允许的网络时机只有用户显式启用来源后的启动后、大厅或手动刷新。请求只能包含来源所需的静态下载信息，例如 URL、通用 User-Agent、协议所需 header、时间和无身份的授权材料；它绝不包含：

- 当前棋盘、对手、商店、经济、牌库、强化、截图、视频、音频或原始 GEP；
- Riot ID、昵称、match/session 标识、设备标识、跨局行为、推荐结果或诊断；
- 用户问题、局内搜索词、RAG query、LLM prompt、语音文本或 Cookie/Token 泄漏。

对局中攻略来源网络请求为零。来源不可用、包过期或不兼容时，系统继续使用局内冻结的本地规则/CompPack，或以 `Unknown` 降级，不阻塞游戏。

## 2. Pack 模型、统计口径与原子发布

### 2.1 Pack 分类

| Pack | 内容 | 使用边界 |
|---|---|---|
| OfficialRulesPack | Set、patch、弈子、费用、羁绊、技能、装备、强化、机制与数值事实 | 确定性规则和兼容性基础。 |
| MetaMetricsPack | 热门阵容、选取率、平均名次、前四率、第一率、样本、分段、地区、时间窗、变体 | 候选排序和风险证据，不能替代当前局分析。 |
| StrategyKnowledgePack | 过渡、经济、连败/连胜、升人口/D 牌窗口、追三、转型和失败条件 | 本地解释和候选路径事实。 |
| PositioningTemplatePack | 基础阵型、角色位、反制原则与适用前提 | 只提供 `BaselineFormation`，最终逻辑格仍由本地棋盘/策略决定。 |
| SourceAndCompatibilityPack | 来源、许可、署名、Set、patchRange、地区、分段、窗口、hash、签名、失效期 | 兼容性、归因、审计和失效门控。 |

一个 `knowledgeSnapshotId` 可以引用多个 Pack，但一次决策使用的所有内容必须共享可验证的 ruleset、patchRange、locale、来源 Manifest hash 和适用范围。

### 2.2 统计不能笼统叫“胜率”

界面和策略必须区分 `FirstPlaceRate`、`Top4Rate`、`AvgPlacement`、`BattleWinRate` 与 `PickRate`。每个指标至少携带 `metricDefinition`、值、样本、来源可提供的不确定性、分段、地区、规则口径、数据窗口、patchRange 和 sourceId。样本不足、来源口径不明、时间窗过期、地区/分段不适用或 patch 不匹配时，只能显示为历史/低置信 Panel 参考，不能推动关键经济、追三或精确站位建议。

### 2.3 Manifest 与发布

每个 Pack 的 Manifest 至少包含：

```text
packId / sourceId / sourceDisplayName / license or authorizationReference
attributionRequirement / acquiredAt / publishedAt / validFrom / expiresAt
locale / set / patchRange / region or rankBracket / dataKinds
schemaVersion / contentHash / signature or verificationMethod
```

更新流程固定为：

```text
受控入站候选
→ D 盘 staging
→ license / Manifest / hash / schema / Set / patch / size 验证
→ 规范化与索引
→ 自检
→ 原子 publish 新 knowledgeSnapshotId
或 rejected（记录真实原因）
```

活跃 Session 的 `knowledgeSnapshotId` 永远固定，更新只影响之后的 DecisionWindow。半成品、旧 patch、hash/许可失败或索引失败不能激活；无兼容快照时回到本地 RulesPack/CompPack。

## 3. TftDataAgent：严格的数据边界

`TftDataAgent` 是本地类型化仓储服务，不是分析 Agent、对话 Agent 或推荐器。

| 允许拥有 | 明确不拥有 |
|---|---|
| `packs\`、`knowledge\`、`dataagent\` 的唯一写入权；受控 AcquisitionCommand；Pack/Manifest/规范化事实/索引/证据保存、发布、回滚、删除 | 当前局会话、GEP 订阅、游戏分析、策略评分、Advice、Voice、Overlay、Panel 文案、优先级、用户陪伴表达。 |
| 类型化本地查找：Comp、Item、Metric、PositioningTemplate、SourceManifest 等 EvidenceRecord/Chunk | HTTP Sidecar、外部 LLM Planner、External RAG、Postgres、第三方实时攻略 API、embedding API、通用自然语言分析。 |
| `Unknown`、`NotFound`、`Stale`、`Incompatible`、`PermissionDenied` 等确定结果 | 向调用者暗示“应该如何玩”或把数据变成结论。 |

FastPath/DecisionWindow 不得同步发出 `DataLookupRequest`、等待 SQLite、等待 DataAgent、等待索引或等待 ledger。它只能读取从当前 `DecisionSnapshot` 预先冻结的 `KnowledgeFactView`。DataAgent 查询仅允许在启动后、大厅、知识预取、维护、用户主动 RichPath 或赛后异步链路发生。

## 4. 本地检索与不可信内容处理

检索顺序是：结构化 Set/patch/locale/packType/applicability 过滤 → 本地术语/弈子/装备/羁绊/阵容标签词法检索 → 可选本地 embedding/rerank → 版本/来源/样本/前提复核。可选 embedding、索引、chunk 和缓存均只能在 `D:\AlifeData\TFTCompanion\knowledge\` 本地生成；不调用外部 embedding API。

`LocalGuideRetrieval` 返回带 `chunkId`、`knowledgeSnapshotId`、pack/source、patchRange、retrievalReason、applicability、freshness、confidence 和 `textOrStructuredFact` 的 `EvidenceChunk`，而不是自由文本答案。每类查询都有 chunk 数、字符数和时间上限；低置信/无结果返回 `Unknown`，不触发在线搜索。

所有攻略内容都是 `ExternalKnowledge / UntrustedContent`：正文、表格、标签和来源元数据分离；禁用脚本、HTML、嵌入指令、外链执行语义；不能调用工具、改变系统约束或扩展权限。Panel 可显示来源/版本/新鲜度，Overlay/Voice 只使用已通过合同验证的短结论。

## 5. 原生证据链与删除

证据链保存的是“知识流程状态”，不是当前局回放、LLM 思维链或用户对话。它服务于维护、恢复、兼容性和可追溯性。

### 5.1 Ledger 范围

每条 entry 需能关联 Pack/Manifest、校验、规范化、索引、发布、删除或受限查询事件，并带稳定身份、前序 hash、状态、时间、reasonCode、受控 Projection 引用和完整性字段。它不保存原始 GEP、当前棋盘、账号、用户问题、LLM prompt/reasoning、语音正文或策略结论。

允许的状态机为：

```text
Acquired → Staged → Validating → Normalized → Indexed → Published
                         └────────────────────────────→ Rejected
Published → Superseded / Deleted
```

`Projection`、checkpoint、重放、compaction 和 tombstone 都由 TftDataAgent 单独拥有。D 盘故障、SQLite 锁、WAL/I/O 异常或 hash 链断裂时进入 `IntegrityDegraded`；不得伪造“已发布/已保存”，更不能将数据换写到 C 盘。

### 5.2 清除与保留

用户可以关闭来源、清除单 Pack、清除单来源或清除本地索引。清除后，后续检索不得使用已删除内容；当前 Advice 只可在自身 scope 正常结束前保留已绑定的最小 evidence 摘要，不重新联网、不重新生成旧建议。恢复材料不得重引入用户已清除的原始包/索引。tombstone 只保留解释删除与防止意外恢复所需的最少元数据。

## 6. GameCoachLlmProfile 与 RichPath

### 6.1 专属 Profile

预期模型族为 Grok 4.5，但这不是硬编码 model id、可用性或 API 形态的承诺。Profile 使用 `providerId`、`configuredModelId`、`endpointProfile`、`credentialRef` 和 `capabilitySnapshot`；实际 endpoint、地区、TLS、套餐、限流、上下文、计费和模型 ID 都必须在独立 PoC 中验证。

`GameCoachLlmGateway` 与 TftDataAgent、QQ、Alife 全局 LLM、TTS 和攻略来源不共享凭据、缓存、队列、预算、fallback、工具权限或故障域。凭据只通过专属 `GameCoachCredentialStore`（DPAPI blob）引用；未配置、TLS/endpoint PoC 未通过、预算拒绝或用户关闭时，调用路径保持 Disabled。

### 6.2 最小化上下文与输出合同

RichPath 的输入只能是当前仍有效的 `StrategicDecision`、冻结 `DecisionSnapshot` 的最小结构化摘要、已验证的本地 `EvidenceRecord` 和用户主动请求所需的最小意图类别。禁止发送原始 GEP、截图、视频、账号、match ID、完整历史、完整敌方信息、QQ 内容、音频、诊断、本地文件、系统提示或工具描述。

RichPath 流程为：

```text
已验证 StrategicDecision + 冻结 EvidenceRecord + 用户主动问题
→ render-tft-companion-advice 的 RichPath 策略
→ GameCoachContextSanitizer
→ GameCoachLlmGateway
→ StructuredExpressionDraft
→ ExpressionContractValidator
→ 合格的 AdviceExpression 或 Invalid
```

草案只可补充受限解释，且必须保留锁定数值、行动、优先级、站位、scope、版本/置信度门和来源。Validator 拒绝越权字段、无来源断言、超时/迟到草案、潜在工具调用、自由文本到 Voice 的无审查转交。错误、预算耗尽和离线都回到本地模板/Panel，不得等待后补播。

## 7. 关键验收与成本边界

### 7.1 必须通过的负向测试

1. 对局中攻略来源没有任何网络请求；下载载荷不包含当前局、身份、查询或推荐结果。
2. 未授权、篡改 hash、许可缺失、旧 Set/patch、样本不足或不兼容内容不能激活。
3. 新 Pack 原子发布，活跃 `knowledgeSnapshotId` 不变，D 盘失败不假装成功也不回退 C 盘。
4. TftDataAgent 无法订阅 GEP、创建 Advice、调用 LLM/HTTP Sidecar/External RAG 或同步阻塞 FastPath。
5. 被删除来源在以后查找中不可用；ledger/backup 不重建被删原始内容。
6. RAG chunk 不能改变系统约束、调用工具或把攻略文本直接传给 Overlay/Voice。
7. LLM Disabled、凭据缺失、TLS/endpoint 未验、上下文不合规、超时或预算拒绝时，不存在远程模型请求；成功草案也不能改写确定性决策。

### 7.2 计划成本

已有完整设计中，知识 Pack/检索/DataAgent/隐私边界约为 12–22 净人日，原生证据链约为 7–11，完整回放基础设施约为 13–22；完整 LLM 实现此前未报价，本工程规划预留 10–18。由此，完整 §§26–29 的纯技术范围约为 42–73 净人日，生产化的来源/Provider/发布/跨机器硬化建议额外预留 15–30，合计约 57–103 净人日。

这些数字不包括第三方授权谈判、内容制作/持续 patch 运维、Provider 账号审批、套餐/计费、法律审阅或外部平台不可用等待。版本拆分、增量范围与停止 Gate 见[完整版本路线图](../plans/2026-07-14-tft-companion-complete-system-version-roadmap.md)。
