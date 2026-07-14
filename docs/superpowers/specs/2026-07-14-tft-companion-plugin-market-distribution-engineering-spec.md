# TFT Companion Alife PluginMarket 发行工程规格

> **状态：** 已确认的未来发行路径；本文件只保存发布契约和 Gate，不授权创建 GitHub Release、修改 `BDFFZI/Alife.PluginMarket`、创建 Pull Request、推送 JSON 或发布插件制品。

**目标：** 将 TFT Companion 的主代码、发布制品与 Alife 插件市场登记分成可验证的三个职责层：主仓库承载代码和版本，GitHub Release 承载可下载制品，`BDFFZI/Alife.PluginMarket` 只承载与其最新 schema 一致的插件 JSON 描述。

**架构：** `D:\TFTCompanion` 是主工作区，Git `origin` 为 `git@github.com:hushu1232/plugin.git`。每个经过验证的插件版本在主仓库创建明确的、可回滚的制品发布；市场仓库仅引用该版本制品和其完整性/兼容性元数据。最新 Alife 从 PluginMarket 的登记信息解析、下载、安装并加载目标宿主对应的插件制品。

**关联资料：** [完整工程文档索引](2026-07-14-tft-companion-engineering-document-index.md)、[完整版本路线图](../plans/2026-07-14-tft-companion-complete-system-version-roadmap.md)、`D:\FOXD\docs\superpowers\specs\2026-07-14-alife-cross-host-plugin-compatibility.md`。

---

## 1. 固定的仓库职责

```text
D:\TFTCompanion
  └─ git@github.com:hushu1232/plugin.git
       ├─ 主代码、工程文档、测试、构建与发布标签
       └─ GitHub Release：目标宿主对应的可下载插件制品

https://github.com/BDFFZI/Alife.PluginMarket
  └─ 最新 Alife 插件市场登记：插件 JSON 描述文件

https://github.com/BDFFZI/Alife
  └─ 宿主运行时与 PluginMarket 消费方，不是 TFT 主代码仓库
```

| 位置 | 允许内容 | 明确不放入 |
|---|---|---|
| `hushu1232/plugin` 主仓库 | TFT Companion 代码、Overwolf 工程、Host、Core、薄 Adapter、测试、文档、构建脚本、版本 tag、Release 制品来源 | Alife.PluginMarket 的他人插件登记、第三方私有凭据、未授权攻略内容。 |
| GitHub Release | 已验证、版本固定、可下载、可校验的插件包与面向用户的变更说明 | 未固定分支快照、开发中临时包、源码树替代的安装制品、密钥。 |
| `BDFFZI/Alife.PluginMarket` | 与最新市场 schema 相符的 JSON 描述和市场所需的最小元数据 | 主代码、TFT 原始对局数据、TTS/LLM 凭据、用户设置、运行日志、攻略包、音频或诊断。 |
| `BDFFZI/Alife` | 最新宿主契约、PluginMarket 安装/加载/升级行为的核验依据 | 作为 TFT 主业务仓库、默认数据根或 QQ/TFT 服务混用位置。 |

## 2. 发行制品边界

插件市场登记的是**可安装制品**，不是一个指向主分支源码的临时链接。每次发布至少有以下稳定关系：

```text
主仓库已验证 commit
→ 不可变版本 tag
→ 目标宿主对应的构建制品 / ZIP
→ VERSION.txt 与完整性信息
→ GitHub Release 固定下载地址
→ PluginMarket JSON 的 release metadata
→ 最新 Alife 安装、加载与回滚验证
```

制品必须遵守此前的跨宿主兼容结论：共享 Core 可以复用，但面向不同 Alife 目标框架/宿主的 Adapter 与制品必须分开。一个被 `ModuleSystem` 扫描的 `Plugins` 目录中只能出现当前宿主对应的一个 Adapter DLL；不能把 local/upstream 两个 Adapter DLL 一起打进同一可扫描目录或同一个“万能 ZIP”。

发布包应只包含当前版本、当前宿主实际需要的文件。依赖、环境准备、`VERSION.txt`、入口模块、升级/卸载影响和 hash/签名信息的具体格式必须以发布时最新 PluginMarket schema 为准。

## 3. PluginMarket JSON 的责任与不变量

市场 JSON 的职责是描述“插件是什么、哪个制品可安装、适用于谁、如何验证”，而不是承载策略或运行数据。无论最新字段名如何变化，描述信息必须覆盖以下语义：

| 元数据域 | 必须表达的语义 |
|---|---|
| 身份 | 稳定 plugin ID、显示名称、简介、作者/主页和版本。 |
| 制品 | 固定 Release/ZIP 下载位置、版本、`VERSION.txt` 兼容关系、hash/完整性校验和大小或等效验证信息。 |
| 兼容性 | 目标 Alife/框架、操作系统/运行环境、所需 Adapter、入口模块、依赖和环境声明。 |
| 生命周期 | 变更说明、升级条件、卸载/回滚行为、已知限制和最小支持版本。 |
| 来源 | 主仓库、Release、许可证/归因和可追溯的版本标签。 |

JSON 中不得出现：GitHub 或 Provider token、TTS/LLM 密钥、用户身份、当前游戏数据、外部攻略内容、调试日志、绝对本机路径或依赖 C 盘的配置。

当前不把字段名、目录层级、JSON 示例或依赖表达硬编码到本规格中。原因是 PluginMarket 仓库的 `Plugin` / `PluginRelease` schema、ZIP 规则、依赖/环境字段、安装器和市场分支在正式发布时可能已经改变；“看起来像旧版本”的 JSON 不能作为发布依据。

## 4. 正式发布流程

### Gate D0：最新契约重新核验

在任何市场 JSON 改动前，先读取并核验当时最新的：

1. `BDFFZI/Alife.PluginMarket` 默认分支、贡献流程、JSON schema、目录/文件命名和校验规则；
2. `BDFFZI/Alife` 的目标框架、`ModuleSystem`、模块入口、市场安装/升级/卸载/回滚逻辑；
3. 目标插件版本的兼容矩阵、依赖来源、许可证、release 制品格式和 `VERSION.txt` 语义；
4. 该市场是否需要 Pull Request、审核或具有直接写入权限。

如果 schema、宿主契约、制品格式或权限状态无法核验，则停在 D0；不提交猜测的 JSON，不把缓存上游结论说成“当前最新”。

### Gate D1：主仓库制品准备

主仓库的版本必须先通过该版本自身的测试和目标机 Gate，之后才允许：

1. 固定经过验证的 commit 与语义化版本 tag；
2. 为当前目标宿主构建单一 Adapter 对应的发行制品；
3. 验证包内容、入口、依赖、`VERSION.txt`、hash、许可证和没有秘密/运行数据；
4. 创建指向不可变制品的 GitHub Release，并记录可回滚的上一稳定版本；
5. 在隔离的目标机/安装路径上验证安装、加载、卸载和回滚，而不是只验证 ZIP 能下载。

缺少任一项时，市场 JSON 不得指向未验证制品。

### Gate D2：市场 JSON 登记

只在 D0 和 D1 都有证据后：

1. 获取 PluginMarket 的当时最新基线；
2. 按实际 schema 创建或更新该插件的 JSON 描述；
3. 检查 plugin ID 稳定性、版本单调性、下载 URL、hash、目标环境、依赖、入口和 release metadata；
4. 用市场提供的校验/测试方式验证 JSON；
5. 以市场要求的 Pull Request 或经明确授权的直接提交方式提交；
6. 在最新 Alife 中执行真实安装、加载、升级与回滚 smoke，保留脱敏结果而不是用户运行数据。

只有用户明确授权时才可以进行 GitHub Release、市场仓库 clone/pull、创建 Pull Request、提交或 push。本规格不会把“完成 v2.0.0”解释成自动对外发布。

## 5. 升级、回滚与失败规则

| 问题 | 唯一处理 |
|---|---|
| PluginMarket schema 或目录规则变化 | 回到 D0 重新生成描述；不复用旧 JSON 模板。 |
| 主制品 hash/依赖/入口/版本不一致 | 停在 D1，重新构建/验证；不指向可变分支或手工修补已发布 ZIP。 |
| 两个宿主 Adapter 被混装或市场无法选择目标制品 | 停止 D1/D2，重新拆分制品或扩展市场 schema 后再评审。 |
| 市场无直接写权限 | 按其贡献规则创建 Pull Request；不尝试绕过审核。 |
| 安装、升级、卸载或回滚 smoke 失败 | 撤回/不合入市场登记，保留上一稳定 release；不让用户用手工复制 DLL 作为发布替代。 |
| 主仓库、Release 或市场任一远端不可用 | 保持本地开发/测试，不声称已发布，也不伪造市场状态。 |

## 6. 与 TFT 版本路线的关系

市场发行是**独立的外部交付 Gate**，不属于 `v0.0.1` 的运行兼容性 PoC，也不应阻塞 `v0.1.0` 至 `v1.0.0` 的本地只读功能验证。即使达到 `v2.0.0`，也只表示完整工程体系具备受控发布资格；实际执行 D0–D2 仍需单独授权。

当前已知状态仅为：`D:\TFTCompanion` 是本地 Git 工作区，`origin` 已配置为 `git@github.com:hushu1232/plugin.git`，工程文档尚未提交，未创建 Release，未访问/修改 PluginMarket，也未推送任何代码或 JSON。
