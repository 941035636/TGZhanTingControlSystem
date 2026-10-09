# Phase UI-9 — 最终待机页面视觉还原验收记录

日期：2026-10-09
分支基线：`codex/product-upgrade` / `f51f75c`
视觉依据：`src/TouchClient/Assets/TGExhibitionUI/StandbyFinal/Standby_Visual_Reference_1920x1080.png`

## 实施范围

- 只修改 TouchClient 的 `WelcomeExperiencePage` 视觉层，并将已提供的纯净背景作为 `Resources/Touch/standby-clean-background.png` 打包。
- 参考图没有被用作运行时背景；品牌、时间、文字、触摸按钮和手势均为独立 UGUI 组件。手势由运行时白色透明图形生成，不含参考图背景像素。
- 固定待机背景不再被管理端旧版 `welcome.background` / `touchBackgroundUrl` 配置替换，以避免重新出现深蓝科技通道；其他页面背景配置未改。
- `TouchControlFacade`、Server、LedPlayer、MeloTTS、正式讲解 Session 和 180 秒空闲判定代码均未修改。

## 实际 Unity 截图

| 画面 | 文件 | 取得方式 |
| --- | --- | --- |
| 待机完整画面 | [01-Standby-1920x1080.png](01-Standby-1920x1080.png) | Windows Player 运行中，Unity UGUI 离屏原生 1920×1080 渲染 |
| 触摸按钮细节 | [02-Touch-Button-Detail.png](02-Touch-Button-Detail.png) | 同一帧缓冲中的按钮区域直接读取，非合成效果图 |
| 1280×720 适配 | [03-Standby-1280x720.png](03-Standby-1280x720.png) | Windows Player 1280×720 窗口直接截图 |
| 2560×1440 适配 | [04-Standby-2560x1440.png](04-Standby-2560x1440.png) | Windows Player 运行中，Unity UGUI 离屏原生 2560×1440 渲染 |
| 欢迎播放视觉状态 | [05-Welcome-Playing-1920x1080.png](05-Welcome-Playing-1920x1080.png) | 对真实 Unity 页面注入受控 `WelcomePlaybackStatus.Playing`；未播放 LED 音频 |
| 欢迎完成进入首页 | [06-Welcome-Completed-Home-1920x1080.png](06-Welcome-Completed-Home-1920x1080.png) | 对真实 Unity 页面注入受控 `Completed`，通过正式 `OnWelcomeEntered` 进入 12 模块首页 |

本机显示器实际为 1366×768，Windows 会将 1920×1080 和 2560×1440 的窗口请求限制到桌面尺寸；因此这两档用 Unity Player 内的 `RenderTexture` 目标尺寸渲染并读取真实 UGUI 帧缓冲。它们验证布局和像素输出，不等同于对应物理显示器现场验收。临时截图驱动仅在系统临时目录的工程副本中，未进入正式 TouchClient 源码或正式 Windows Player。

## 与最终参考图逐项对比

| 项目 | 结果 | 观察 |
| --- | --- | --- |
| 主标题位置 | PASS（待人工视觉确认） | 位于穹顶中轴、地球上方；实际画面约比参考图低数像素，未触碰地球。 |
| 数字地球完整度 | PASS | 地球、光环、展台完整；没有大面积不透明 UI 覆盖。地球仍是静态背景图，不具备真实 3D 旋转。 |
| 触摸按钮位置 | PASS | 按钮位于地球与展台下方的地面中轴，入口文字在其下方；1280 和 2560 均未出屏。 |
| 背景显示质量 | PASS（当前素材） | 星空、两侧城市与反光地面均显示；原素材说明记录它由 1672×941 放大到 1920×1080，2560 显示会有一定软化。 |
| 品牌、时间、文字清晰度 | PASS | TG 圆标与品牌独立排版；时间来自系统时钟，格式 `yyyy-MM-dd HH:mm:ss`；白字在星空和地面上可读。 |
| 页面整体视觉比例 | PASS（待人工视觉确认） | 1920 与 2560 的 16:9 布局保持一致，1280 窗口内没有遮挡或裁切。 |

## 功能与构建检查

| 检查 | 状态 | 证据与边界 |
| --- | --- | --- |
| Unity 2020.3.35f1c2 Windows Player 构建 | PASS | 正式项目副本构建日志 `artifacts/phase-ui9-standby-final/unity-build-third.log`：`Build Successful`；正式输出 `artifacts/phase-ui9-standby-final/Windows/TouchClient.exe`。 |
| 背景资源打包 | PASS | 正式 Player 截图显示新的纯净展厅背景，旧科技通道未出现。 |
| 点击唤醒事件与防重复请求 | PASS（受控运行） | 对真实按钮执行一次 `Button.onClick` 后 `IsRequestPending=True`；原有 `requesting` 防重入判断未改。未实测快速多点触控硬件行为。 |
| 待机 / 欢迎 / 首页互斥 | PASS（受控运行） | 日志 `QA_STANDBY_EXCLUSIVE=True`、`QA_PLAYING_EXCLUSIVE=True`、`QA_HOME_EXCLUSIVE=True`；首页截图无欢迎层残留。 |
| 180 秒空闲返回 | PASS（判定路径） | 首页状态下注入已超过 180 秒的上次操作时间，日志 `QA_IDLE_RETURN_EXCLUSIVE=True`。没有真实等待 180 秒；真实计时等待为 NOT RUN。 |
| 标题淡入、按钮呼吸及切换淡入淡出 | PASS（代码与运行路径） | 使用少量 UGUI 图形与协程，受控完成态经淡出后进入首页；没有引入 3D 场景。尚待用户观感确认动画节奏。 |
| LED 欢迎音频端到端播放 | NOT RUN | 本轮只做 TouchClient 视觉；欢迎播放截图为受控状态，不代表 LED 真实音频已播放。 |
| 正式讲解 Session 中空闲保护 | NOT RUN | 相关逻辑未改，但本轮没有开启正式 Session 做运行验收。 |
| 1920 / 2560 物理显示器现场检查 | NOT RUN | 本机物理显示器只有 1366×768；使用 Unity 原生尺寸离屏渲染验证布局，仍需现场屏幕确认。 |

## 已知问题与交付边界

1. 参考图与纯净背景都是由较小原图放大到 1920×1080；2560×1440 可用，但图像细节不是真正 2K/4K 原生素材。
2. 本轮没有 LED 播放端参与，不能据欢迎播放状态截图宣称音频链路通过。
3. 触摸按钮的呼吸和页面渐变已经运行，但节奏与强度属于视觉判断，等待人工确认后再决定是否调整。
4. 正式 Windows Player 在 `artifacts/phase-ui9-standby-final/Windows/`，临时 QA Player 在同级 `QA-Windows/`；后者含截图驱动，不能交付甲方。

结论：Unity 实际视觉与指定参考图的主要构图接近，技术检查完成；**等待人工视觉确认，不自动进入下一阶段。**
