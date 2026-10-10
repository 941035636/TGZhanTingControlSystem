# TouchClient 首页背景小范围视觉修正记录

日期：2026-10-10  
状态：已完成实现与运行截图，等待人工视觉确认；本文不代表最终视觉验收通过。

## 本轮范围

本轮只调整 `ModuleKioskHomePage` 的首页背景层级、显示比例、标题区可读性和浅色容器过渡。未修改 12 模块卡片布局或内部设计，未修改待机、欢迎、Server、LedPlayer、MeloTTS 以及讲解业务逻辑。

## Current State Audit 与修正

| 检查项 | 修正前 | 修正后 |
| --- | --- | --- |
| 父容器 | 背景直接挂在 `Module Kiosk Home Page` 根节点 | 保持挂在页面根节点，但建立 `Frame -> Viewport -> Scene` 独立层级 |
| Canvas 层级 | 背景依赖创建顺序 | `Exhibition Home Scene Frame` 明确设置为首页根节点的第一个子节点 |
| RectTransform | 背景整页 Stretch | Frame 整页 Stretch；Viewport 内缩 1；Scene 居中锚点 `(0.5, 0.5)`、居中 Pivot |
| Image Type | 默认 Simple，未显式声明 | 显式 `Image.Type.Simple` |
| Preserve Aspect | 未显式启用 | 显式启用，并使用 `AspectRatioFitter.EnvelopeParent` |
| 资源导入比例 | Unity NPOT 缩放把 1920×1080 导入为 2048×1024，运行时发生 2:1 变形 | `nPOTScale=None`，保持原始 1920×1080、16:9 |
| 裁切 | 无可靠约束 | Viewport 使用 `RectMask2D`，仅裁掉 Aspect Fill 超出部分 |
| 触摸 | 背景 Image 未统一声明行为 | Frame、Scene、氛围层、标题底层全部 `raycastTarget=false` |
| 标题对比度 | 标题直接覆盖复杂背景 | 新增独立浅色半透明标题底板与细边框 |
| 页面整体遮罩 | 全页白色可读性遮罩，背景层次被压平 | 移除全页遮罩，仅保留 8% 冷色氛围层，文字可读性由局部容器承担 |
| 卡片 | 现有 4×3 摄影卡片 | 未修改；卡片仍使用自身完整不透明背景和文字区 |

## Aspect Fill 实测

运行时 Canvas 设计参考尺寸下：

- 背景 Viewport：`1620.00 × 908.00`
- 背景 Scene：`1620.00 × 911.25`
- 背景定位：`anchoredPosition=(0.00, 0.00)`
- 水平裁切：`0`
- 垂直裁切：总计 `3.25` 个参考像素，即上下各约 `1.625` 个参考像素
- 1280×720 下 Canvas 等比缩放后，上下实际各约 `1.08` 屏幕像素

该裁切来自 16:9 原图填满 1620:908 视口，不存在横向拉伸或任意构图裁切。

## 真实 Windows Player 截图

- [1920×1080 首页](Home-Background-Correction-1920x1080.png)
- [1280×720 首页](Home-Background-Correction-1280x720.png)

两张截图均由 Unity 2020.3.35f1c2 Windows Player 实际运行后采集，使用同一套正式首页代码和真实 Server/LED 状态；截图时 Server 在线、LED 离线。

## 验证结果

| 项目 | 结果 | 说明 |
| --- | --- | --- |
| TouchClient C# 编译 | PASS | 0 warning，0 error |
| Unity Windows Player 构建 | PASS | `Build Successful` |
| 1920×1080 无裁切/遮挡 | PASS | 顶栏、导航、标题、12 卡片、底部操作栏均完整 |
| 1280×720 无裁切/遮挡 | PASS | 同一 1920×1080 参考布局按 CanvasScaler 等比缩放 |
| 背景不拦截触摸 | PASS（代码审计） | 所有背景与标题装饰 Image 均关闭 Raycast Target |
| 4×3 卡片布局 | PASS | 未改动布局和卡片构建代码 |
| 五个导航图标与选中态 | PASS | 未改动 SideNavigation，截图中显示正常 |
| 最终视觉验收 | PENDING | 等待人工确认，不在本轮自行判定 PASS |

## 素材构图限制

当前背景原图的主要视觉焦点位于画面中央，而首页核心信息本身就是覆盖中央区域的 12 张卡片。两者天然存在面积竞争。本轮没有为“展示背景”而缩小卡片或破坏信息密度，而是把整幅图作为卡片间隙、边缘和底部的环境氛围层使用，并以局部浅色标题底板保证可读性。

如果人工确认后仍希望明显增加背景可见面积，更合理的后续方案是只把该素材用于标题区和外围边缘装饰，或另行提供中央留白、焦点位于两侧的专用首页背景；不建议让当前中央构图与 12 卡片争夺同一区域。
