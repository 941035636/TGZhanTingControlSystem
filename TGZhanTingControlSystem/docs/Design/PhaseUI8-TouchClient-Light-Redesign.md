# Phase UI-8 — TouchClient 明亮摄影卡片首页

## 设计目标

将 TouchClient 的默认 `module-kiosk` 首页从深蓝工业控制台视觉调整为明亮、简洁、适用于企业展厅接待的轻科技界面。业务入口、服务端协议和播放链路保持不变；本阶段仅修改 Unity TouchClient 的运行时 UGUI 表现层。

视觉基准：`docs/Design/TouchClient-Home-Light-Reference.png`。

## 原界面问题

- 深蓝色覆盖过多，Shell、内容容器和卡片叠加后整体偏重；
- 发光描边、角标和装饰线重复，模块之间缺少由内容封面带来的识别度；
- 模块墙为单选和右侧操作区，无法直观表达多展区临时组合；
- 无封面时的科技占位占据过多视觉权重。

## 视觉 Token

`TouchTheme` 统一为以下明亮展厅基准：

| Token | 值 | 用途 |
| --- | --- | --- |
| `AppBackground` | `#F5F8FC` | 应用背景 |
| `Surface` | `#FFFFFF` | 内容、顶栏和导航基础表面 |
| `TextPrimary` | `#17345C` | 标题与主要文本 |
| `TextSecondary` | `#64748B` | 辅助文本 |
| `Primary` | `#1677FF` | 选择、高亮与主 CTA |
| `PrimarySoft` | `#EAF3FF` | 选中容器与中性状态 |
| `Border` | `#E3EAF3` | 轻量边界 |
| `Success` | `#18A874` | 真实正常状态 |
| `Warning` | `#E8A23A` | 真实受限状态 |

服务端 Accent 仍未接管语义色、导航或主 CTA。

## 布局与卡片

- 设计基准：1920×1080；内容区采用标题区、模块摄影墙和底部操作栏三段结构。
- 1920×1080：模块墙为 4 列 × 3 行；1280×720 自动切至 3 列，窄于该宽度时为 2 列并保持垂直滚动。
- 卡片优先加载真实 `ExhibitionModule.coverUrl`，使用居中裁切；底部渐变只用于保障白色标题可读性。
- 缺少封面或下载失败时使用明确的浅蓝展厅占位卡，不伪装为甲方正式图片。
- 选中状态仅使用蓝色描边、勾选角标和顺序号，不以纯蓝遮盖照片。
- 图片加载继续复用 `TouchImageLoader`：请求去重、缓存、30 秒失败退避。页面关闭时由 `TouchOperatorUi` 调用 `Dispose`，释放缓存 Sprite/Texture。

## 交互与真实数据

- 点击卡片切换本地选择状态，支持一个或多个模块；模块顺序以当前 Server 内容顺序为准。
- 底部操作栏显示真实选择数量和名称摘要，提供清空选择、全部讲解、开始讲解。
- 开始讲解仍由 `TouchOperatorUi` 调用既有 `TouchControlFacade.StartModules`；全部讲解仍走既有 `TouchControlFacade.StartAll`。
- `Connected`、`SystemReadiness.canStart` 和活动 Session 继续决定按钮可用性；本阶段没有新增客户端假状态，也未绕过 LED 离线门禁或单活动 Session 保护。
- 断线、LED 未就绪、活动讲解中的说明均来自现有 `TouchUiState`。

## 修改文件

- `src/TouchClient/Assets/Scripts/UI/Theme/TouchTheme.cs`
- `src/TouchClient/Assets/Scripts/UI/TouchAppShell.cs`
- `src/TouchClient/Assets/Scripts/UI/Components/ContentHost.cs`
- `src/TouchClient/Assets/Scripts/UI/Components/TopBar.cs`
- `src/TouchClient/Assets/Scripts/UI/Components/SideNavigation.cs`
- `src/TouchClient/Assets/Scripts/UI/Components/StatusBadge.cs`
- `src/TouchClient/Assets/Scripts/UI/Components/TouchUiFactory.cs`
- `src/TouchClient/Assets/Scripts/UI/Services/TouchImageLoader.cs`
- `src/TouchClient/Assets/Scripts/UI/Pages/ModuleKioskHomePage.cs`
- `src/TouchClient/Assets/Scripts/TouchOperatorUi.cs`
- `docs/Design/TouchClient-Home-Light-Reference.png`

## 验证记录

| 项目 | 结果 | 证据/说明 |
| --- | --- | --- |
| TouchClient C# 编译 | PASS | `dotnet build src/TouchClient/TG.Control.Touch.csproj --no-restore`，0 error；7 条既有运行时注入字段警告。 |
| `git diff --check` | PASS | 无空白错误。 |
| 真实状态绑定审计 | PASS | 页面仅消费 `TouchUiState`；启动事件仍由 `TouchOperatorUi → TouchControlFacade` 转发。 |
| 封面加载、失败占位、缓存释放 | PASS（代码审计） | `TouchImageLoader` 继续统一加载，并新增生命周期释放。 |
| 1920×1080 Unity 实际截图 | BLOCKED | Unity 工程正在被交互式编辑器占用，批处理构建被拒绝；该编辑器的当前截图接口也返回 Unity `SetIsBorderRequired` 不支持错误。未以参考图替代运行截图。 |
| 1280×720 Unity 实际截图 | BLOCKED | 同上。布局代码依据当前 viewport 宽度在 4/3/2 列间切换。 |
| 单选、多选、取消与清空 | PASS（代码级） | HashSet 选择状态、卡片 Toggle、清空按钮及真实可用性已编译。待 Unity 交互式复测。 |
| LED 离线门禁、活动 Session 保护 | PASS（代码审计） | 沿用 `TouchUiState.Readiness.canStart` 与 `HasActiveSession`，没有修改播放/Server 协议。 |
| Windows Player 构建 | BLOCKED | 见 Unity 实际截图阻碍。 |

## 未解决项

- 待关闭当前 Unity 编辑器或由现场编辑器完成重新加载后，补采 1920×1080、1280×720、未选择、多选、Server 断线及 LED 离线的真实 Player 截图。
- 真实模块封面取决于管理端已发布的 `coverUrl`。本阶段不生成或硬编码甲方照片。
