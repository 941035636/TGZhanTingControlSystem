# Phase UI-11 — TouchClient 深蓝科技风首页视觉重构

日期：2026-10-10  
状态：实现、构建和运行截图已完成，等待人工视觉确认；未自行判定最终视觉验收 PASS。

## 实施前审计

- 当前分支：`codex/product-upgrade`
- 实施前 HEAD：`9e290df95d35dc431735adbff9ef50f166426a73`
- 实际启用首页：`ModuleKioskHomePage`
- `ReceptionHomePage` 与 `RouteCard`：保留的旧首页模板和路线卡，本轮未修改
- `TouchControlFacade`：仍是 UI 业务入口，本轮未修改
- 待机、欢迎、欢迎音频、180 秒空闲返回：仍由现有 `WelcomeExperiencePage`、`TouchOperatorUi` 和 Facade 流程负责，本轮未改写
- 实施前存在上一轮未提交的浅色首页背景修正及用户新放入的 `HomeDark` 素材包；均未删除或回滚

## 视觉实现

### 首页专属深蓝模式

没有把全局 `TouchTheme` 直接替换成深色主题。`TouchAppShell` 增加首页专属视觉模式：

- 进入实际 12 模块首页时，TopBar、SideNavigation、ContentHost 切换为深蓝玻璃视觉；
- 离开首页进入路线、播放或系统状态页面时，立即恢复原有浅色产品视觉；
- QA 实测进入系统状态页后深色背景已关闭，未污染其他页面；
- 视觉切换不复制导航或业务逻辑。

### 素材使用

- 运行时背景：`Home_Dark_Abstract_1920x1080.png`
- 运行时导航图标：`nav_home/nav_mic/nav_status/nav_route/nav_topic_128.png`
- `Approved_Visual_Reference.png` 只用于对照，没有作为运行时页面背景
- 背景导入保持原始 `1920×1080`，关闭 NPOT 缩放和 UI 不需要的 Mipmap
- 背景使用独立 UGUI Image，`Image.Type=Simple`、`Preserve Aspect=true`、`raycastTarget=false`
- 所有文字、状态、按钮、卡片和选择结果仍由独立 UGUI 与真实业务状态驱动

### 页面结构

- 顶部：深蓝半透明状态栏，保留 TG 品牌、实时时钟、真实 Server 与 LED 状态
- 左侧：深蓝半透明导航，使用素材包五个图标和真实选中/禁用状态
- 内容区：深蓝玻璃面板、细蓝描边、低强度科技背景
- 标题：白色“展厅讲解”、浅蓝辅助文字与克制蓝色强调
- 卡片：保留原有 4×3 摄影卡片、模块数据和点击热区，只调整外框状态色
- 底部：深蓝操作栏、真实选择数量、真实就绪状态、原有清空/全部讲解/开始讲解动作

## 真实运行截图

1. [1920×1080 首页](01-Home-Dark-1920x1080.png)
2. [1920×1080 三模块选中](02-Home-Dark-Three-Selected-1920x1080.png)
3. [1280×720 首页](03-Home-Dark-1280x720.png)
4. [2560×1440 Unity 高分辨率渲染](04-Home-Dark-2560x1440.png)
5. [1920×1080 待机页](05-Standby-1920x1080.png)
6. [待机进入首页的过渡中间态](06-Standby-To-Home-Transition-1920x1080.png)
7. [离开首页后恢复浅色 Shell](07-NonHome-Light-Shell-Restore-1920x1080.png)

截图均来自 Unity 2020.3.35f1c2 Windows Player 实际运行。截图时真实状态为 Server 在线、LED 离线，没有伪造就绪状态。

## 分辨率边界

- `1920×1080`：真实窗口运行与截图
- `1280×720`：真实窗口运行与截图
- `2560×1440`：QA 工作站显示器最大分辨率为 1920×1080，Windows 会把 2560×1440 窗口限制回 1920×1080；因此使用 Unity 原生 `ScreenCapture` 2×高分辨率渲染，从 1280×720 运行帧生成实际 2560×1440 图片
- 三种输出均保持 16:9；Canvas 参考布局为 `1648×936`，12 张卡片均为 `396.50×211.33` 参考单位，无文字溢出、卡片裁切或按钮重叠

## 验证结果

| 项目 | 状态 | 结果 |
| --- | --- | --- |
| TouchClient Release 编译 | PASS | 0 error；7 个既有 Unity 序列化字段警告 |
| Unity Windows Player 构建 | PASS | `Build Successful` |
| 12 模块加载 | PASS | 真实正式内容 V1，12/12 显示 |
| 4×3 卡片布局 | PASS | 三种输出无裁切 |
| 三模块选择顺序 | PASS | 显示 01/02/03，底部显示“已选择 3 个展区” |
| 清空选择 | PASS | 调用真实按钮事件后活动选择徽标为 0 |
| Server/LED 真实状态 | PASS | Server 在线、LED 离线正确显示 |
| 待机进入首页 | PASS | 现有 FadeToHome 流程进入深蓝首页，无页面叠加 |
| 离开首页恢复原视觉 | PASS | 系统状态页恢复浅色 Shell，深色背景未激活 |
| 全部讲解/开始讲解实际启动 | NOT RUN | 现场 LED 离线，遵守真实门禁，不构造假在线状态；事件绑定与业务路径未修改 |
| 最终人工视觉验收 | PENDING | 等待甲方/用户确认 |

## 参考图差异与当前限制

1. 素材包明确说明当前独立背景是抽象深蓝科技底图，不是参考图中的完整展厅建筑背景。因此当前页面没有参考图里的星空穹顶、城市窗景、镜面大厅和中央地球，不能声称 100% 还原。
2. 当前背景的优势是不会与 12 张摄影卡片争夺视觉焦点，整体可读性更稳定；代价是空间纵深和华丽程度低于批准参考图。
3. 卡片继续使用现有临时合成摄影卡素材，其中标题、编号和箭头已经烘焙在图片中。本轮按要求未重新设计卡片。
4. 光效刻意控制在细描边、选中态和主按钮范围，没有复制参考图中过强的全屏霓虹泛光。
5. 如后续要求更接近参考图，需要单独提供无文字、无卡片、无状态栏的 1920×1080 展厅建筑背景；不应从批准合成图裁切获取。

## 业务边界确认

本轮没有修改 `TouchControlFacade`、Server、LedPlayer、MeloTTS、Session、播放协议、TTS 生命周期、模块顺序、选择顺序或欢迎业务状态机。`TouchOperatorUi` 仅增加一行首页视觉模式切换调用。

## 本轮代码与运行资源

- `Assets/Scripts/TouchOperatorUi.cs`
- `Assets/Scripts/UI/TouchAppShell.cs`
- `Assets/Scripts/UI/Theme/TouchTheme.cs`
- `Assets/Scripts/UI/Components/TopBar.cs`
- `Assets/Scripts/UI/Components/SideNavigation.cs`
- `Assets/Scripts/UI/Components/ContentHost.cs`
- `Assets/Scripts/UI/Components/StatusBadge.cs`
- `Assets/Scripts/UI/Pages/ModuleKioskHomePage.cs`
- `Assets/Resources/TGExhibitionUI/HomeDark/`

本轮未提交、未推送，等待人工视觉确认。
