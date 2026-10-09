# Phase UI-9 — TouchClient 首页视觉补齐验收记录

日期：2026-10-09。基线分支：`codex/product-upgrade`。本轮没有修改 Server、LedPlayer、MeloTTS 或讲解 Session 逻辑。

## 素材与实现

- 使用 `Assets/TGExhibitionUI/HomeFinal/TG_Home_UI_Kit/TG_Home_UI_Kit/Backgrounds/Home_Light_1920x1080.png` 的独立无 UI 展厅背景；运行时副本位于 `Assets/Resources/Touch/HomeFinal/`，仅铺在首页内容层底部。
- 五枚独立 PNG 导航图标接入 UGUI `Image`，随选中、未选中、不可用状态着色。
- 顶栏品牌由独立圆形 TG 图形和文字组件组成，与待机页文案一致。
- 12 张既有摄影卡片及 4×3 布局保留；底部操作栏仅收紧间距、字号和状态简文案，业务按钮逻辑未变。
- 素材包说明该背景由 1672×941 放大到 1920×1080；它不是原生 4K 摄影素材。

## 实机截图

截图来自 Unity 2020.3.35f1c2 构建的 Windows Player，连接本地 Server 并加载正式内容版本 V1（12 个展区）。测试驱动仅存在于临时 QA 工程，不包含在正式构建中。1920×1080 由实际 Player 的 UGUI 离屏渲染生成，因为测试电脑物理屏幕为 1366×768；1280×720 为 Player 窗口直接截图。

| 验收项 | 结果 | 证据 |
| --- | --- | --- |
| 独立明亮背景、12 卡片、4×3 布局、真实连接状态 | PASS | `01-Home-1920x1080.png` |
| 1280×720 文字与底部操作栏 | PASS | `02-Home-1280x720.png` |
| 左侧导航图标及首页选中态 | PASS | `03-Home-Selected-Navigation.png` |
| 三个展区选中及底部计数 | PASS | `04-Home-Three-Selected-1920x1080.png`；Player 日志 `QA_HOME_SELECTION=已选择 3 个展区` |
| 全部讲解、开始讲解的实际业务流程 | NOT RUN | 本轮仅做首页视觉验收，不把 UI 状态模拟当作业务回归 |
| 甲方人工视觉确认 | NOT RUN | 等待人工确认 |

本轮截图显示本地 Server 在线、系统可接待；这不是对 LED 现场设备的单独验收结论。

Windows Player 输出：`artifacts/phase-ui9-home-final/Windows/TouchClient.exe`。构建日志保存在本机临时 QA 目录。请在人工确认后再决定是否提交或进入下一阶段。
