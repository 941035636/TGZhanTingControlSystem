# LED D3D 格式错误专项回归（2026-09-04）

## 结论与范围

**PASS — 已消除当前运行路径的 `Unsupported D3D format 0x58`，修复版实际 Windows Player 视频输出与控制回归通过。**

- 修复前基线：`dcc40ba518372b1a4034ddebdec026145918d04a`，分支 `codex/product-upgrade`。
- 修复 commit：本报告所在的提交。
- 只修正 LED 播放接入及首帧诊断；未修改 Touch UI、Server、Contracts、缓存、TTS 或播放计划时间。
- **NOT RUN — 用户原始黑屏视频及现场大屏/GPU复测。** 本机复现了同样的 D3D 错误；旧版在窗口可见时仍能显示本轮工程素材，不能声称在本机稳定复现了现场持续黑屏。

## 根因与最小修复

`dcc40ba` 将 `LedRuntimeBootstrap` 从已接入的 LibVLC/UMP 改回 AVPro 1.8.9 / Media Foundation，依靠关闭硬件解码规避问题。但项目既有 `docs/03_AVPro接入说明.md` 已记录：该版本原生纹理接口的 D3D 格式问题不能单靠关闭硬件解码解决。

本轮基线实际日志显示 `MF-MediaEngine-Software` 仍产生 **8 条** `Unsupported D3D format 0x58`。同一日志同时声称 `Video output verified`，说明“有纹理对象和帧计数”不能单独证明 D3D 纹理兼容或最终显示正确。

修改内容：

1. `LedRuntimeBootstrap` 恢复工程已自带的 `UniversalMediaPlayer` / `UniversalMediaPlaybackAdapter` / `RawImage` 接入。保留当前 `playbackAdapterComponent` 字段绑定、现场配置、讲解音频和其他最近修复；未回退整个文件或提交。
2. LibVLC 使用现有 `MediaPlayerHelper.GenPluginTexture` 创建 Unity 自有的 `BGRA32` 纹理，避开旧 AVPro 外部 D3D 纹理导入。
3. UMP 适配器实现现有 `IVideoPlaybackDiagnostics`，监听 `ImageReady`，保留首帧超时检测；新 Prepare、Stop、停用和解码错误都会清除上一媒体的首帧状态。
4. 新增 `scripts/Test-LedVideoPlayback.ps1`：控制实际 Windows Player，采集视频区域像素，并检查 D3D 错误。基线运行 FAIL，修复版运行 PASS。不是仅测 API 返回值。

未修改 AVPro/UMP 第三方插件源码或更换其二进制依赖。

## 环境和素材

| 项目 | 本轮实际值 |
|---|---|
| 系统/Unity | Windows 10；Unity 2020.3.35f1c2；StandaloneWindows64 |
| 实际渲染 GPU | AMD Radeon 530，D3D11 level 11.1，驱动 26.20.12026.2001 |
| Server | 当前 Release Server，独立 `http://localhost:5082`；测试 TTS worker 关闭 |
| 数据/缓存 | `artifacts/LedD3DRegression/Data`、`Cache`；未修改 5080 正式运行数据 |
| 路线 | 既有 Phase 8 工程测试路线 A → B → A，3 个节点 |
| 视频 A | MP4 / H.264 / AAC，1920×1080，30 fps，12 秒，13,469,221 字节 |
| 视频 A SHA-256 | `ecfb15be7331e033a72d7a92ba0f58427d8ca7c3ac9933e71841300557bea518` |
| 视频 B | MP4 / H.264 / AAC，1920×1080，30 fps，12 秒，284,178 字节 |
| 视频 B SHA-256 | `4bc6bdd39689a65c81c1f48ab43c0807fc81071fdda8da8abe5659dcd9436f41` |
| 截图 | 实际 Windows Player 可见窗口客户区，1366×768；视频源分辨率仍为 1080P |
| 修复版主程序集 SHA-256 | `d539043180faef4981843a09a9803d2a42c8e6a5c763fb91a6a0fa4b50b0dd8d` |

本机代理下 Unity 使用 `127.0.0.1:5082` 曾收到 502，隔离测试配置改为 `localhost:5082` 后正常；未改变系统代理或正式现场配置。隐藏窗口采集的纯黑图不作为播放黑屏证据，只采用窗口显示后的截图。

## 结果

| 检查 | 状态 | 实际结果/证据 |
|---|---|---|
| 修复前 D3D 错误复现 | PASS | 基线软件解码仍报 8 条 `0x58`；[完整运行日志](Evidence/LedD3D/baseline-player.txt)。 |
| 基线运行同一回归脚本 | FAIL（预期负例） | 视频在本机可见，但 D3D 错误检查拒绝通过；[结果](Evidence/LedD3D/baseline-results.json)、[可见窗口截图](Evidence/LedD3D/baseline-playing.png)。 |
| 修复版 Windows 构建 | PASS | Unity 正常退出 0；既有构建入口同时复制 libvlc/libvlccore 到实际运行查找目录。 |
| LED 就绪、本地素材播放 | PASS | 同步 V5，实际显示视频；[画面](Evidence/LedD3D/01-playing.png)、[日志](Evidence/LedD3D/fixed-player.txt)。 |
| Pause | PASS | 间隔 1 秒采集的视频区域像素相同；[暂停](Evidence/LedD3D/02-paused.png)、[保持暂停](Evidence/LedD3D/03-still-paused.png)。两张 PNG 的 SHA-256 均为 `fa1ff0157ba933d13e87ac122077ec110a1101cf4dbeb48f27dfbf2beb416f9a`。 |
| Resume | PASS | 视频区域像素恢复变化；[继续](Evidence/LedD3D/04-resumed.png)。 |
| Skip | PASS | Session 前进到下一个节点，实际显示下一视频；[切换后](Evidence/LedD3D/05-next-node.png)。 |
| Retry | PASS | 重新准备并播放当前视频；[重播后](Evidence/LedD3D/06-retry.png)。 |
| 多节点完成/再次运行 | PASS | 修复版两轮控制回归均完成；第二轮为关闭后重新启动同一个修复构建，重新执行完整脚本。 |
| D3D 错误及首帧 | PASS | 修复版两轮均无 `Unsupported D3D`，日志确认 BGRA32 首帧和输出；第二轮 [10 项检查全部通过](Evidence/LedD3D/fixed-results.json)。 |
| 现场原视频黑屏 | NOT RUN | 未取得用户实际黑屏视频及现场复测结果，不以工程测试素材替代。 |
| >2 GB、4K、8 小时长稳、可听音频/实际声画同步 | NOT RUN | 不属于本次已完成的专项短时回归，不据此扩大原 Phase 8 验收结论。 |

本轮操作由隔离 Server API 驱动，但视频解码、渲染、暂停、继续、切换均在实际 Player 内执行。未宣称完成 Touch UI 点击回归或现场音频测量。

## 复跑方法

1. 用 Unity `TG.Control.Editor.WindowsPlayerBuilder.Build` 构建 Windows LED，设置 `TG_WINDOWS_BUILD_OUTPUT` 到新输出目录；不要使用 `-nographics` 验证视频。
2. 启动独立端口和数据目录的 Server，准备至少两个明亮、有运动、长度不小于 12 秒的测试视频节点；可以复用上述工程素材，但不要连接正式运行数据。当前仓库不提交视频文件。
3. 通过 `TG_LED_PLAYER_CONFIG` 指定独立 LED 配置和缓存。启动该构建，日志写到新的文件，取得其进程 ID。
4. 确认测试 Server 没有活动 Session，执行：

```powershell
.\scripts\Test-LedVideoPlayback.ps1 `
  -ServerUrl http://localhost:5082 `
  -PlayerId <实际LED进程ID> `
  -PlayerLogPath <本次Player日志绝对路径> `
  -ModuleId module-01 `
  -OutputDirectory <证据输出目录> `
  -IsolatedTestEnvironment
```

脚本会显示指定的 Unity 窗口，只截取该窗口的客户区，失败时也保存结果并停止自己创建的测试 Session。黑场艺术素材不适用这套亮度断言；使用现场素材时应另做人工可见画面核对。

## 本地交付

- 修复构建：`artifacts/LedD3DRegression/Fixed/LedPlayer.exe`。
- Unity 构建日志：`artifacts/led-d3d-baseline-build.log`、`artifacts/led-d3d-fixed-build.log`。
- 全量本地运行证据：`artifacts/LedD3DRegression`；关键运行日志、截图和结果已随本报告提交。提交的日志副本仅规范化换行和行尾空白，未改动日志内容；原始日志仍保留在本地证据目录。
- 部署时使用整个 `Fixed` 输出目录（含 `_Data`、UnityPlayer、插件和 VLC 模块），不能只替换 EXE；保留现场 `led-player.json` 配置，勿复制隔离测试的 5082 配置。
- 本轮不自动替换现场安装目录，不修改正在运行的 5080 Server/MeloTTS 服务。
