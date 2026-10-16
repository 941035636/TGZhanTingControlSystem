# 模块图集投放到 LED 大屏 — 封存开发计划

> 状态：`IN PROGRESS / Gallery D TouchClient已完成，等待人工验收后进入 Gallery E`
>
> 记录日期：2026-10-13
>
> 方案审计基线：`4ce82a1fb9c8ddacf4c843d75037ca6a483d4401`
>
> 计划分支：`codex/product-upgrade`

## 1. 需求目标

在 TouchClient 增加独立的“图片展示”能力：操作员先选择展厅模块，再从该模块的图集中选择图片并投放到 LED 大屏。一个模块对应一个有序图集，图集中可以包含多张图片。

本功能不得破坏或替换现有能力：

- 正式自动讲解 Session；
- AVPro 视频播放；
- 欢迎词播放；
- LED 待机界面；
- UDP `7002` 旧安卓中控兼容；
- 内容发布、版本历史、回滚和 LED 素材缓存；
- TTS/讲解音频现有链路。

本计划只记录后续实施方案，不属于当前三项关键 Bug 的打包范围。

## 2. 当前源码事实

当前系统已经具备以下可复用基础：

- `AssetKind.Image` 和完整的 `ContentAsset` 身份：`AssetId + URL + SHA256 + SizeBytes + MediaType`；
- AdminWeb 图片上传入口；
- 内容发布、不可变版本、回滚；
- `ContentManifestBuilder` 和 LED 本地缓存；
- Server 到 LED 的长轮询命令通道；
- LED 待机页已经具备图片下载与 Unity Texture 解码代码。

当前缺口：

- `ExhibitionModule` 没有独立的模块级图集；
- 正式协议只有视频/音频播放动作；
- LED 没有正式图片展示控制器；
- TouchClient 没有图集选择和投放页面；
- Server 没有图集展示会话、状态恢复和与正式讲解的互斥规则。

## 3. 产品交互方案

### 3.1 模块选择

新增独立导航入口“图片展示”。首页继续使用真实12模块数据和现有摄影卡片视觉，展示：

- 模块封面；
- 模块名称；
- 图集图片数量；
- 当前是否正在大屏展示。

### 3.2 图集页面

选择模块后进入图集视图：

- 左侧/中央显示当前图片大预览；
- 底部显示可横向滚动的缩略图列表；
- 右侧显示模块名称、当前序号、总数、LED状态；
- 主操作为“投放到大屏”；
- 控制包括上一张、下一张、停止展示；
- 自动轮播作为可选能力，第一版默认手动翻页。

为了避免现场误触，第一版约定：点击缩略图只改变中控预览，点击“投放到大屏”后才改变 LED 画面。进入投放状态后是否允许点击缩略图直接切换，可在人工体验确认后决定。

## 4. 领域模型

建议给 `ExhibitionModule` 增加向后兼容的可空/空数组字段：

```text
GalleryImages: ContentAsset[]
```

约束：

- 仅允许 `AssetKind.Image`；
- 数组顺序就是图集顺序；
- 图片名称作为管理端可编辑标题，第一版不增加复杂字幕模型；
- 旧 JSON 缺少字段时按空数组处理；
- 图片继续进入现有不可变资产体系，不建立第二套文件存储。

不建议把图集图片继续堆入讲解节点 `NarrationNode.Assets`，否则图片展示与正式讲解节点语义混杂，后续发布校验和路线播放容易互相影响。

## 5. Server 方案

新增独立 `ImageGalleryCoordinator`，不要把图集状态机写入 `PlaybackCoordinator`。

建议 API：

```text
POST /api/gallery/start
POST /api/gallery/control
GET  /api/gallery/active
GET  /api/gallery/sessions/{sessionId}
POST /api/gallery/status
```

控制语义：

```text
Show / Previous / Next / StartSlideshow / PauseSlideshow / Stop
```

Server 权威职责：

- 从当前正式内容版本解析模块和图片；
- 校验 LED 在线、内容版本和素材完整性；
- 保存当前模块、图片序号、展示状态和轮播状态；
- 把上一张/下一张转换为确定的目标图片；
- 防止连续点击导致旧命令覆盖新命令；
- Server 重启后恢复会话；
- LED 重连后重新下发当前图片；
- 写入 JSONL 关键操作日志。

## 6. 指令与会话隔离

推荐参照现有欢迎词通道，新增独立图集命令，不修改正式 `PlaybackAction` 的既有数值和语义。

```text
GalleryDisplayAction: Prepare / Show / Stop
GalleryDisplayCommand:
  Sequence, CommandId, SessionId, ModuleId, ImageId,
  ContentVersion, AssetId, Url, Sha256, SizeBytes,
  MediaType, ExecuteAtUtc, FitMode
```

LED 先收到 `Prepare`，完成缓存校验和纹理解码后上报 Ready；Server 再发送 `Show`。这能避免首次展示时出现白屏、旧图闪现或下载等待。

## 7. LED 播放端方案

新增 `LedImageDisplayController`，只处理图集指令，不修改 AVPro 适配层。

建议画面层级：

```text
黑色底层
图片展示层
AVPro 视频层
状态/故障提示层
```

实现要求：

- 使用 `LedContentCache`，按真实 SHA/Size 校验；
- 使用双缓冲图片层，切换时约 200～300ms 淡入淡出；
- 默认 `Aspect Fit`，完整显示图片，空白处使用黑色；
- 正式内容同步阶段预缓存模块图集资产；目标图片收到 `Prepare` 后才解码到备用纹理，不在LED内维持额外的上一张/下一张4K纹理；
- 切换后及时释放旧 Texture/Sprite，防止大量4K图片造成显存增长；
- 停止后恢复管理端配置的 LED 待机画面；
- 图片失败时保留上一张有效图片并向 Server 上报明确错误，不显示白屏。

正式首版建议只支持 JPG/JPEG、PNG。WebP 在当前 Unity 运行环境完成兼容验证前不开放。

## 8. 内容发布与缓存

AdminWeb 每个模块增加“图片图集”区域：

- 批量上传；
- 拖动排序；
- 预览；
- 修改名称；
- 删除；
- 发布前检查格式、文件存在、SHA和Size。

发布后图集进入现有 Manifest。相同 AssetId 被多个模块引用时只下载一次。Rollback 必须恢复历史图集顺序及历史资产引用。

建议上传提示：优先 1920×1080 或同为16:9的横屏图片，最高允许4K；其他比例可以播放，但使用黑边完整显示。

## 9. 播放优先级和冲突规则

建议优先级：

1. 正式自动讲解；
2. 欢迎词；
3. 手动图片展示；
4. LED 待机画面。

规则：

- 正式讲解进行中禁止启动图集；
- 图集展示中启动正式讲解时，先可靠停止图集，再开始讲解；
- 欢迎词正在准备或播放时禁止启动图集；
- LED离线或正式内容版本不满足现有门禁时禁止投放；
- TouchClient 重启后从 Server 恢复当前图集状态；
- 图集功能不得占用或修改 UDP `7002`。

## 10. 建议实施阶段

1. **Gallery A — 领域与发布基础**：Contracts、AdminWeb模块图集、发布校验、Manifest、Rollback测试。
2. **Gallery B — Server协调器**：独立命令通道、会话持久化、互斥和恢复。
3. **Gallery C — LED渲染**：缓存、解码、双缓冲切换、错误上报、资源释放。
4. **Gallery D — TouchClient页面**：模块选择、缩略图、投放、前后切换、停止与状态恢复。
5. **Gallery E — 集成验收**：大图压力、断网、重连、Server/LED/Touch重启、正式讲解切换、Rollback和UDP回归。

当前实施进度（2026-10-16）：

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| Gallery A | `ACCEPTED` | 领域模型、AdminWeb模块图集、发布校验、Manifest及Rollback自动化测试已验收。 |
| Gallery B | `ACCEPTED` | 独立命令通道、Server协调器、会话持久化、互斥、重启恢复与LED重连重发已通过人工验收。 |
| Gallery C | `ACCEPTED` | LED图片缓存完整性校验、解码、双缓冲、Contain显示、失败上报、优先级撤屏和资源释放已完成人工验收。 |
| Gallery D | `COMPLETE / 待人工确认` | 已完成独立图片展示入口、模块与缩略图预览、显式投放、前后切换、8秒轮播、停止、错误反馈及活动会话恢复。 |
| Gallery E | `NOT STARTED` | 尚未开始。 |

首个可测试版本预计5～7个工作日；含自动轮播、恢复和现场压力测试的交付版本建议预留7～10个工作日。

## 11. 后续开发启动条件

开始开发前必须先完成当前三项关键 Bug 的统一安装包和现场验收，并建立干净Git基线。后续直接读取本文件继续，不需要重新设计，但开始修改前仍须以当时本地源码重新审计接口和工作树状态。
