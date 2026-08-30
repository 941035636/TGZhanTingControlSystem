# Phase 8 产品交付级稳定性与故障恢复验收

- 基线：`af5ed3dc8a66a446cb8d8f4c42270790ad5e52e0`
- 执行时间：2026-08-30 至 2026-08-31（Asia/Shanghai）
- 执行原则：测试优先、证据优先、最小修复；未进行 UI 重设计、TTS 或新产品功能开发。
- 总结：本轮发现并修复 7 类交付阻断问题；Windows 三端基本流程和故障恢复已通过。由于缺少真实大文件、现场素材和测量设备，Phase 8 **尚不能整体判定为 PASS**。
- 状态口径：仅使用 `PASS / FAIL / BLOCKED / NOT RUN`。短测不替代长稳测试，小文件不替代大文件测试，协议计划时间不替代实际声画测量。
- 修复 commit：本文与全部 Phase 8 修复位于同一个 Phase 8 commit。

## 1. 测试环境与素材

| 项目 | 实际环境 |
|---|---|
| 操作系统 | Windows 10 Pro 10.0.19045，约 19.9 GB RAM |
| Unity | `2020.3.35f1c2`，`C:\Program Files\Unity\Hub\Editor\2020.3.35f1c2\Editor\Unity.exe` |
| .NET | SDK 9.0.317；Server 目标 `net8.0` |
| Node.js | 24.19.0；npm 11.17.0 |
| GPU | AMD Radeon 530 2 GB；Intel HD Graphics 620 1 GB；实际 Player 日志为 D3D11 / AMD Radeon 530 |
| 三端 | Release Server DLL、实际 TouchClient Windows Player、实际 LedPlayer Windows Player |
| 播放后端 | 当前正式运行路径为 UMP / LibVLC；AVPro 1.8.9 仍在工程中但未用于本轮正式播放路径 |
| 运行数据 | `Builds/Phase8/RuntimeData`（隔离于工程默认数据） |
| 工程测试视频 A | MP4，H.264/AAC，1920×1080，30 fps，12 秒，13,469,221 字节，SHA-256 `ecfb15be7331e033a72d7a92ba0f58427d8ca7c3ac9933e71841300557bea518` |
| 工程测试视频 B | MP4，H.264/AAC，1920×1080，30 fps，12 秒，284,178 字节，SHA-256 `4bc6bdd39689a65c81c1f48ab43c0807fc81071fdda8da8abe5659dcd9436f41` |
| 大文件素材 | 未找到真实超过 2 GB 的视频 |
| 声画同步素材/设备 | 无可用于现场可观察偏差测量的参考素材、拍摄或测量设备 |

工程测试视频只用于验证三端基本流程、缓存完整性和恢复逻辑，不作为大文件、真实展厅素材、8 小时长稳或声画同步验收替代品。

## 2. 构建与实际运行

| 检查项 | 状态 | 操作步骤与实际结果 | 证据 | 问题/修复 |
|---|---|---|---|---|
| Admin Web | PASS | `npm ci` 后执行生产构建；Vite 构建成功，npm 未报告漏洞。 | 本地构建输出 | 无代码修复 |
| Server Release | PASS | `dotnet build -c Release --no-restore`；最终结果 0 warning / 0 error。 | 本地控制台输出 | 无 |
| TouchClient Windows 构建 | PASS | 使用指定 Unity 批处理生成 StandaloneWindows64，并实际启动。 | `Builds/Phase8/Logs/TouchClient-final-build.log` | 初次复现 `EditorBuildSettings` 无场景导致构建失败；增加最小 bootstrap scene 与 Windows 构建入口。 |
| LedPlayer Windows 构建 | PASS | 使用指定 Unity 生成 StandaloneWindows64，并实际启动。 | `Builds/Phase8/Logs/LedPlayer-recovery-fix-build.log` | 初次构建缺场景；首次运行还复现 LibVLC 查找失败。构建结束后将已有 `x86_64` LibVLC DLL 复制到运行时实际查找目录。 |
| Server + Touch + LED 同机实际运行 | PASS | 三个实际 Windows 进程同时运行；Touch 显示 Server 在线/系统可接待，LED 注册、同步并播放。 | [三端就绪截图](../../Builds/Phase8/Evidence/01-touch-led-ready-window.png)、[实际播放截图](../../Builds/Phase8/Evidence/09-led-actual-h264-playback.png) | 未用纯 API 结果代替 Player。 |
| Windows 服务安装/开机启动 | NOT RUN | 未改动本机服务配置。 | 无 | 本轮不执行有系统级持久影响的部署动作。 |

## 3. 第一组：三端基础稳定性

**状态：PASS**

- 环境/素材：上述实际三端 Windows 构建；V5 内容；3 节点路线（A → B → C）。
- 步骤：实际 Touch UI 选择路线并开始；进入控制页；点击暂停、继续、跳过；由实际 LED Player 播放多节点，等待完成。
- 实际结果：Session `07a7b7a1b9e34803999a94798712ce56` 中，暂停后 `paused=true`，继续后 `paused=false`，跳过后从节点 A 前进到节点 B；其他完整流程 Session 自动播放三节点并完成。
- 证据：[Touch 当前讲解页](../../Builds/Phase8/Evidence/06-touch-active-control-page.png)、[LED 实际 H.264 画面](../../Builds/Phase8/Evidence/09-led-actual-h264-playback.png)、`operational-events.jsonl` 中 Start / Pause / Resume / Skip / Completed。
- 发现问题：计划起播前快速暂停会被稍后的计划起播覆盖，Session 表示暂停但 LED 继续播放。
- 最小修复：保留准备阶段收到的暂停状态，起播后立即保持暂停并报告 Paused；回归中暂停跨过原视频总时长仍保持在同一节点。

## 4. 第二、三组：真实大文件与断点续传

**状态：BLOCKED**

`BLOCKED — 缺少真实>2GB测试素材`

- 环境/素材：扫描项目及当前用户常用目录，未发现可用于验收的真实超过 2 GB 视频。
- 未声称通过的项目：>2 GB 上传、SHA-256、发布、LED 下载、本地缓存、完整下载后播放、Seek、重播、切换节点、重启复用；以及网络断开、LedPlayer 退出、Server 不可访问三种大文件断点续传。
- 小文件补充证据（不替代大文件验收）：对 13,469,221 字节缓存文件翻转首字节，SHA 从正确值变为 `63f53a...71c1`；LED 检出不一致、删除并重新下载，最终恢复为预期 SHA。应用重启后两个缓存文件时间戳不变且直接 Ready。
- 证据：`Builds/Phase8/Logs/LedPlayer-cache-reuse.log`，其中包含“缓存文件SHA-256不一致”及后续 `2/2` 同步完成。
- 发现问题：修复前 Server 发布不校验 SHA，LED 下载/复用缓存也不校验 SHA；错误 SHA 的 V1 曾被错误标为 Ready。
- 最小修复：Server 发布时流式 SHA-256 校验；LED 对已存在文件、续传结果和下载结果执行大小与 SHA 完整性校验；哈希计算放到后台任务避免阻塞 Unity 主线程。

## 5. 第四组：故障恢复矩阵

| 场景 | 状态 | 操作步骤与实际结果 | 证据 | 问题/修复 |
|---|---|---|---|---|
| TouchClient 重启 | PASS | 在 Session `0383048d452e4f84b22e07978bf39357` 节点 A 暂停时关闭并重启 Touch；相同 Session ID、节点号和暂停状态仍在，Touch 显示“继续当前讲解”。 | [恢复截图](../../Builds/Phase8/Evidence/11-touch-restart-session-recovered.png) | 无额外修复 |
| Server 重启 | PASS | 同一暂停 Session 下停止并重新启动 Server，复用同一数据目录；Session ID、节点 A、`paused=true` 均恢复。 | JSONL `Recovered` 事件；运行查询结果 | 现有持久化机制通过；客户端重新注册后会重新准备当前节点。 |
| LedPlayer 重启 | PASS | 暂停活动 Session 时关闭 LED；修复前重启后虽在线但 Resume 仍停在待机画面。修复后重启，重新准备节点 A、恢复 Paused，Resume 后出现实际视频并继续完成。 | [LED 恢复后实际画面](../../Builds/Phase8/Evidence/12-led-restart-current-node-resumed.png)、JSONL `ClientRecovered` | 增加每进程实例 ID；仅在新实例注册时重发当前节点，保留暂停状态，不改变正常播放语义。 |
| Server 暂时不可访问后自动重连 | PASS | 停止 Server 后重新启动；Touch 和 LED 进程不重启即可重新在线，LED V5 Ready。 | readiness 查询及客户端运行日志 | 基本状态无需人工重启整个系统。 |
| 单独隔离 Touch ↔ Server 链路 | NOT RUN | 三端均在同一主机/loopback，本轮未配置进程级防火墙隔离。 | 无 | 不能用 Server 整体停止冒充独立链路测试。 |
| 单独隔离 LED ↔ Server 链路 | NOT RUN | 同上。 | 无 | 同上。 |

## 6. 第五组：播放稳定性

| 项目 | 状态 | 实际结果/证据 |
|---|---|---|
| MP4 / H.264 / 1080P | PASS | 实际 LedPlayer 显示 1920×1080 H.264 画面；日志 9 次 `LibVLC media prepared: 1920x1080`，无运行异常。 |
| 多视频连续切换 | PASS | A → B → C 多节点连续播放并自动完成；三个短循环均完成。 |
| Pause / Resume | PASS | UI 与 API 状态一致；重复 5 次 Pause 和 5 次 Resume 均保持幂等状态。 |
| Skip | PASS | 实际 UI 跳过后准确前进一个节点；重复请求未产生多个活动 Session。 |
| Retry | PASS | Retry 后立即 Skip，Session 从节点 A 前进到节点 B，未损坏 Session。 |
| 重复播放 | PASS | 三轮连续实际 Player 循环均完成，分别为 42.7 s、42.6 s、42.9 s。 |
| Seek | FAIL | 实际活动 Session 调用现有 Seek 操作，HTTP 404，`accepted=false`，消息“不支持该控制操作”。未在 Phase 8 偷偷新增产品功能。 |
| 4K | NOT RUN | 现场目标是否包含 4K 尚未确认，且无本轮正式 4K 素材。 |
| 真实展厅视频连续播放 | BLOCKED | 当前仅有工程生成测试素材，无正式展厅视频。 |
| AVPro 硬件解码 | NOT RUN | 当前正式播放路径为 LibVLC/UMP，并未调用 AVPro；D3D11/Media Foundation 初始化日志不能证明 LibVLC 硬件解码。 |
| 黑屏/花屏 | PASS | 本轮可观察工程素材播放未出现黑屏或花屏；证据见实际播放截图。范围仅限短时工程测试。 |
| 无声/音频重复 | NOT RUN | 素材含 AAC，但本轮没有录音或声学测量证据，不能仅凭容器轨道和日志判定。 |
| 短时内存趋势 | PASS | LED Working Set 三轮结束为约 224.4 / 226.3 / 225.1 MB，后两轮未持续上升；Touch 约 128.4 / 128.5 / 120.7 MB。该结果不代表 8 小时长稳。 |

## 7. 第六组：长时间运行

- 短时自动循环：**PASS**。三个实际 Player 三节点循环均完成，未出现 Server/Touch/LED 异常、播放失败或人工干预；最终运行日志关键错误计数为 0。
- **8小时Soak Test尚未完成**：`NOT RUN`。
- 原因：本轮没有等待 8 小时；明确不把约 129 秒的三轮短测等同于长稳验收。

## 8. 第七组：声画同步

**状态：BLOCKED**

- 现有协议证据：Server 使用统一 `executeAtUtc`；客户端日志记录计划起播及状态报告；单一 LED 客户端的协议日志显示 `start drift 0ms`。
- 限制：当前正式架构由同一 LED 端承载视频与讲解音频，Server 的单客户端时间戳差不能证明实际声画偏差；本轮无明显同步参考素材、录音/高速摄影或其他测量设备。
- 结论：不能据 planned time 相同或客户端日志声称 `<1秒`，也不能声称达到约 500 ms 内部目标。
- 阻塞原因：缺少实际可观察起播偏差测量条件。

## 9. 第八组：内容发布安全

| 项目 | 状态 | 实际结果 |
|---|---|---|
| 正常发布 | PASS | 正常内容从 V2 发布为 V3。 |
| 素材缺失 | PASS | `/media/phase8-missing.mp4` 返回 400；正式版本保持 V2。 |
| SHA 错误 | PASS | 64 位全零 SHA 返回 400，并报告预期/实际哈希；正式版本保持 V2。 |
| 素材尺寸错误 | PASS | 记录 1 字节、实际 13,469,221 字节时返回 400；正式版本保持 V2。 |
| 非法外部内容引用 | PASS | `https://example.com/illegal.mp4` 返回 400；正式版本保持 V2。 |
| 失败后旧正式版本可用 | PASS | 四类失败发布后 `/api/content/current` 均仍为 V2，LED 保持可用。 |
| Rollback | PASS | 从 V4 回滚 V2，按现有语义生成新正式版本 V5，`publishedBy=admin（回滚自 V2）`。 |
| Rollback 后 LED 同步 | PASS | 活动讲解结束后 LED 自动同步到 V5，`ledReady=true`。 |
| 活动讲解时内容变化 | PASS | Session `5abcf47074134011a8690c7e1b34248a` 启动于 V3；发布 V4、回滚生成 V5 后仍固定使用 V3 且保持暂停，未偷偷切换会话内容。 |

## 10. 第九组：异常操作

| 项目 | 状态 | 实际结果/证据 |
|---|---|---|
| 连续快速开始 | PASS | 修复前 6 个并发请求出现 4×200、2×409，并生成 4 个 Session；增加启动互斥后为 1×200、5×409，活动 Session 数为 1。 |
| 重复 Pause | PASS | 5 个并发请求均返回 200，最终 `paused=true`，未重复改变状态。 |
| 重复 Resume | PASS | 5 个并发请求均返回 200，最终 `paused=false`。 |
| 快速 Skip | PASS | 请求被接受，Session 只完成/前进一次；未形成并行 Session。JSONL 保留每个操作请求。 |
| Retry 后立即 Skip | PASS | Session `101f06765e674dddb5ae5e7e4e63ff52` 从节点 A 正确前进至节点 B。 |
| Stop 确认过程中 Session 刷新 | NOT RUN | 未做 UI 确认框与轮询刷新精确时序注入。 |
| Server 断开时操作 | NOT RUN | 验证了自动重连，但未在断线窗口执行一组 UI 控制动作。 |
| LED 离线时启动 | PASS | LED 离线超过在线阈值后 readiness 为 `canStart=false`；启动返回 409“LED播放端离线，暂时不能开始讲解”。 |
| Active Session 下再次启动 | PASS | 并发回归中其余 5 个请求稳定返回 409。 |

## 11. 第十组：日志与可诊断性

**状态：PASS（最小整改后）**

- 复现：原 JSONL 具备发生时间、Session、操作和部分错误原因，但故障事件没有结构化 `clientId` / `nodeId`，必须交叉查运行日志，构成真实诊断盲区。
- 最小修复：仅为 `OperationalEvent` 增加可选 `clientId` 和 `nodeId`，在播放失败与客户端恢复事件写入；不建设新日志平台。
- 回归：向实际活动 Session 注入明确测试故障后，JSONL 事件 `NodeSkippedAfterFailure` 同时包含：
  - 时间：`2026-08-31T05:40:52.169846+08:00`
  - Session：`046e98fb72584ff48dd13b1885bb2551`
  - 节点：`phase8-node-a`
  - 客户端：`led-main`
  - 操作：`NodeSkippedAfterFailure`
  - 原因：`Phase8 diagnostic fault injection`
- 证据：`Builds/Phase8/RuntimeData/operational-events.jsonl`。

## 12. 本轮最小修复清单

1. 增加两端可复现的 Windows 构建入口与 bootstrap scene；LedPlayer 构建后补齐 LibVLC 实际运行目录。
2. Server 发布与 LED 缓存链路增加真实 SHA-256 完整性校验和损坏自愈。
3. 修复计划起播前 Pause 被覆盖的竞态。
4. 修复 LED 已在线但状态条仍显示“连接中”。
5. 增加客户端进程实例识别，LED 重启/Server 重启后只恢复当前节点并保留暂停状态。
6. 为开始讲解增加最小互斥，防止快速重复点击生成多个 Session。
7. 为故障 JSONL 增加可选客户端和节点字段，补齐真实诊断盲区。

未进行架构重构、UI 改版、正式 TTS 或 Seek 功能开发。

## 13. 最终四类清单

### A. 已通过

- Server、TouchClient、LedPlayer Windows 构建与实际三端运行。
- 选择路线、开始、多节点、暂停、继续、跳过、完成。
- 工程素材 MP4/H.264 1080P、连续切换、Retry、重复播放。
- Touch、Server、LED 重启恢复；Server 暂时不可访问后的自动重连。
- 正常发布、缺失/非法/尺寸/SHA 拒绝、旧版本保留、rollback、LED 版本同步、活动 Session 版本固定。
- LED 缓存 SHA 损坏检出、自愈、应用重启复用。
- 快速开始互斥、重复 Pause/Resume、快速 Skip、Retry 后 Skip、LED 离线启动、Active Session 再启动。
- JSONL 故障诊断字段；三轮短时自动循环与短时内存趋势。

### B. 失败

- Seek：当前控制接口明确返回“不支持该控制操作”（HTTP 404）。Phase 8 未把新增 Seek 功能伪装成稳定性修复。

### C. 因硬件/素材条件阻塞

- `BLOCKED — 缺少真实>2GB测试素材`：完整大文件组与三类大文件断点续传均未验收。
- 缺少正式展厅视频，真实现场视频连续播放验收阻塞。
- 缺少同步参考素材和实际测量设备，`<1秒` / 约 500 ms 声画同步验收阻塞。

### D. 尚未执行

- 8 小时 Soak Test。
- 4K（现场目标尚未确认）。
- 单独 Touch ↔ Server、LED ↔ Server 网络隔离。
- Stop 确认框刷新竞态、断线窗口 UI 操作。
- AVPro 正式播放与硬件解码确认；可听音频的无声/重复音频测量。
- Windows 服务安装/开机启动验收。

## 14. 证据索引

- `Builds/Phase8/Evidence/01-touch-led-ready-window.png`
- `Builds/Phase8/Evidence/06-touch-active-control-page.png`
- `Builds/Phase8/Evidence/09-led-actual-h264-playback.png`
- `Builds/Phase8/Evidence/10-led-online-overlay-regression.png`
- `Builds/Phase8/Evidence/11-touch-restart-session-recovered.png`
- `Builds/Phase8/Evidence/12-led-restart-current-node-resumed.png`
- `Builds/Phase8/Logs/TouchClient-final-build.log`
- `Builds/Phase8/Logs/LedPlayer-recovery-fix-build.log`
- `Builds/Phase8/Logs/LedPlayer-cache-reuse.log`
- `Builds/Phase8/RuntimeData/operational-events.jsonl`

报告到此停止，不进入 TTS 或其他 Phase。
