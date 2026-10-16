# Gallery B — Server协调器

> 实施日期：2026-10-15
> 状态：`ACCEPTED / 2026-10-16人工验收通过`
> 基线：Gallery A commit `d1a6db90a6b60ebae29ce4f23afc819dfea4e944`

## 1. 阶段边界

本阶段只建立 Server 权威的图片图集协调能力：独立协议、命令通道、活动会话、持久化、冲突规则、重启恢复和 LED 重连重发。

本阶段没有实现：

- LedPlayer 图片下载、SHA/Size校验、解码和双缓冲渲染（Gallery C）；
- TouchClient 图集选择与控制页面（Gallery D）；
- 现场三端完整图片投放验收（Gallery E）。

## 2. 协议与命令通道

新增独立 `GalleryDisplayCommand`，不复用、不修改 `PlaybackAction`：

- LED 命令：`Prepare(0) / Show(1) / Stop(2)`；
- LED 状态：`Received / Ready / Showing / Failed / Stopped`；
- 操作员控制：`Show / Previous / Next / StartSlideshow / PauseSlideshow / Stop`；
- 显示方式：V1 固定 `Contain`，保留完整图片，Gallery C 以黑色补边。

命令携带完整不可变资产身份：`AssetId + Url + SHA256 + SizeBytes + MediaType`，并携带 `SessionId / ModuleId / ImageId / ContentVersion / Sequence / CommandId / ExecuteAtUtc`。

独立长轮询端点：

```text
GET /api/gallery/commands/next?clientId=led-main
```

图集命令使用 `CommandBroker` 的独立 Channel，不会被正式播放或欢迎词轮询消费。

## 3. API

```text
POST /api/gallery/start
POST /api/gallery/control
GET  /api/gallery/active
GET  /api/gallery/sessions/{sessionId}
POST /api/gallery/status
```

- 操作API沿用现有管理员Operator或终端认证；
- LED命令轮询和状态上报沿用终端密钥认证；
- 未新增对外匿名接口。

## 4. Prepare → Ready → Show

1. Server从当前正式 `PublishedContent` 解析模块和有序 `GalleryImages`；
2. Server验证图片类型、AssetId、URL、SHA-256、Size、MediaType；
3. Server下发 `Prepare`；
4. 只有当前目标图片、当前 `PrepareCommandId` 的 `Ready` 回执才有效；
5. Server下发带计划执行时间的 `Show`；
6. LED上报当前 `ShowCommandId` 的 `Showing` 后，会话状态变为展示中。

连续快速切图时，旧图片的迟到 `Ready/Showing` 回执会被丢弃，不能覆盖新目标。

## 5. 会话状态与持久化

活动会话保存到：

```text
Data/active-gallery-session.json
```

快照包含：内容版本、模块、当前图片身份和序号、展示状态、轮播状态/间隔、错误和更新时间。

Server重启时：

- 当前Published Content版本、模块、图片序号和AssetId均兼容时恢复；
- 等待LED重新注册，再重新下发当前图片的 `Prepare`；
- 内容版本或图片身份变化时，确定性丢弃旧会话并清除快照；
- 不会把旧版本图片绑定到新正式内容。

## 6. 优先级和互斥

优先级保持：

1. 正式自动讲解；
2. 欢迎词；
3. 手动图片图集；
4. LED待机画面。

规则：

- 正式讲解活动时拒绝新图集；
- 欢迎流程处于 Requested/Preparing/Playing 时拒绝新图集；
- 启动正式讲解或欢迎流程时，在同一图集切换锁内先发送 `Stop`、清除图集快照，再执行高优先级操作；
- 快速并发的图集启动与高优先级启动不会越过该切换锁；
- 图集不占用UDP 7002，不修改旧安卓视频兼容通道。

## 7. LED门禁

图集复用现有 `PlaybackOptions`：

- LED离线时拒绝启动；
- `RequireLedReadyBeforeStart`、`AllowDegradedPlayback`、内容版本和Ready规则与现有正式播放门禁一致；
- 受限模式允许时，Gallery C 可按命令中的完整资产信息按需下载；
- 严格模式下内容版本不一致或LED未Ready会拒绝启动。

## 8. 轮播

Server保存轮播是否启用和间隔（3～3600秒）。轮播计时由Server执行，只在当前图片已确认 `Showing` 后推进下一张；`Failed` 会暂停轮播，避免错误状态下连续切换。Server重启恢复轮播配置。

## 9. 操作日志

关键动作写入现有JSONL操作日志，Category为 `Gallery`：

- Start / Show / Previous / Next；
- StartSlideshow / PauseSlideshow / Stop；
- DisplayFailed；
- Recovered / RecoveryDiscarded / ClientRecovered；
- Preempted。

## 10. 下一阶段边界

Gallery C只应实现 LedPlayer 侧：

- 图集命令长轮询和状态上报；
- 复用 `LedContentCache` 的真实 SHA/Size 校验；
- 图片解码、双缓冲、Aspect Fit、失败保留旧图；
- Stop 后恢复待机画面；
- 不修改 AVPro 视频播放和旧 UDP 兼容逻辑。

TouchClient 页面仍属于 Gallery D。
