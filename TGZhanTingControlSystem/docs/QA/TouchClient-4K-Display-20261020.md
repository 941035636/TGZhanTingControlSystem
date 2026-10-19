# TouchClient 4K 字体与显示适配验证（2026-10-20）

本阶段延续已通过的图片展示页视觉修改，仅调整 TouchClient 的显示输出与文字清晰度基础；未更改播放、图集、欢迎或 Server 业务逻辑，也未提交或推送 Git。

## 最小改动

- Windows Player 在启动时使用主显示器当前原生桌面尺寸请求 `FullScreenWindow`，避免旧 Player 分辨率偏好把4K界面先固定在1080P再放大。不写死3840×2160，1080P设备仍以自身原生尺寸运行。
- 保持1920×1080的 Canvas 逻辑设计基准，开启 Screen Space Overlay 的像素对齐。没有把控件和字体尺寸整体乘2。
- 保留 UGUI 动态字体；Unity 2020.3 的动态 `Text` 按 Canvas 实际缩放倍数请求字形。增加中文字体候选，不进行全局 TMP 迁移。
- 分辨率稳定后记录帧缓冲、桌面尺寸、Canvas 倍率、字体与 DPI；仅在稳定后仍不匹配时报警。

## 验证结果

| 检查项 | 状态 | 实际结果及证据 |
| --- | --- | --- |
| 中文字体覆盖与2倍 Canvas 字形像素密度 | PASS | 隔离 Unity 验证输出 `HIGH_DPI_VERIFICATION PASS`，见 `artifacts/Touch4K-20261020/unity-build.log`。这是受控2倍缩放验证，不是4K实机验收。 |
| 原有图集选择、预览与投放事件回归 | PASS | `artifacts/TouchGalleryTech-20261019/Project/artifacts/gallery-d-editor-verification.json`。 |
| Unity 2020.3.35f1c2 Windows Player 构建 | PASS | `artifacts/Touch4K-20261020/unity-build.log` 正常退出，播放器在 `artifacts/Touch4K-20261020/WindowsPlayer/`。此目录为验证构建，不是正式安装包。 |
| 本机原生分辨率启动 | PASS | 1366×768桌面上得到 `framebuffer=1366x768, desktop=1366x768, canvasScale=0.71, pixelPerfect=True`，见 `artifacts/Touch4K-20261020/runtime-local.log`。 |
| 3840×2160实际 Windows Player 截图和文字锐度 | NOT RUN | 当前开发设备实际显示器为1366×768；不能把受控2倍字体测试或放大图片当作4K实机截图。 |
| 4K触控命中、窗口/画面位置、长时间显存趋势 | NOT RUN | 需要甲方55英寸4K触控设备。 |

已知素材边界：待机页目前仍加载 `Resources/Touch/standby-clean-background` 对应的1920×1080背景。它在4K上会等比放大；本阶段没有替换为另一套4K分层素材，以免改变已确认的待机视觉。文字与UGUI线条的高清适配不能消除这张原图自身的分辨率限制。

## 甲方4K设备验收口径

在3840×2160且中控屏为主显示器的 Windows 设备上启动新 TouchClient，查看运行日志中的 `Touch display:` 行。预期：

`framebuffer=3840x2160, desktop=3840x2160, mode=FullScreenWindow, canvasScale=2.00, pixelPerfect=True, dynamicFont=True`

还需保存**原始3840×2160实际运行截图**，人工检查中文文字、导航图标、正式卡片及图片展示页预览是否清晰，并点击各页面主要按钮确认触控位置与视觉位置一致。若日志中的帧缓冲仍为1920×1080，不能判定4K通过，应先检查中控屏是否为主显示器及 Windows 显示设置。
