# Phase UI-9 — 智慧展厅科技迎宾系统与自动待机

## 目标

TouchClient 启动时先显示明亮、低负载的科技待机页；有效触摸请求服务端欢迎流程。欢迎音频**只**经由 `Server → LedPlayer → LED 音响` 输出，触控端只显示视觉、文案和服务端权威进度字幕。LED 上报实际播放完成后才进入 12 模块首页。

## 状态机与优先级

`Standby → Preparing → Playing → Completed → Home`，失败进入 `Failed` 并提供“进入讲解首页”安全兜底。欢迎流程完全独立于 `PlaybackSession` 和 `PlaybackCommand`：不创建、不修改、不停止正式讲解 Session。

正式讲解优先：存在正式 Session 时服务端拒绝欢迎请求；欢迎准备/播放中开始正式讲解也被 API 拒绝。Server 重启后欢迎协调器重新回到内存 `Standby`，不会留下僵尸欢迎任务。

## 协调与完整性

- `WelcomePlaybackCommand` / `WelcomePlaybackStatusReport` 是最小独立协议；正式 `PlaybackCommand` 与 `PlaybackCoordinator` 未重构。
- `welcome.audio` 复用现有 UI 素材资产字段：`AssetId + URL + SHA-256 + Size + MediaType`。缺少任一完整性字段即拒绝播放。
- LedPlayer 通过既有 `LedContentCache` 下载并以真实 SHA-256、大小验证；校验、解码、超时或播放失败均回报 `Failed`。
- 服务端仅在 LED 上报 `Preparing`（素材已可播）后发送计划 `Play`；LedPlayer 仅在 `AudioSource` 实际停止后回报 `Completed`。
- 触控字幕使用 `WelcomeCaptionCue(StartSeconds, EndSeconds, Text)` 与服务端位置进度匹配，不以字数或假计时模拟。
- 欢迎预备/播放两分钟未完成，Server 标为 `Failed` 并向 LED 发 `Stop`，避免无限等待。

## 配置

保留兼容的 `UiExperienceConfig`，新增可空 `Welcome`：

- `AudioEnabled`；
- `Captions` 分句时间轴。

既有 `touchElements` 中的 `welcome.eyebrow`、`welcome.title`、`welcome.subtitle`、`welcome.background`、`welcome.logo`、`welcome.audio` 继续生效。AdminWeb 的终端界面配置可编辑标题、说明、背景、已校验欢迎音频、音频启用开关、字幕时间轴和 180 秒空闲返回时间。旧 JSON 没有 `Welcome` 时按 `AudioEnabled=true`、空字幕兼容读取。

## 待机与交互

待机页采用浅蓝白空间、缓慢流动光带和品牌中心区；不使用旧版深蓝同心圆。整个可视页面是唤醒触控目标，有重复触摸防抖，等待中不再发起重复请求。

仅在以下条件自动回到待机：连接正常、无正式 Session、无欢迎准备/播放、当前不在路线编辑页且路线草稿未修改，且连续 180 秒无输入。断网、正式讲解、暂停、准备/恢复和未保存路线均不会触发自动待机，也不会发送 Stop 或改变内容/路线。

## 异常策略

| 场景 | 处理 |
| --- | --- |
| LED 离线 | Server 拒绝请求，Touch 明确提示并留在待机页 |
| 音频未配置/不完整 | Server 拒绝，不伪造播放成功 |
| SHA/大小/解码失败 | LedPlayer 回报失败；Touch 提供重试或进入首页 |
| Server/Touch 重启 | 欢迎没有持久化 Session；重新读取权威状态，避免僵尸播放 |
| 重复触摸 | Touch 防抖 + Server 单协调器返回同一活动请求 |
| 正式讲解竞争 | 正式讲解优先，双方 API 拒绝重叠 |

## 验证记录

| 项目 | 结果 | 说明 |
| --- | --- | --- |
| Server Release Build | PASS | `dotnet build ...TG.Control.Server.csproj -c Release --no-restore`，0 error/0 warning |
| AdminWeb production build | PASS | `npm run build` |
| TouchClient C# build | PASS | `dotnet build src/TouchClient/TG.Control.Touch.csproj --no-restore`，0 error |
| LedPlayer C# project build | BLOCKED（既有） | 项目文件仍引用历史已删除的 `UniversalMediaPlaybackAdapter.cs`；不是本阶段新代码错误，需由 Unity Editor 编译确认 |
| Unity Player 截图/三端实播 | BLOCKED | 当前 Unity 编辑器被交互实例占用，且截图接口报 `SetIsBorderRequired failed`；未以设计图冒充运行截图 |
| 180 秒自动返回实机测试 | NOT RUN | 代码路径已接入，等待可用 Unity Player 做实际验收 |
| 正式讲解回归 | NOT RUN | 等待可用 LED/Touch Player 三端现场复测 |

## 修改范围

- `src/Shared/TG.Control.Contracts/WelcomeAttractContracts.cs`
- `src/Shared/TG.Control.Contracts/DomainModels.cs`
- `src/Shared/UnityContracts/Runtime/Contracts.cs`
- `src/Server/TG.Control.Server/{CommandBroker,WelcomeCoordinator,Program,UiExperiencePolicy}.cs`
- `src/LedPlayer/Assets/Scripts/{LedApiClient,LedWelcomePlaybackController,LedRuntimeBootstrap}.cs`
- `src/TouchClient/Assets/Scripts/{TouchApiClient,TouchControlFacade,TouchOperatorUi}.cs`
- `src/TouchClient/Assets/Scripts/UI/{TouchUiState,TouchUiPresenter}.cs`
- `src/TouchClient/Assets/Scripts/UI/Pages/WelcomeExperiencePage.cs`
- `src/AdminWeb/src/{api,main}.ts`

## 尚待现场验收

关闭或释放当前 Unity 编辑器后，使用正式发布的 `welcome.audio` 实测：待机 → 全屏触摸 → LED 缓存校验 → LED 音响欢迎语音 → 字幕 → 实际完成 → 首页 → 180 秒返回待机；另覆盖 LED 离线、SHA 失败、重复触摸、正式讲解/暂停竞争和 Server/LED/Touch 重启。截图必须来自实际 Windows Player。
