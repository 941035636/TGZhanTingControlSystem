# LED 播放器接入说明

## 当前正式基线

- Unity：2020.3.35f1c2，Windows x64，1920×1080；
- 播放器：已随工程合入的已授权 AVPro Video 3.2.0f1 Ultra；
- 运行适配器：`AvProMediaPlaybackAdapter`；
- Windows 后端：Media Foundation MediaEngine，当前验证为硬件解码；
- 正式视频先下载到 LED 主机 NTFS 缓存，通过完整性校验后再以本地路径播放；
- Windows Player 必须完整携带 `AVProVideo.dll`、`AVProVideoWinRT.dll` 和 `Audio360.dll`。

`LedRuntimeBootstrap` 在运行时创建 AVPro `MediaPlayer`、适配器和 `DisplayUGUI`。播放面板在首个可渲染帧之前保持黑色，避免把未初始化纹理或待机层误显示为视频。缓存、统一计划起播时间、讲解音频和 Server 协议均不依赖具体视频后端。

## 已验证行为

2026-09-27 使用实际 Windows Player、两个 1080P H.264/MP4 节点完成：

- 内容同步和 LED Ready；
- 实际可见视频帧输出；
- Pause 画面冻结、Resume 恢复变化；
- Skip、Retry、多节点完成；
- AVPro 原生版本 `3.2.0f1-ultra`；
- 后端日志 `MF-MediaEngine-Hardware`；
- 未出现 `Unsupported D3D format`、黑屏或播放异常。

回归入口为 `scripts/Test-LedVideoPlayback.ps1`。脚本按运行日志识别当前活动后端，不再把 LibVLC 文本当作通过条件。现场 GPU、客户原片、>2GB、4K、8 小时和实测声画同步仍必须按 Phase 8 验收表单独执行，不能由本次短时回归替代。

## 历史迁移说明

旧 AVPro 1.8.9 在 Unity 2020.3/DX11 组合上曾出现 `Unsupported D3D format 0x58`，工程一度切换到 LibVLC/UniversalMediaPlayer。该记录保留在 `docs/QA/LED-D3D-Playback-Regression.md`，仅用于解释历史故障，不代表当前运行架构。当前工程已经删除旧 UMP/LibVLC 运行路径，禁止按历史文档重新复制 `libvlc.dll` 或恢复旧适配器。

## 正式素材建议

- MP4 容器；
- H.264 视频和 AAC 音频；
- 1920×1080，固定 25 或 30 fps；
- 控制码率并保留合理关键帧间隔，避免超长 GOP 导致 Seek 和切换缓慢；
- 发布前记录文件大小和 SHA-256；
- AVPro 为商业专有依赖，生成交付包前必须确认许可证覆盖交付设备和再分发范围。
