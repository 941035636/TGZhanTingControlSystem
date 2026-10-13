# LedPlayer 时钟偏差与旧中控暂停切换修复记录

日期：2026-10-13  
分支：`codex/product-upgrade`

## 1. 处理范围

本次只修改 LedPlayer，处理以下三个现场问题：

1. 中控开始讲解后，视频停在第一帧，必须人工“暂停→继续”后才能播放。
2. 点击待机欢迎入口后，TouchClient 长时间停留在“LED端正在校验欢迎音频”。
3. UDP 7002 旧中控第一次点击暂停有效，第二次点击同一个暂停按钮不能恢复播放。

Server、TouchClient、正式 Session 状态机和新中控的 Pause/Resume 指令语义均未修改。

## 2. 视频首播与欢迎词卡住的共同根因

### 2.1 现场证据

- Server 和 LED 播放端能够正常建立连接。
- 视频 Prepare、缓存复用、AVPro 打开媒体和 FirstFrameReady 均已成功。
- 视频 Prepare 耗时约 0.131 秒，不是下载、缓存或解码过慢。
- Server 已发送 PlayVideo，但 LED 在人工 Resume 前没有进入持续播放。
- 欢迎音频能够完成下载、SHA-256 校验和解码，日志已记录真实时长、声道数和采样率。
- 现场 Server 时间约为 20:31，LED 播放端时间约为 20:29，存在约两分钟偏差。
- 手工把两台电脑时间校准后，视频首播与欢迎词播放均恢复正常，证实问题来自设备时钟偏差。

### 2.2 代码缺陷

Server 在播放命令中下发绝对时间 `executeAtUtc`。LedPlayer 原实现直接使用本机的
`DateTimeOffset.UtcNow` 等待该绝对时间：

- LED 时钟落后 Server 两分钟时，播放协程会无提示等待约两分钟。
- 视频已经渲染出第一帧，但播放协程仍未通过时间等待，因此画面表现为卡在第一帧。
- 欢迎音频已经缓存并解码，但同样停留在计划时间等待，因此 TouchClient 一直显示校验/准备状态。
- 人工 Resume 不走首次计划时间等待，所以会让视频立即继续，造成“暂停再继续才能播放”的表象。

这两个现象不是素材下载失败、AVPro 硬件解码失败或 UDP 7002 导致，而是同一套绝对时间等待逻辑在三台电脑未同步时失效。

## 3. 时钟偏差保护修复

修改文件：

- `src/LedPlayer/Assets/Scripts/LedPlaybackController.cs`
- `src/LedPlayer/Assets/Scripts/LedWelcomePlaybackController.cs`

修复策略：

1. 计算 Server 计划时间与 LED 当前时间的偏差。
2. 只信任绝对值不超过 0.5 秒的计划偏差。
3. 可信的小偏差转换为 Unity 单调时钟 `Time.realtimeSinceStartup` 等待，避免系统时间在等待期间变化。
4. 偏差超过 0.5 秒时记录明确警告，忽略绝对计划时间并立即播放。
5. 视频只有在计划时间可信时才按迟到量补偿 Seek；时钟明显不可信时不再错误跳过视频内容。

预期现场日志：

```text
[LED Player] 检测到Server与LED计划时间偏差 ... 秒，超过可信窗口 ... 秒；忽略绝对时间并立即从请求位置播放。
Welcome detected Server/LED clock offset ... outside trusted window ..., starting immediately.
```

此保护用于避免时钟异常导致业务卡死，不能替代三台设备的正常校时。正式部署仍应让 Server、TouchClient 和 LedPlayer 使用同一局域网时间源，以保证计划起播精度和声画同步。

## 4. UDP 旧中控暂停键修复

修改文件：

- `src/LedPlayer/Assets/Scripts/LegacyUdpPlaybackBridge.cs`

### 4.1 根因

旧中控只有一个播放/暂停切换按钮，每次点击暂停区域都会发送同一条 UDP 消息：

```text
VideoPause
```

兼容桥原来把每一条 `VideoPause` 都固定转换成新协议的 `PlaybackAction.Pause`。因此第一次点击可以暂停，第二次点击仍然是暂停，不可能恢复播放。

### 4.2 修复方式

- 在旧协议兼容桥中保存旧播放会话的暂停状态。
- 第一次收到 `VideoPause` 时转换为 `Pause`。
- 第二次收到 `VideoPause` 时转换为 `Resume`，后续继续交替。
- 收到旧视频 Play 或 CloseVideo 时重置切换状态。
- 新中控通过 Server 下发的独立 Pause/Resume 指令保持原样，不受此兼容逻辑影响。

预期现场日志按点击顺序交替出现：

```text
[LED Legacy UDP] VideoPause切换为暂停。
[LED Legacy UDP] VideoPause切换为继续。
```

## 5. 验证状态

| 项目 | 状态 | 说明 |
| --- | --- | --- |
| 手工校时后的正式视频首播 | PASS | 现场确认无需再依赖暂停/继续启动视频 |
| 手工校时后的欢迎词播放 | PASS | 现场确认两个问题随时钟校准一并恢复 |
| LedPlayer Unity脚本编译 | PASS | Unity 2020.3.35f1c2，无脚本编译错误 |
| LedPlayer Windows Player构建 | PASS | 本地正式Windows构建成功 |
| 安装包文件清单与SHA-256 | PASS | 156个文件全部通过清单校验 |
| 应用内时钟偏差保护现场验证 | NOT RUN | 已进入新安装包，仍需在不同步时钟条件下验证立即播放 |
| 旧中控暂停→再次点击继续 | NOT RUN | 代码与构建完成，仍需现场UDP 7002实机回归 |

## 6. 交付与部署

本次只需要更新 LedPlayer，不需要重新部署 Server 或 TouchClient。

安装器：

`artifacts/LedPlayerHotfix-20261013-ClockUdp/Installer/TG智慧展厅播放端_Setup.exe`

安装器 SHA-256：

```text
0dafd833554f2f0bfb32abe4f07e24fe6eec07bcc1393a1e6f791d9798b227d0
```

安装后 `Assembly-CSharp.dll` 预期 SHA-256：

```text
5ee3e63c21f3362c3e5b02f2264cc306c3ed0cf4fdede478eaf4601e4aea8a85
```

覆盖安装前应彻底退出 LedPlayer 和自动启动器。安装器目录需要保留现场
`led.site-install.json`，并放入 Server 导出的 `server-public.cer`。覆盖安装应保留现有配置与素材缓存。

## 7. 现场回归清单

1. 故意让 Server 与 LED 相差一分钟以上，启动欢迎词，确认不会长期停在校验状态，并出现时钟偏差警告。
2. 在相同时间差下开始正式视频，确认三秒内持续播放，不人工点击暂停/继续。
3. 恢复三台电脑时间同步，再验证欢迎词、视频首播和基本声画同步。
4. 使用旧中控播放视频，第一次点击暂停，第二次点击同一按钮恢复播放。
5. 验证旧中控停止、重新播放和音量控制仍正常。
6. 验证新中控暂停、继续、跳过和终止未受 UDP 兼容修改影响。

