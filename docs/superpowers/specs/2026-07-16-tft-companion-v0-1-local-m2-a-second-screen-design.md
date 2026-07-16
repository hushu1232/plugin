# TFT Companion v0.1-local / M2-A 第二屏手动陪练设计

> **状态：** 设计方向已确认；本文等待用户审阅；尚未授权实现。  
> **版本边界：** 本文只定义 `v0.1-local / M2-A`。它不是 `v0.1.0`，不改变 `TargetMachineGate = Pending / BlockedExternal`，不证明真实 Overwolf、TFT GEP、TFT 棋盘、D 盘目标机行为、Alife Panel 或公共商店发布能力。  
> **VCS 边界：** 本设计不授权 `git add`、commit、push、tag、reset、clean、restore、stash、checkout、PR 或 Release。

## 1. 目标与产品定位

M2-A 提供一个独立于 Overwolf 的 Windows 第二屏陪练窗口。它让用户在回合结束时主动提交少量有界事实，并以 M1 已验证的本地生命周期安全链路显示一条当前教育性建议。

```text
用户手动启动 WPF 窗口
→ 用户开始陪练并提交 Quick Checkpoint
→ M1 LocalCompanionEngine / LocalAdviceCoordinator
→ ManualPanelProjectionHarness
→ SecondScreenPresentationSkill
→ 单一 Current Advice Card
```

M2-A 的产品定位是“陪伴与教练”，不是实时读局、自动操作、游戏内叠加层或策略指挥器。

## 2. 已确认的设计决策

| 决策 | 已确认结论 |
|---|---|
| UI 技术栈 | WPF + .NET 9；不引入额外 NuGet 包。 |
| 显示形态 | 普通、可拖动、可缩放的 Windows 窗口，用户手动放到第二显示器。 |
| 非目标 | 非 Overlay、非透明鼠标穿透窗口、非浏览器、非 PWA、非 localhost 服务。 |
| 启动方式 | 用户手动启动；无开机自启、托盘常驻、后台 watcher、游戏进程检测、自动开窗、自动置顶、自动聚焦或自动移动窗口。 |
| 输入形态 | 单页、按场景动态展开的 Quick Checkpoint；无自由文本。 |
| 陪练会话 | 一个训练主题对应一个跨多个回合的 `ManualRunId`；每次提交递增 revision。 |
| 建议密度 | 仅一张 Current Advice Card；无建议历史流、自动通知、语音、弹窗、战局日志或数据库。 |
| 持久化 | 仅保存最小会话恢复快照到 D 盘；不保存历史、截图、游戏数据或渲染文本。 |
| 静态知识 | 不属于 M2-A；作为独立 M2-B 设计。 |
| 官方赛后复盘 | 不属于 M2-A；作为受外部 API Gate 约束的 M2-C。 |
| Overwolf | 保留为未来可选事实适配器；M2-A 不调用。 |
| OCR | `Deferred Backup Research`；不属于 M2-A 或已批准主线。 |

## 3. 严格边界

M2-A 不得引入或使用：

```text
Overwolf、GEP、Host、WebSocket、HTTP、localhost、网络请求、下载、浏览器、PWA、
截图、OCR、视频/窗口捕获、游戏进程检测、内存读取、注入、抓包、反向工程、
鼠标/键盘/ADB/热键、自动买牌/刷新/升本/换位、SQLite、建议历史、战局日志、
LLM、RAG、DataAgent、TTS、QQ Speech、真实 TFT 数据、敌方棋盘数据。
```

插件可控的运行时写入仅允许位于：

```text
D:\AlifeData\TFTCompanion\
```

禁止任何 C 盘、`AppData`、`Temp` 或默认用户目录回退。

`D:\Alife`，尤其是 `D:\Alife\sources\Alife\Alife.Platform\AlifePath.cs`，不属于 M2-A 范围。

## 4. 架构与依赖方向

```text
TftCompanion.SecondScreen (WPF)
    │
    ├─ View / ViewModel
    ├─ SecondScreenPresentationSkill
    ├─ ManualSessionController
    ├─ ManualSessionRecoveryStore
    │
    ▼
TftCompanion.Poc.Core (M1)
    ├─ LocalCompanionEngine
    ├─ LocalAdviceCoordinator
    ├─ EmbeddedFixtureExpressionSkill
    └─ ManualPanelProjectionHarness
```

依赖必须单向：

```text
WPF → Core
Core ↛ WPF
Core ↛ SecondScreen
SecondScreen ↛ Host / Overwolf / Alife
```

WPF ViewModel 只允许消费 `ManualPanelProjection` 与 M2-A 的展示状态模型。它不得直接消费或渲染 `SemanticAdvice`。

## 5. 渲染与玩家可见语言边界

M1 已锁定的事实保持不变：

```text
ManualPanelProjectionHarness.Project(coordinator, now)
= M1 唯一具备 lifecycle-safe 语义的渲染入口。

EmbeddedFixtureExpressionSkill.TryRender(advice, out rendered)
= 纯语义 formatter，不是任意历史 advice 的 stale-safe 授权器。
```

M2-A 增加一个专属 `SecondScreenPresentationSkill`，其职责仅是把 `ManualPanelProjection` 的状态转换为用户可见的状态说明，例如 Unknown、Cleared、Expired、RecoveryDegraded。

语言规则：

```text
陪练正文：只能来自 EmbeddedFixtureExpressionSkill。
状态说明：只能来自 SecondScreenPresentationSkill。
ViewModel：不得拼接陪练或状态性自然语言。
reasonCode：不得直接作为用户陪练文本显示。
固定按钮标签：可作为普通 UI resource，不属于陪练建议。
```

未来 M2 的任何 UI、Panel、语音队列或显示消费者只能消费 `ManualPanelProjection`，不得绕过 `LocalAdviceCoordinator + ManualPanelProjectionHarness`，把历史 `SemanticAdvice` 直接传给 `EmbeddedFixtureExpressionSkill`。

## 6. Quick Checkpoint 与会话语义

### 6.1 单页布局

```text
┌─────────────────────────────────────────────────────────────┐
│ TFT Companion · Manual / FixtureOnly                         │
│ 会话状态 | 当前 Revision | 恢复状态 | 精度状态               │
├──────────────────────────────┬───────────────────────────────┤
│ Quick Checkpoint             │ Current Coach                 │
│ - 训练主题                   │ - 来源与状态                  │
│ - 当前意图                   │ - 一条当前陪练正文            │
│ - 场景必填的枚举字段         │ - 状态性说明                  │
│ [提交本回合快照]             │ [清除当前建议]                │
│                              │ [开始新陪练]                  │
└──────────────────────────────┴───────────────────────────────┘
```

首期不保存窗口位置或尺寸。用户自行将窗口放到第二显示器，应用不检测显示器、缩放、窗口焦点或游戏窗口。

### 6.2 场景字段

```text
LossStreakReview：Intent、HealthBand、GoldBand。
RerollReview：Intent、GoldBand、CopiesBand、UnitCostBand。
```

仅显示当前场景需要的有界枚举字段；不允许玩家输入自由文本、对局 ID、回合 ID、GEP sequence、敌方信息或棋盘内容。

### 6.3 会话状态机

```text
NoSession
  → EditingCheckpoint
  → CurrentAdvice
  ├─ Submit: revision + 1，替换当前建议
  ├─ Clear: Cleared
  ├─ ExpiresAt: Expired
  └─ Start New Session: 新 ManualRunId

Cleared
  → 同一 run 的更高 revision 可继续提交

Expired
  → 新快照或新 session 后重新计算

任何状态
  → D 盘不可用或恢复无效：RecoveryDegraded
```

行为不变量：

```text
同一 run 的 revision 必须单调增加。
Clear 后旧 revision 不得复活。
开始新陪练后旧 run 不得复活。
切换训练主题不得静默复用旧 run。
不完整 checkpoint 不得提交。
```

### 6.4 过期处理

窗口打开且存在 Current Advice 时，WPF 可使用仅属于 UI 生命周期的 `DispatcherTimer` 安排一次 expiry refresh：

```text
Current Advice 出现
→ 等待至 ExpiresAt
→ 在 UI 线程调用 Project(now)
→ 过期后停止 timer

窗口重新激活
→ 先调用 Project(now)
→ 再显示 Projection
```

它不是后台服务、游戏轮询、文件 watcher 或进程监控。窗口关闭后 timer 必须随窗口销毁。

## 7. D 盘最小恢复快照

### 7.1 固定路径与 provisioning

恢复文件固定为：

```text
D:\AlifeData\TFTCompanion\manual-session-v1.json
```

M2-A 不得复用 `poc-status.json`，因为它属于 v0.0.1 Host 的运行状态边界。

现有 D 盘策略禁止普通运行时自动创建目录或文件。M2-A 的规则为：

```text
普通启动 / Submit / Clear：只 OpenExisting。

用户明确点击“启用 D 盘恢复”：
  1. 先验证既有 D:\AlifeData\TFTCompanion\ canonical root；
  2. 不创建 D:\AlifeData 或 TFTCompanion 根目录；
  3. 只允许创建唯一固定文件 manual-session-v1.json；
  4. 创建后立即使用同句柄检查最终路径、卷 GUID、reparse point 与 link count；
  5. 验证失败时进入 MemoryOnlyDegraded；
  6. 不创建任意其他文件名，不回退其他目录。
```

根目录不存在时，应用必须拒绝 provisioning 并显示由 `SecondScreenPresentationSkill` 生成的降级状态；未来安装器或明确部署步骤可预置根目录，但不属于 M2-A。

### 7.2 快照内容

`ManualSessionRecoverySnapshot` 仅含：

```text
schemaVersion
snapshotGeneration
ManualRunId
highestRevision
sessionPhase
FixtureScenarioId
Topic
Intent
HealthBand
GoldBand
CopiesBand
UnitCostBand
Provenance
FixturePackVersion
CreatedAt
ExpiresAt
canonical payload digest
```

明确不保存：

```text
RenderedText、完整 Advice 历史、旧 Advice 集合、自由文本、对局/回合 ID、
GEP 数据、敌方信息、截图、OCR、语音、LLM、RAG、游戏窗口或网络数据。
```

快照必须有固定大小上限，并使用 canonical UTF-8 payload 的 SHA-256 digest 验证完整性。M2-A 的恢复承诺是“正常关闭后尽力恢复、损坏后绝不伪恢复”；不承诺在电源中断或写入中断时保留最后一次状态。

### 7.3 写入与恢复

仅在以下用户主动状态变化后写入：

```text
开始新陪练
提交本回合快照
清除当前建议
```

写入顺序：

```text
用户操作
→ 内存中的 M1 状态转换完成
→ 创建有界恢复 DTO
→ 写入固定 D 盘文件（WriteThrough）
→ 成功：RecoveryAvailable
→ 失败：MemoryOnlyDegraded
```

写入失败不回滚当前内存建议，但 UI 必须提示本次会话无法恢复。不得尝试 C 盘、`AppData`、`Temp` 或其他回退位置。

恢复顺序：

```text
OpenExisting
→ 文件大小 / schema / digest / 枚举 / 路径验证
→ 使用冻结 FixturePack 重新 Evaluate
→ Project(now)
→ 只显示重新计算后仍有效的 ManualPanelProjection
```

恢复文件中的任何缓存 RenderedText 均不被信任或读取。快照损坏、版本不匹配、枚举非法、路径不可信或 advice 已过期时，应用必须安全进入 Unknown / Expired / RecoveryDegraded，不能显示旧建议。

## 8. 测试 Gate

### 8.1 会话与生命周期

必须证明：

```text
开始陪练产生新的 ManualRunId。
同一 session 的提交 revision 单调递增。
Clear 后旧 revision 不再显示。
新 session 激活后旧 run 不再显示。
切换主题不复用旧 run。
不完整 checkpoint 不提交。
ExpiresAt 后 Current Advice Card 消失。
窗口重新激活先刷新 expiry。
```

### 8.2 表达与 UI 边界

必须证明：

```text
ViewModel 只消费 ManualPanelProjection。
ViewModel 不直接调用 EmbeddedFixtureExpressionSkill.TryRender。
状态说明只来自 SecondScreenPresentationSkill。
reasonCode 不直接显示为陪练文本。
```

### 8.3 D 盘恢复与降级

必须证明：

```text
未 provisioning 时为 MemoryOnlyDegraded。
显式 provisioning 只创建固定 manual-session-v1.json。
日常路径只 OpenExisting。
路径逃逸、reparse point、hard link、卷不匹配均被拒绝。
超尺寸、schema/digest 不匹配、非法枚举、JSON 损坏均不恢复旧建议。
恢复重新 Evaluate + Project，不读取 RenderedText。
恢复后过期建议不显示。
D 盘写失败保留内存陪练，但显示不可恢复状态。
无 C 盘、AppData 或 Temp fallback。
```

### 8.4 手工 smoke

在不启动游戏、Host、Overwolf、模拟器、截图或网络服务的前提下，验证：

```text
启动窗口、手动放到第二显示器、开始陪练、提交多个 checkpoint、
Clear、开始新 session、显式 provisioning、关闭并重启恢复有效 session、
恢复过期 session、D 盘不可用或快照无效时安全降级。
```

## 9. M2-A 明确不做项

```text
Overwolf / GEP、Host、真实 TFT、游戏内 Overlay、透明鼠标穿透层、浏览器、PWA、
localhost、HTTP、WebSocket、OCR、截图、录像、游戏进程检测、窗口控制、热键、
自动操作、静态知识包、官方赛后 API、建议历史、SQLite、TTS、LLM、RAG、
DataAgent、敌方棋盘、实时棋盘、牌库、自动游戏事实采集。
```

## 10. 验收与估算

M2-A 只有同时满足下列条件才可交接为本地第二屏陪练闭环：

```text
用户无需启动游戏即可演示 Start / Submit / Clear / New Session。
窗口只显示一个 lifecycle-safe Current Advice Card。
有效会话可从 D 盘恢复；D 盘失败时进入明确的内存降级。
无 C 盘/AppData/Temp fallback。
无 Overwolf、Host、HTTP、WebSocket 或后台常驻。
WPF build、M2-A 测试、M1 回归、边界扫描与手工 smoke 全部通过。
所有表述保持 FixtureOnly / Manual / Educational，不伪装成实时游戏能力。
```

在不引入安装器、网络、静态知识包或真实游戏集成的前提下，估算为：

| 子项 | 预估净工程时间 |
|---|---:|
| WPF 壳、ViewModel、Quick Checkpoint | 2–3 天 |
| ManualSessionController 与 M1 集成 | 1–2 天 |
| SecondScreenPresentationSkill | 0.5–1 天 |
| D 盘 provisioning 与最小恢复快照 | 3–4 天 |
| Storage 安全回归、恢复/降级测试 | 2–3 天 |
| WPF smoke、全量回归与交接 | 1–2 天 |
| 合计 | **9–15 净工程天** |

自然日估算约为 **2–3 周**，不包含安装器、发布签名、Overwolf 审核、Riot API 权限、静态内容授权或真实游戏目标机集成。

## 11. 后续阶段

```text
M2-B：离线、版本化、来源可追溯的静态知识包。
M2-C：受 Riot 官方 API / 权限 Gate 约束的赛后复盘。
Overwolf：可选 runtime fact adapter；TargetMachineGate 仍 Pending。
OCR：Deferred Backup Research；不是 M2-A、M2-B 或已批准主线。
```
