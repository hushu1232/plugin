# TFT Companion v0.0.1 — Go / No-Go 记录模板

**日期**：______________  
**操作者**：______________  
**机器（脱敏）**：______________  
**结论状态**：`Go` / `No-Go` / `BlockedExternal`

> 不填写 Riot ID、玩家名、真实 match ID、原始 GEP、Origin 原文、pairing token、完整控制台日志、截图或视频。

## A. 本地自动化 Gate

| 检查 | 实际命令 | 结果 | 证据摘要 |
|---|---|---|---|
| .NET 9 restore/build | `C:\Users\hu shu\.dotnet\dotnet.exe restore/build` | Pass / Fail | SDK、warning/error 数 |
| NUnit | `... dotnet.exe test ... --configuration Release` | Pass / Fail | 通过/失败数 |
| Node 边界检查 | `node --test tests\overwolf\static-boundary-check.js` | Pass / Fail | 命名检查结果 |
| bundle npm test | `npm test`（bundle 目录） | Pass / Fail | 命名检查结果 |
| diff 空白检查 | `git diff --check` | Pass / Fail | 无输出 / 原因 |

## B. G0 — D 盘存储与隐私

| 合同 | 结果 | 脱敏证据 | 失败动作 |
|---|---|---|---|
| 唯一可控根是 `D:\AlifeData\TFTCompanion\` | Pass / Fail / BlockedExternal | 状态枚举 | 停止，不增加备用路径 |
| D 盘不可用时 MemoryOnlyDegraded，无 C/AppData/Temp 回退 | Pass / Fail / BlockedExternal | fake/Host I/O 结论 | 停止并修复 root policy |
| `poc-status.json` ≤8192 bytes 且只含白名单字段 | Pass / Fail / BlockedExternal | 大小、字段名 | 停止并移除泄漏 |
| 不保存 raw GEP、标识、token、Origin、URL query 或 renderer 内容 | Pass / Fail | 自动检查/审计结论 | 停止并修复边界 |

## C. G1 — Loopback 协议与配对

| 合同 | 结果 | 脱敏证据 | 失败动作 |
|---|---|---|---|
| Host 仅监听 `127.0.0.1:32173`，无 HTTP health/control 面 | Pass / Fail | 端口/路由结论 | 停止 |
| `/ingest` 和 `/render` 都由同一 Background Bridge 建立 | Pass / Fail / BlockedExternal | bridge instance 一致性 | 停止，不让 Renderer 直连 |
| 精确 HTTP Origin、body Origin、pairing proof、channel、protocol/schema 均实际校验 | Pass / Fail | 命名拒绝码 | 停止 |
| token 不在 URL、状态文件、日志或 Git 中 | Pass / Fail | 边界检查 | 轮换 token 后重测 |
| binary、超大文本、超速消息、错 route/type/runtime/epoch/sequence/session/lease/command 均 fail-closed | Pass / Fail | 命名拒绝码 | 停止 |

## D. G2 — 最小事实、lease 与 Renderer 安全

| 合同 | 结果 | 脱敏证据 | 失败动作 |
|---|---|---|---|
| Bridge 只发送 typed `stateSnapshot`，无 raw GEP 邮箱/持久化 | Pass / Fail | 自动检查 | 停止 |
| 新 render lease 首命令为 `hideAll` | Pass / Fail / BlockedExternal | lease 顺序 | 停止 |
| 错/旧 runtime/epoch/sequence/session/lease/command receipt 不能推进显示，旧 lease 被替换前收到 HideAll | Pass / Fail | 集成/目标机结果 | 停止 |
| `hidden` 单独不会显示 marker | Pass / Fail | Gate 结果 | 停止 |
| 当前 lease 后的权威 snapshot + match/round + fresh 才会 show | Pass / Fail / BlockedExternal | Gate 结果 | 停止 |
| disconnect、reconnect、gap、stale 后先隐藏，旧内容不复活 | Pass / Fail / BlockedExternal | Gate 结果 | 停止 |
| manifest + CSS + `setMouseGrab` 成功与手工点击均证明穿透 | Pass / Fail / BlockedExternal | Pass/Fail | 停止 |

## E. 外部平台证据

| 项目 | 版本/状态 | 结果 | 备注（不含敏感内容） |
|---|---|---|---|
| Overwolf manifest 当前 schema |  | Pass / Fail / BlockedExternal |  |
| 当前 TFT GEP game ID / feature allowlist |  | Pass / Fail / BlockedExternal |  |
| 当前 TFT 字段路径用于存在性语义 |  | Pass / Fail / BlockedExternal |  |
| 实际 Overwolf Origin 可验证 |  | Pass / Fail / BlockedExternal |  |
| 自定义/练习对局 smoke |  | Pass / Fail / BlockedExternal |  |

## 决策规则

- 仅 A–D 全 Pass 且 E 所需项全 Pass 时，填写 `Go`。
- `Fail`、`No-Go` 或 `BlockedExternal` 任何一项都不允许进入 v0.1.0 功能开发。
- `BlockedExternal` 不是通过；记录所需权限/运行时/官方文档后停止。

## 结论与下一步

**结论理由**：

____________________________________________________________________________

**阻断项**：

____________________________________________________________________________

**下一步（只填写已获授权动作）**：

____________________________________________________________________________
