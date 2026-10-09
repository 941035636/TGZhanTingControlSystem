# Phase UI-9 Visual Correction — 第一轮视觉还原与测试报告

日期：2026-10-09  
目标分辨率：1920×1080  
视觉基准：`TG_Exhibition_UI_Assets/References/Reference_4.png`、`Reference_6.png`

## 实施范围

- 将欢迎/待机模式与 App Shell 改为互斥显示，欢迎页面显示期间主界面整体停用，避免透明素材导致页面叠加和底层控件误触。
- 重新排版待机页与欢迎语音播放态，仅中央“触碰开启”按钮可触发欢迎流程。
- 将首页调整为明亮展厅风格的 4×3 摄影卡片布局，真实模块仍按发布内容的 `order` 与卡片一一绑定。
- 使用素材包内 12 张 `Composite` 临时卡片做第一轮还原；不再叠加重复的编号、标题、说明和箭头。
- 保留 `TouchControlFacade`、欢迎 Server 协调、正式 Session、LED 播放、MeloTTS 和 180 秒空闲返回逻辑。

## 运行截图

- `01-Standby-1920x1080.png`：待机页面。
- `02-Welcome-Playing-1920x1080.png`：欢迎语音播放视觉状态。
- `03-Welcome-Completed-Home-1920x1080.png`：欢迎结束后的首页切换状态。
- `04-Module-Home-1920x1080.png`：12 模块首页。
- `05-Three-Modules-Selected-1920x1080.png`：前三个模块通过真实卡片 `Button.onClick` 选中后的状态。

以上截图均由 Unity 2020.3.35f1c2 Windows Player 在 1920×1080 帧缓冲中实际运行并通过 `ScreenCapture` 生成；临时截图驱动仅存在于 `artifacts/phase-ui9-visual/BuildProject`，未进入正式 TouchClient 源码。

## 参考图逐项对比

| 检查项 | 结果 | 说明 |
| --- | --- | --- |
| 欢迎页与首页层级 | PASS | 欢迎模式会停用完整 `Touch App Shell`，主界面不再透出，也不接受底层点击。 |
| 待机页科技氛围 | PASS（首轮） | 使用现有可商用集成的独立科技背景、真实文字与独立交互控件，未把 `Reference_4` 整页当按钮。 |
| 欢迎语音播放态 | PASS（视觉） | 播放态隐藏启动按钮与光环，显示真实状态/字幕组件；本轮截图使用受控 `WelcomePlaybackStatus.Playing`。 |
| 首页明亮度与层级 | PASS | 白色半透明展厅画布、清晰标题区、4×3 摄影卡片、底部选择与 CTA 层级已建立。 |
| 12 张模块卡片 | PASS（临时素材） | 已按内容顺序绑定 12 张提取卡片；图片内已有文字，Unity 未重复叠字。 |
| 三模块选中反馈 | PASS | 外框和顺序徽标 `01/02/03` 来自本地真实选择状态。 |
| 1920×1080 裁切 | PASS | 五张截图均为 1920×1080，无页面越界；模块卡、底部操作栏完整。 |

## 素材限制（必须人工知悉）

1. `Reference_4.png`、`Reference_6.png` 是完整合成效果图，不是可直接拆分的生产素材。本轮没有把整页参考图当作不可交互背景。
2. 素材包没有提供 `Reference_4` 中无文字、无按钮的高清展厅底图，因此待机页继续使用工程已有的独立科技背景来还原布局与氛围；这与参考图中的白色实体展厅场景仍有明显差异。
3. 12 张 `Module_*_Composite.png` 约为 340×188，编号、标题、说明、箭头和渐变已经烘焙，属于临时视觉基准，不是 4K/高清独立摄影素材。
4. `Module_01_Composite.png` 自带参考图中的蓝色选中描边。正式交付应由甲方提供 12 张无文字独立照片，再全部改为 Unity 文字、箭头和选中状态。
5. `Reference_5.png` 仅用于氛围观察，未作为 Unity 背景使用。

## 编译与边界验证

| 验证 | 状态 | 证据/结论 |
| --- | --- | --- |
| `TG.Control.Touch.csproj` Release | PASS | 0 error；仅保留 7 个既有 Unity 序列化字段警告。 |
| Unity Windows Player Build | PASS | Unity 2020.3.35f1c2：`Build Successful`。 |
| 12 模块真实内容加载 | PASS | Windows Player 加载 Published Content V1，共 12 个模块。 |
| 页面完成态切换 | PASS | 调用正式 `OnWelcomeEntered` 路径后，欢迎层关闭、Shell 恢复、首页显示。 |
| 三模块选择 | PASS | 真实首页卡片点击事件产生 3 项选择和顺序反馈。 |
| 仅中央按钮可唤醒 | PASS（代码+层级） | 全屏背景不再挂 `Button`；唯一唤醒事件绑定到中央 `Welcome Touch Action`。 |
| 180 秒空闲返回 | PASS（代码审计） | `touchIdleTimeoutSeconds` 默认值仍为 180，检测条件和业务保护未删除；本轮未等待 180 秒做计时实测。 |
| 欢迎音频/Server 协调 | PASS（代码审计） | `WelcomeRequested → TouchControlFacade.RequestWelcome()` 路径未改；Touch 仍不本地播放欢迎音频。 |
| 欢迎请求被拒绝 | PASS（接口+代码） | `409` 返回的类型化 `WelcomePlaybackStatus` 由 Touch 正常消费；LED 离线等失败显示安全进入首页入口，不再永久停留在“正在连接”。 |
| 未配置欢迎音频 | PASS（接口+代码） | Server 在 25 ms 内返回“未配置已校验的欢迎语音素材”；Touch 按既有产品规则直接进入讲解首页。 |
| 终端认证失败 | PASS（代码） | 请求超时或 `401` 会退出忙碌态并显示安全进入首页入口。 |
| 正式欢迎音频端到端播放 | NOT RUN | 本轮只做 TouchClient 视觉整改，未启动 LedPlayer 做音频端到端验收；播放页截图为受控真实 Unity 状态。 |
| Server/Contracts/LedPlayer/MeloTTS 修改 | PASS | 正式源码差异仅涉及 TouchClient UI 和本轮视觉资源。 |

## 当前环境说明

截图时 Server 已运行并提供 Published Content V1，但现场 `C:\ProgramData\TG Exhibition\Config\server.site.json` 对当前非管理员进程返回访问拒绝。直接启动 Server 时若不显式提供与终端一致的现场密钥，会退回 Development 默认密钥并导致 Touch/LED 收到 `401`。本轮复测 Server 已使用与终端配置一致的密钥启动；未修改部署权限或在源码中写死现场密钥。

## 第一轮结论

首轮视觉还原达到“页面互斥、科技待机、明亮首页、12 张摄影卡、真实选择反馈”的目标，可进入人工视觉确认。正式高清交付仍依赖甲方补充独立的无文字欢迎背景和 12 张无文字模块照片。
