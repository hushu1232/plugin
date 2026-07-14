# TFT Companion 完整工程文档集索引

> **状态：** 已确认设计的工程化拆分；仅用于评审、版本规划和后续逐版本详细计划。它不授权创建代码、构建项目、安装 Overwolf、创建 Provider 账户、发起网络请求或修改 `D:\Alife`。

**目标：** 将此前已确认的 PC《云顶之弈》只读陪伴教练完整体系，整理为按职责可独立审阅、可独立验证、可分阶段停止的工程文档集。

**架构：** 一个独立的 `TftCompanionHost` 在受监督的运行时中承载会话、事实、策略与交付协调；Overwolf 只采集和渲染，Alife 侧边面板只展示与发出受控命令。实时 FastPath 只使用本地、冻结且已验证的事实；知识维护、可选 RichPath、证据、回放和诊断均处于不阻塞实时链路的受限旁路。

**技术边界：** PC《云顶之弈》、Overwolf TFT GEP、独立本机 Host、双物理 Loopback WebSocket、D 盘私有数据根、确定性本地策略、可选独立 TTS 与可选独立 LLM Profile。

---

## 1. 文档控制与使用方式

本索引不替换下列已确认的历史基线，而是将其中的结论装配成可执行的工程包。

| 文档 | 地位 | 使用方式 |
|---|---|---|
| [TFT 设计检查点](2026-07-13-tft-companion-design-checkpoint.md) | 历史设计基线，覆盖 §§1–30 的逐项用户确认 | 当本索引或拆分规格与其表述不同，先以明确的本索引纠偏为准；没有纠偏时，以检查点为准。 |
| [首期路线图](../plans/2026-07-14-tft-companion-version-roadmap.md) | `v0.0.1` 至 `v0.4.0` 的已确认窄范围路线 | 本文档集保留这五版的范围和门槛；完整路线图只补齐后续版本。 |
| [v0.0.1 详细计划](../plans/2026-07-14-tft-companion-v0-0-1-runtime-compatibility-poc.md) | 当前唯一可在获得实施授权后进入细化执行的版本计划 | 后续版本不得借用它的 PoC 范围提前创建功能代码。 |
| 跨宿主插件兼容工程（独立于本目录） | 与 TFT 功能体系并行、但独立的 Alife 插件分发工程 | 只在需要同时支持本地 Alife 与缓存上游宿主时，实施前重新读取 `D:\FOXD\docs\superpowers\specs\2026-07-14-alife-cross-host-plugin-compatibility.md` 及其路线图；它不属于本次迁移的 TFT 专属文档集，也不改变 TFT 运行时安全边界。 |

本轮新增文件是规划产物，不要求提交 Git。现有未跟踪文档和 `D:\Alife` 中用户的改动都不属于本工程文档集的可修改范围。

## 2. 产品边界：什么是“完整体系”

完整体系是一个**只读、低打扰、可解释的陪伴教练**，不是代打、自动化、游戏数据采集平台或通用聊天机器人。

它应能在已确认的数据能力内，完成下面的闭环：

```text
受支持的 GEP / 窗口事实
  → 版本、会话、新鲜度与能力门控
  → 冻结的本地规则、阵容与知识事实
  → 确定性战略、经济、牌库、装备、风险与站位候选
  → 单一 StrategicDecision
  → 低打扰表达与 Advice 协调
  → Panel 优先、High 时 Overlay、可选独立游戏语音
  → 真实回执、失效、回放与安全降级证据
```

完整体系不包含以下能力，后续版本也不能用“完整”名义重新引入：

- 任何鼠标、键盘、ADB 或安卓模拟器控制；任何自动购买、刷新、升级、换位、合装、观战切换或使用道具。
- 游戏进程内存读取、注入、抓包、解密、协议绕过或对私有接口的爬取。
- 排位/匹配/自定义队列检测与按队列类型启停。会话仅依赖受支持的 `match_start` / `match_end` 等游戏事实。
- 以不稳定 OCR 代替实时棋盘真相；持续截图、录像或上传视觉流；“每次换位都提示”。
- 将当前局面、账号、匹配标识、用户问题、牌库、截图、诊断或建议结果传给第三方攻略来源。
- 让 LLM 决定数值、经济、优先级、站位格、游戏操作或 Advice 最终状态。

安卓模拟器中的《金铲铲之战》不属于本工程范围。后续若讨论它，必须重新评估可获得的公开、稳定、授权数据能力，而不能把 PC TFT 的 Overwolf 合同平移过去。

## 3. 全局不变量

每一份后续规格和版本计划都必须保留下面的约束。若某项无法满足，版本应关闭对应能力或停在 Gate，不以临时绕过继续推进。

1. **严格只读。** Overlay 永久鼠标穿透；所有可点击控件只存在于 Alife 独立侧边面板。不存在游戏输入模拟的接口或回退路径。
2. **唯一可控写入根。** 所有插件可控写入经同一 `StorageRootPolicy` 解析后必须位于 `D:\AlifeData\TFTCompanion\`；D 盘不可用时进入内存或持久化降级，绝不回退至 C 盘、AppData、Temp、默认 Alife 路径或隐式 SDK 缓存。
3. **FastPath 本地且有界。** 它不等待网络、远程 LLM、RAG、SQLite、TftDataAgent、知识下载、索引、导出、回放或普通 QQ `SpeechService`。
4. **单一状态所有者。** LiveState、知识快照、Advice、游戏语音、Viewport、Renderer 事实和持久化分别有唯一写者；任何模块不能拿到“万能上下文”后跨域修改状态。
5. **旧内容绝不复活。** 旧 `runtimeEpoch`、`sessionEpoch`、`stateRevision`、`adviceRevision`、`attemptId`、`viewGeneration` 或 `renderLeaseId` 的迟到回调只能被忽略或脱敏终态化，不能开始播放、重新显示箭头、关闭新建议或把历史显示为当前。
6. **未知优于伪精确。** 字段缺失、GEP gap、版本不兼容、知识过期、窗口定位低置信度或持久化异常时，系统必须明确降级、沉默或退回 Panel；不得伪造精确牌库、精确站位、精确胜率或完整事实。
7. **DataAgent 只查找与保存。** `TftDataAgent` 不分析当前局、不推荐、不写 Advice、不发语音、不画 Overlay、不调用 LLM，也不位于实时等待链。
8. **外部能力默认 Disabled。** 第三方攻略下载、TTS 和 LLM 均需要各自的显式配置、允许范围、最小化出站载荷和独立健康状态；关闭时真实调用路径不存在。

## 4. 运行域与文档地图

```text
                         ┌────────────────────────────┐
                         │ RuntimeSupervisor           │
                         │ epoch / lifecycle / health  │
                         └──────────────┬─────────────┘
                                        │
 Overwolf Bridge ── ingress ──> IngressAndStateDomain ──> DecisionDomain
      │                                 │                       │
      │                                 │                       v
      └── render receipts <── DeliveryDomain <── AdviceCoordinator
                                            │          │       │
                                           Panel    Overlay   Voice

 KnowledgeAndProfileDomain ── frozen facts ────────> DecisionDomain / RichPath
 TftDataAgent / Evidence Ledger ── only local cold path and maintenance
```

| 工程规格 | 主责任 | 基线章节 | 不能拥有的能力 |
|---|---|---|---|
| [运行时与决策规格](2026-07-14-tft-companion-runtime-decision-engineering-spec.md) | 会话、事实、快照、规则、战略、经济、牌库、装备、风险、逻辑站位 | §§3–15、17、20.1、30 | 像素渲染、音频播放、数据库写入、远程调用。 |
| [宿主、存储与 IPC 规格](2026-07-14-tft-companion-host-storage-ipc-engineering-spec.md) | Host、Bridge、双通道 IPC、D 盘根、生命周期、跨进程安全 | §§2、4–7、24–25、30 | 策略仲裁、DataAgent 分析、TTS/LLM 业务决定。 |
| [交付体验规格](2026-07-14-tft-companion-delivery-experience-engineering-spec.md) | 表达 Skill、Advice 生命周期、Panel、Overlay、独立游戏 Voice | §§16–18、22–24 | 修改游戏事实、直接控制游戏、复用 QQ 链路、让表达改变决策。 |
| [知识、LLM 与证据规格](2026-07-14-tft-companion-knowledge-llm-evidence-engineering-spec.md) | 内容包、授权、DataAgent、检索、RichPath、证据与删除 | §§5、8、26–28 | 当前局分析、FastPath 同步查库、当前局上传、模型工具调用。 |
| [质量与发布规格](2026-07-14-tft-companion-quality-release-engineering-spec.md) | 回放、故障注入、性能、隐私审计、发布 Gate | §29 与 §§22–30 验收合同 | 为了测试收集真实原始对局、规避平台规则或绕过失败 Gate。 |
| [PluginMarket 发行规格](2026-07-14-tft-companion-plugin-market-distribution-engineering-spec.md) | 主仓库、Release 制品、Alife.PluginMarket JSON 登记、升级/回滚与外部发布 Gate | 用户确认的发行流程与跨宿主制品边界 | 自动创建 Release、自动提交/推送市场 JSON、猜测最新 schema 或混装 Adapter。 |
| [完整版本路线图](../plans/2026-07-14-tft-companion-complete-system-version-roadmap.md) | `v0.0.1` 至 `v2.0.0` 的范围、依赖、成本和停止条件 | 本文档集全部 | 具体代码任务；每版仍需独立详细计划。 |

## 5. Gate 链与安全降级

版本不是唯一的依赖表达。下面的 Gate 从运行边界到完整系统验收构成不可跨越的链；每个 Gate 失败时的唯一可接受处理都已在相应规格中写明。

```text
G0 私有运行边界与 D 盘根
  → G1 Overwolf / Loopback 兼容性
  → G2 最小实时链路与真实 Renderer 回执
  → G3 Viewport 定位与安全降级
  → G4 Advice / Voice 生命周期
  → G5 本地知识、证据与授权边界
  → G6 可选 RichPath / LLM Provider
  → G7 回放、性能、故障和自定义局验收
```

典型降级原则如下：

| 故障或缺口 | 允许保留 | 必须关闭 |
|---|---|---|
| GEP gap、字段过期或无法重同步 | 不确定的 Panel 状态或通用说明 | 精确站位、精确牌库、依赖缺失事实的主动建议。 |
| Viewport 为 Medium / Low | Panel、Edge Dock、逻辑方向建议 | 棋盘内格位、像素标签、移动箭头。 |
| TTS 或 LLM 不可用 | FastPath、Panel、合格的 Overlay | 等待远程服务后补播过期内容，或把 QQ 链路当回退。 |
| 知识包不兼容、授权失效或索引异常 | 冻结的本地 RulesPack / CompPack | 当前版本的统计结论、RAG 增益、自动下载。 |
| D 盘故障 | 内存中仍可验证的实时核心 | 新持久化发布、缓存声明、C 盘回退。 |
| Loopback PoC 失败 | 文档化的停止状态 | HTTP/文件/SQLite polling 替代；是否评估 Sidecar + Named Pipe 必须重新审阅。 |

## 6. 版本规划纪律

完整路线图覆盖长期系统，不表示所有能力都应被做完。它将价值分成三个可独立停止点：

1. **首期自用闭环：** `v0.0.1` 至 `v0.4.0`，验证只读、Panel、受限 Overlay、可选窄语音和单来源本地知识维护。
2. **完整本地确定性教练：** 后续 `v0.x` 至 `v1.0.0`，补齐连败、阵容路径、经济、牌库/复制器、装备/强化、风险与逻辑站位；仍不依赖远程 LLM。
3. **完整原始设计：** `v1.1.0` 至 `v2.0.0`，才逐步打开完整交付生命周期、进阶语音、知识/证据、可选 RichPath 与全链质量平台。

一个版本只能增加一个可验证的用户闭环或一个必需的风险收敛闭环。版本通过前，不得以新增“更聪明”的功能掩盖安全、回执、隐私、存储或失效问题。

## 7. 后续详细计划的创建规则

本索引与完整路线图的粒度是工程规格和版本 Gate，不是源代码级实施清单。这样做是刻意避免在不存在的项目壳、接口、SDK 与目标机证据上伪造文件路径或测试命令。

只有同时满足以下条件，才可为某一版本创建独立详细计划：

1. 前序版本的 Gate 已有可复查证据，或本版本被明确授权作为独立替代路线重新设计。
2. 所需宿主、目标框架、Overwolf manifest、Provider 能力和本地目录政策已重新核验。
3. 用户明确授权进入该版本的实施规划；详细计划必须单独标明修改文件、测试、回滚和目标机验证，且不能预创建下一版本代码。
4. 若需要修改 `D:\Alife`，先检查其工作树并避开用户未提交修改；若需要跨宿主分发，遵循独立的跨宿主插件兼容设计。

在用户授权前，本工程文档集的正确终点是“范围、接口、Gate、成本和停点已清楚”，而不是启动任何服务或制造空壳代码。完成本地工程版本也不自动授权 GitHub Release 或 Alife.PluginMarket JSON 提交；外部发行必须遵循[PluginMarket 发行规格](2026-07-14-tft-companion-plugin-market-distribution-engineering-spec.md)的 D0–D2 Gate。
