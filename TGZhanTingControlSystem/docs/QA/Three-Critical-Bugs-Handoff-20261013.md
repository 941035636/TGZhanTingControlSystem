# 三项现场关键 Bug 修复与交接记录（2026-10-13）

## 1. 交接基线

- 工作目录：`D:\src\TGZhanTingControlSystem`
- 工作分支：`codex/product-upgrade`
- 记录前基线：`b395357b647eca7e8b181550cf00884ae374a929`
- 现场拓扑：Server、中控端 TouchClient、LedPlayer 分别部署在三台 Windows 主机。
- Server 地址：`192.168.86.40`
- LedPlayer 地址：`192.168.86.42`
- 旧播放系统兼容端口：UDP `7002`，本轮修改不得破坏。

本记录只说明当前三项现场关键问题的原因、处理过程、验证证据和剩余风险。`artifacts/` 下的运行日志和构建包属于本机产物，不纳入 Git；关键结论已经写入本文，换电脑后不依赖这些本地产物也能继续开发。

## 2. 总体进度

| 问题 | 源码修复 | 本机验证 | 甲方三机验证 | 当前结论 |
| --- | --- | --- | --- | --- |
| 欢迎词停在“LED端正在校验欢迎音频” | 已完成 | 已通过协议及真实 Windows LedPlayer 测试 | 待验证 | 本地 PASS，现场 NOT RUN |
| 正式视频停在第一帧，必须暂停再继续 | 已完成 | 正常首播路径通过 | 目标 RTX A4000 故障路径待验证 | 本地 PASS，现场 NOT RUN |
| LED 待机文字、蓝线及待机媒体被旧配置覆盖 | 大部分完成 | 防旧页面覆盖与重启持久化通过 | 首次全新安装仍有时序缺口 | 部分 PASS，冷启动 FAIL/待修复 |

当前安装包不能标记为最终验收包。应先补齐第三项“全新安装首次启动自动应用服务器待机媒体”的最小修复，再统一构建并赴现场验证。

## 3. Bug 一：欢迎词 Prepare 后没有可靠进入 Play

### 3.1 现场现象

中控端点击待机页入口后，一直显示“LED端正在校验欢迎音频”。现场 LedPlayer 日志证明：

- 已收到欢迎词 Prepare 请求；
- MP3 已下载并通过缓存校验；
- 已成功解码，时长约 `11.337s`、双声道、`44100Hz`；
- 但没有稳定进入实际播放状态，中控端也无法结束校验等待。

因此问题不在音频格式、文件下载或 SHA-256，而在 Prepare 与 Play 的指令时序。

### 3.2 根因

早期热修复在欢迎请求创建后立即启动 Play 重试，没有等待 LedPlayer 完成解码并上报 `Preparing`/已准备确认。

现场设备解码速度比开发机慢，过早到达的 Play 找不到已经准备好的请求。客户端兼容逻辑会取消并重新开始 Prepare；后续重复 Play 又可能再次打断准备，形成“不断准备、始终无法开始”的竞争。开发机速度快，因此不容易复现。

### 3.3 已实施修复

涉及文件：

- `src/Server/TG.Control.Server/WelcomeCoordinator.cs`
- `src/LedPlayer/Assets/Scripts/LedWelcomePlaybackController.cs`
- `src/LedPlayer/Assets/Scripts/LedApiClient.cs`

修复策略：

1. Server 创建欢迎请求时只发送 Prepare。
2. 只有收到 LedPlayer 的 `Preparing`（素材已准备）状态后，Server 才启动 Play 投递。
3. 每个请求只允许一个 Play 投递循环，避免重复循环互相干扰。
4. Play 采用有界重试；收到 `Playing`、`Completed`、`Failed` 或超时后立即停止。
5. 请求开始时固定本次使用的欢迎音频描述，避免管理端草稿在播放过程中变化。
6. LedPlayer 在等待 Play 时每 `0.75s` 重复上报准备完成，抵抗一次状态请求丢失。
7. LedPlayer 忽略重复启动命令，并增加音频输出初始化失败日志与恢复描述信息。

### 3.4 已完成验证

- 协议顺序：初始仅 Prepare；准备确认前没有 Play；准备确认后仅投递 Play；Playing 后不再投递。
- Windows LedPlayer：真实 MP3 完成缓存、解码、开始播放，Server 状态按 `Preparing → Playing → Completed` 变化。
- 音频元数据：时长 `11.337s`、2 声道、`44100Hz`。
- 本地验证日志：`artifacts/WelcomeHotfixValidation-20261013/LedPlayer-runtime.log`（仅本机，不进 Git）。
- 本地结果：`artifacts/WelcomeHotfixValidation-20261013/welcome-regression-result.json`（仅本机，不进 Git）。

### 3.5 仍然存疑/待现场验证

- 尚未在甲方 Server、TouchClient、LedPlayer 三台独立设备上重新验证完整链路。
- 自动化和日志只能证明播放组件已进入输出状态；甲方现场扬声器实际听音仍为 `NOT RUN`。
- 现场验证时必须保留 Server 与 LedPlayer 同一 requestId 的 Prepare、Preparing、Play、Playing、Completed 日志，不能只看中控提示。

## 4. Bug 二：正式视频只显示第一帧，暂停后继续才播放

### 4.1 现场现象

甲方 LedPlayer 使用 NVIDIA RTX A4000 和 AVPro Media Foundation 硬件解码。日志显示：

- 收到 Prepare 和 PlayVideo；
- AVPro 使用 `MF-MediaEngine-Hardware`；
- 已报告第一张可渲染帧；
- 屏幕却停在第一帧；
- 操作员在中控端执行暂停、继续后才真正播放。

### 4.2 根因

部分 Windows 硬件解码环境中，AVPro/Media Foundation 会同时满足 `IsPlaying=true` 和“已有第一帧”，但底层播放时钟或画面投递实际上没有持续前进。旧代码把“第一帧出现”直接当作播放成功并向 Server 上报 Playing，因此无法自动恢复。

### 4.3 已实施修复

涉及文件：

- `src/LedPlayer/Assets/Scripts/LedPlaybackController.cs`

修复策略：

1. PlayVideo 后进入真实进度确认，不再只检查 `IsPlaying` 或首帧。
2. 同时确认帧号继续增长，且播放时间至少前进 `0.12s`。
3. 如果首帧或进度停滞，LedPlayer 内部自动执行一次等价恢复：`Pause → 等待 0.08s → Seek 到计划时间加已过时间 → Resume`。
4. 最多自动恢复 3 次，总确认窗口不超过约 `2.5s`。
5. 只有确认持续播放后才向 Server 报告 Playing；超过窗口则报告明确失败，不再虚报成功。
6. 未修改 UDP `7002` 旧播放协议兼容逻辑。

### 4.4 已完成验证

- 本机真实 Windows LedPlayer、真实 MP4、AVPro 硬件路径验证通过。
- 测试命令只有 Prepare → PlayVideo，没有人工或 Server Pause/Resume。
- 首帧约 `0.050s`；帧号从 1 增长到 6；时间从 `0.050s` 增长到 `0.181s`；约 `0.216s` 确认持续播放。
- 约 `120.257s` 正常完成。
- 本地日志：`artifacts/WelcomeHotfixValidation-20261013/VideoFirstStart-runtime-2.log`（仅本机，不进 Git）。
- 本地结果：`artifacts/WelcomeHotfixValidation-20261013/video-first-start-regression-result.json`（仅本机，不进 Git）。

### 4.5 仍然存疑/待现场验证

- 开发机显卡为 AMD Radeon 530，未复现甲方 RTX A4000 的原始冻结，因此本地通过的是正常首播路径。
- 自动恢复分支已按现场症状实现并通过代码/构建检查，但必须在 RTX A4000 设备上实测。
- 现场验收要求：首次点击开始后不触碰暂停/继续，3 秒内画面和播放时间必须持续前进；若恢复触发，应在日志中看到自动恢复记录。

## 5. Bug 三：LED 待机文字、蓝线和待机媒体配置回退

### 5.1 现场现象

1. LedPlayer 待机页面仍显示旧品牌文字、状态文字和两条蓝色横线。
2. 管理端配置待机视频后能播放，但随后发布其他界面配置时，待机视频会被旧配置覆盖。
3. 重启 LedPlayer 主机后，可能回到旧的带文字页面，需要再次上传/发布待机视频。

### 5.2 已确认根因

根因由三部分组成：

1. `LedStatusOverlay` 仍会创建旧版品牌、标题、副标题、状态文本、遮罩和横线对象。
2. AdminWeb 保存的是完整 `UiExperienceConfig`。旧浏览器页面持有旧版本配置时也能直接保存，Server 没有版本冲突检查，因此一次与 LED 无关的发布也可能把新的待机媒体字段覆盖回旧值。
3. LedPlayer 过去没有可靠持久化最后一次成功的界面配置，重启时过度依赖再次从 Server 收到配置。

### 5.3 已实施修复

涉及文件：

- `src/LedPlayer/Assets/Scripts/LedStatusOverlay.cs`
- `src/Server/TG.Control.Server/UiExperienceRepository.cs`
- `src/Server/TG.Control.Server/Program.cs`
- `src/AdminWeb/src/main.ts`
- `src/AdminWeb/src/ui-media-validation.ts`
- `src/AdminWeb/tests/ui-media-validation.test.ts`

修复策略：

1. 不再创建待机遮罩、地平线和顶部蓝线。
2. 为兼容旧场景/序列化对象，保留旧字段，但强制隐藏品牌、标题、副标题、状态和连接文字；待机页仅显示管理端配置的图片/视频，未配置时只显示纯底色。
3. 成功收到的界面配置写入 `Application.persistentDataPath/led-ui-experience.json`，LedPlayer 启动时优先加载。
4. Server 保存界面配置时要求请求版本必须等于当前版本；旧页面保存返回 HTTP 409，不允许静默覆盖新配置。
5. AdminWeb 对版本冲突给出重新加载提示。
6. 上传校验已支持甲方 `4096×2304` 等 4K 待机视频，不再限制为 1920×1080。

### 5.4 已完成验证

- 发布包含待机视频的 V2，即使旧的品牌/状态显示标记为 true，Player 仍只打开媒体，不显示旧文字和蓝线对象。
- 发布仅修改 TouchClient 的 V3 后，LED 媒体类型和 URL 保持不变。
- 使用旧 V1 再保存时返回 HTTP 409，服务器继续保持 V3。
- 在已经成功收到并持久化配置后重启 Windows Player，日志显示先加载本地 V3，再打开缓存视频。
- 测试结束后已把本地服务数据恢复为 V4 原始逻辑值。
- 本地结果：`artifacts/WelcomeHotfixValidation-20261013/led-idle-regression-result.json`（仅本机，不进 Git）。

### 5.5 已确认的剩余缺陷（下一步最高优先级）

全新安装或清空本地配置后的第一次启动仍存在事件订阅时序缺口：

1. `LedApiClient.OnEnable` 可能先启动界面配置轮询并成功取得服务器配置。
2. 此时 `LedStatusOverlay.Start` 还没有订阅配置事件，首次事件被丢失。
3. `LedApiClient` 已把内部版本号更新为最新，之后服务器未变更就不会再次发事件。
4. 因为本地持久化文件尚不存在，首次启动可能只显示纯底色，直到管理端再次发布配置。

这不会恢复旧文字和蓝线，但会导致“服务器已经配置待机图片/视频，新装 Player 首次启动却不显示媒体”。该项当前应标记为 `FAIL/待最小修复`。

建议的最小修复：

1. `LedApiClient` 保存最近一次成功取得的 `UiExperienceConfig`，暴露只读属性。
2. `LedStatusOverlay` 完成事件订阅后，立即读取并应用该属性；或者把订阅提前到 `OnEnable`，同时保留一次状态回放。
3. 使用全新 persistentDataPath、服务器预先配置待机视频的条件启动 Player，不进行二次发布，确认第一次启动直接显示媒体并生成本地持久化文件。
4. 继续验证离线重启、在线重启、仅修改 Touch 设置、旧版本 HTTP 409 四种情况。

像素级截图本轮未完成，因此“实际屏幕完全无文字、无蓝线”的最终人工视觉确认仍为 `NOT RUN`；源码和构建对象层面的隐藏已完成。

## 6. 同批次其他相关改动

当前工作树还包含与现场部署直接相关的既有修改，应一并保留：

- Server 安装/启动不再打包或强制依赖 MeloTTS/Python 模型。
- 管理端固定账号为 `admin`，密码为 `tg001`。
- Server 升级安装时增加停止旧进程/服务及部署健康检查。
- AdminWeb 待机媒体支持 4K 视频尺寸验证。
- TouchClient 4K/高 DPI 适配与科技蓝退出确认框。
- UDP `7002` 旧播放端兼容能力保留。

## 7. 公司电脑继续开发的推荐顺序

1. 拉取 `origin/codex/product-upgrade`，不要切回或合并旧 `main` 作为开发基线。
2. 首先修复第五节所述 LedPlayer 首次冷启动配置回放缺口，只做最小改动。
3. 用全新本地配置目录验证“服务器已有待机视频、无需再次发布、Player 首启即显示”。
4. 构建 LedPlayer，在 RTX A4000 现场设备验证首次视频播放，禁止人工先点暂停/继续。
5. 在三台设备验证欢迎词完整状态链和扬声器实际输出。
6. 三项全部通过后，再统一生成 Server、TouchClient、LedPlayer 三套独立安装包；不要把当前本机安装包直接标记为最终交付包。

## 8. 最终验收判定标准

- 欢迎词：一次点击后完成 Prepare、Play、Playing、Completed，实际扬声器出声，不重复叠加，不长期停留在校验状态。
- 视频首播：首次开始后 3 秒内画面与时间持续前进，无需人工暂停/继续。
- LED 待机：只有管理端配置的图片/视频或纯底色；没有旧文字和蓝线；其他页面发布不得覆盖；全新安装和重启都能自动恢复。
- 兼容性：UDP `7002` 功能继续可用，正式 Server 播放命令不受影响。
