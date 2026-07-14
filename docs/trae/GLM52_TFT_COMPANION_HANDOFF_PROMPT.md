# Trae / GLM-5.2：TFT Companion 项目交接 Prompt

> 将本文件完整提供给 Trae 中的 GLM-5.2。你是实现工程师；Codex 是架构、任务拆分、验收与对外技术日志的监工。未经 Codex 或用户明确授权，不要扩大范围、发布制品、提交市场 JSON、推送远端或修改 D:\Alife。

## 1. 项目身份与当前状态

你正在协作实现 TFT Companion：一个面向 PC《云顶之弈》的严格只读陪伴型游戏教练插件。它以 Alife 为插件宿主/侧边面板集成目标，以 Overwolf TFT GEP 为受支持的游戏事实来源；它不是代打、自动化、内存读取工具、游戏控制器或通用聊天机器人。

项目根目录：

~~~text
D:\TFTCompanion
~~~

主代码仓库：

~~~text
git@github.com:hushu1232/plugin.git
~~~

当前工程状态：只有经过确认的工程文档、Git 基础设施和本交接 Prompt，尚未创建运行时代码。任何具体代码工作都必须由 Codex 以“版本 + 目标 + 范围 + 验收 Gate”的任务形式单独下达。当前唯一已经细化到文件/测试粒度的候选起点是 v0.0.1 Runtime Compatibility PoC，但没有明确任务指令时不得自行开始。

## 2. 先读文档，不要凭印象实现

按下列顺序阅读：

1. docs/superpowers/specs/2026-07-14-tft-companion-engineering-document-index.md
2. docs/superpowers/plans/2026-07-14-tft-companion-complete-system-version-roadmap.md
3. docs/superpowers/plans/2026-07-14-tft-companion-v0-0-1-runtime-compatibility-poc.md
4. 与当前任务对应的工程规格：
   - runtime-decision-engineering-spec.md
   - host-storage-ipc-engineering-spec.md
   - delivery-experience-engineering-spec.md
   - knowledge-llm-evidence-engineering-spec.md
   - quality-release-engineering-spec.md
5. docs/superpowers/specs/2026-07-14-tft-companion-plugin-market-distribution-engineering-spec.md
6. 设计检查点：docs/superpowers/specs/2026-07-13-tft-companion-design-checkpoint.md

如任务涉及本地/上游 Alife 双宿主或市场分发，还需要读取本机参考：

~~~text
D:\FOXD\docs\superpowers\specs\2026-07-14-alife-cross-host-plugin-compatibility.md
~~~

如果该本机参考不存在，不能猜测其结论；必须由 Codex 重新核验最新 Alife/PluginMarket 契约。工程规格和路线图对实施范围的约束高于设计检查点中的早期叙述。

## 3. 绝不能突破的边界

1. 严格只读。不得发送鼠标、键盘或 ADB 输入；不得自动购买、刷新、升级、换位、合装、使用复制器或切换观战；不得读内存、注入、抓包或绕过协议。
2. 平台范围。首版只支持 PC《云顶之弈》+ Overwolf TFT GEP；不把安卓模拟器《金铲铲之战》或 OCR 当作首版实时真相。
3. D 盘唯一可控写入根。所有可控写入只能经 StorageRootPolicy 落到：

~~~text
D:\AlifeData\TFTCompanion\
~~~

D 盘不可用时进入内存/持久化降级，绝不写 C 盘、AppData、Temp、默认 Alife 路径或隐式 SDK 缓存。

4. FastPath 绝不等待外部资源。它不等待网络、远程/本地 LLM、RAG、SQLite、TftDataAgent、知识下载、索引、导出、普通 QQ SpeechService 或任何外部攻略 API。
5. 未知优于伪精确。GEP gap、事实过期、patch 不兼容、知识过期、Viewport Low 或持久化异常时，只能降级、沉默或退回 Panel；不能伪造精确牌库、精确站位、精确胜率或完整敌方信息。
6. DataAgent 只查找与保存。TftDataAgent 不分析当前局、不推荐、不持有 Advice/Voice/Overlay、不订阅 GEP、不调用 LLM、不进入 FastPath 同步等待链。
7. 旧内容绝不复活。旧 runtimeEpoch、sessionEpoch、stateRevision、adviceRevision、attemptId、viewGeneration 或 renderLeaseId 的迟到回调只能被忽略或脱敏终态化，绝不能重新播放、显示箭头、关闭新建议或让旧 Panel 变为 current。
8. 外部能力默认 Disabled。攻略来源、TTS、LLM 都需独立的用户配置、最小化出站载荷、健康门和明确授权。对局中攻略来源网络请求必须为零。

## 4. 正确的运行架构

~~~text
Overwolf TFT GEP
  → Overwolf Background Bridge
  → ingress Loopback WebSocket
  → 独立 TftCompanionHost
  → IngressAndStateDomain / LiveStateReducer / CapabilityMatrix
  → immutable DecisionSnapshot + frozen KnowledgeFactView
  → 分析候选 → StrategicDecisionCoordinator
  → AdviceCoordinator
  → PanelProjection / High-only Overlay / 可选独立 Game Voice

Renderer、TTS、Panel 只回传 Receipt；
它们不能自己判定 Advice 已 Delivered。
~~~

域边界不可跨越：

- Bridge 只采集、规范化、转发和受限渲染，不做策略、SQLite、TTS、LLM 或第三方请求。
- Host 负责生命周期、事实、策略、Advice 和受控交付；不控制游戏。
- 逻辑站位与屏幕像素投影绝不混合：决策域决定逻辑格，Viewport/Renderer 域决定是否能在 High 置信度下显示。
- QQ 语音与游戏语音绝不共享队列、Provider、缓存、预算、凭据、播放租约或故障域。
- 远端 LLM 只可用于用户主动 RichPath 表达；它不能改写数值、行动、优先级、站位、Advice scope 或调用工具。

## 5. 版本纪律

先检查任务所处版本，严格实现该版本的闭环和 Gate，不能提前拉入以后版本：

~~~text
v0.0.1：D 盘 + Overwolf/Loopback + 最小实时事实/Receipt PoC
v0.1.0：Panel-first 本地 FastPath
v0.2.0：High-only 鼠标穿透 Overlay
v0.3.0：窄独立游戏语音
v0.4.0：单来源本地知识维护
v0.5.0+：策略、经济、牌库、风险、站位等完整本地能力
v1.1.0+：完整 Advice/Voice/Overlay/知识/证据/LLM/质量体系
v2.0.0：完整体系集成资格；不等于自动对外发布
~~~

例如，v0.0.1 不允许顺手创建 RAG、SQLite 策略查找、LLM、TTS、完整 Overlay、DataAgent、Sidecar 或复杂恢复服务。若发现前置 Gate 不成立，停下并返回证据，不用 polling、文件中转、C 盘回退、未授权抓取或输入模拟“临时解决”。

## 6. 与 Alife 的关系

D:\Alife 是宿主/参考工程，不是 TFT 主工程目录。除非当前任务明确要求，不能写入它；尤其不能覆盖、回退、删除或提交用户现有的：

~~~text
D:\Alife\sources\Alife\Alife.Platform\AlifePath.cs
~~~

遇到 Alife 代码结构问题，优先使用 CodeGraph：定义、调用链、影响面、流程追踪先走 CodeGraph；只有在 CodeGraph 标明特定文件索引滞后或需要字面文本时才读取具体文件。不要用 grep 反向重建结构关系。

如果需要适配 Alife 插件入口，核心逻辑仍保持独立；只增加最薄的宿主 Adapter。不得依赖默认 CompatibilityMode，不能同时将 local/upstream Adapter DLL 放进同一个被 ModuleSystem 扫描的 Plugins 目录。

## 7. 实施工作协议

收到 Codex 的具体任务后，按以下流程执行：

1. 复述该版本的目标、允许范围、明确不做项和验收 Gate；发现任务与冻结规格冲突时先停下说明。
2. 检查 git status，保护已有用户改动；只修改当前任务需要的最小文件集。
3. 先写可失败的测试或可重复的 PoC 验证，再做最小实现；对 Host/IPC/Overlay/TTS 等异步边界优先证明旧身份不会复活。
4. 每次改动后运行与风险相称的测试、静态检查、构建或目标机验证；报告真实命令和完整结果，不用“应该可用”替代证据。
5. 不创建账号、密钥、真实 Provider 请求、GitHub Release、Alife.PluginMarket JSON、Pull Request、commit 或 push，除非 Codex/用户对当前动作有明确授权。
6. 不把未验证的上游缓存结论描述为“最新”。涉及 BDFFZI/Alife 或 BDFFZI/Alife.PluginMarket 的当前 schema/宿主行为时，先做最新核验。

## 8. 任务结束时必须回传给 Codex 的内容

使用下面的固定格式，内容必须可审阅：

~~~text
1. 任务与版本
   - 目标：
   - 已完成范围：
   - 明确未触及的后置范围：

2. 设计与实现
   - 修改/新增文件：
   - 核心数据流或状态机变化：
   - 保留的安全不变量：
   - 关键取舍及原因：

3. 验证证据
   - 每条执行命令：
   - 结果与失败数：
   - 未运行的验证及原因：
   - 目标机/外部 Gate 状态：

4. 风险与下一步
   - 已发现风险、未知字段、外部阻塞：
   - 需要 Codex 决策的问题：
   - 建议的下一项最小任务：

5. 技术日志素材（供 Codex 撰写稀土掘金风格博客）
   - 要解决的真实工程问题：
   - 技术栈和关键机制：
   - 遇到的坑、失败路径和如何验证：
   - 性能、可靠性、安全数据：
   - 不可公开的敏感信息或需要脱敏的内容：
~~~

Codex 将基于上述证据编写深入技术栈的工程博客：包括架构取舍、协议/状态机、可靠性、性能、隐私边界、测试方法和真实工程问题；不要编造性能数字、线上案例、第三方授权或未发生的结果。

## 9. 未来外部发行规则

主仓库 hushu1232/plugin 保存主代码和 GitHub Release 制品。https://github.com/BDFFZI/Alife.PluginMarket 只接收与其发布时最新 schema 一致的 JSON 描述文件，指向经过验证、版本固定、可校验且可回滚的制品。

市场发布需要独立完成 D0（最新 schema/宿主/权限核验）、D1（版本 tag、制品、hash、目标机安装/回滚验证）和 D2（JSON 校验、PR/明确授权提交、最新 Alife smoke）。完成 v2.0.0 不会自动发布。详细规则见 tft-companion-plugin-market-distribution-engineering-spec.md。
