# Gallery C — LED图片渲染

> 实施日期：2026-10-16
> 状态：`COMPLETE / 等待人工确认`
> 基线：Gallery A commit `d1a6db90a6b60ebae29ce4f23afc819dfea4e944`，Gallery B为同一未提交工作树上的已验收增量

## 1. 阶段边界

本阶段只实现 LedPlayer 对 Gallery B 正式协议的消费：长轮询、完整性缓存、图片解码、双缓冲显示、状态回执、失败保持和资源释放。

本阶段没有实现：

- TouchClient 图片展示页面、缩略图和操作入口（Gallery D）；
- 长时间4K压力、三机断网/重启、Rollback和现场视觉验收（Gallery E）；
- 新图片格式、缩放手势、字幕或复杂转场；
- 对 AVPro、正式讲解状态机、欢迎音频、UDP 7002 的行为修改。

## 2. 命令接入

`LedApiClient` 新增独立 Gallery 长轮询和状态上报：

```text
GET  /api/gallery/commands/next?clientId=led-main
POST /api/gallery/status
```

命令仍使用 Gallery B 定义的独立 `GalleryDisplayCommand`，不复用 `PlaybackCommand`，也不改变其枚举值、API路径或认证方式。

## 3. 缓存和完整性

目标图片进入解码前必须具备：

- `AssetId`、URL和图片身份；
- 正数 `SizeBytes`；
- 64位 SHA-256；
- `image/jpeg` 或 `image/png`。

图片复用现有 `LedContentCache`：

1. 正式内容 Manifest 会预缓存全部已发布图集资产；
2. Gallery `Prepare` 始终把命令中的 Size/SHA 注册为本次权威校验值；
3. 已存在缓存若 Size/SHA 不匹配会被删除并重新下载；
4. `file://` 和绝对本地路径也不再绕过 Size/SHA 校验；
5. 下载完成后再次校验，校验成功才交给 Unity 图片解码。

没有 SHA/Size 的新 Gallery 命令会直接失败，不会退化成 Legacy 弱校验。

## 4. 渲染层与双缓冲

`LedImageDisplayController` 运行时创建独立 UGUI Canvas：

- 参考分辨率：1920×1080；
- Canvas `sortingOrder = 10`；
- AVPro正式视频 Canvas为0，待机/状态Canvas为20；
- Gallery激活时待机内容根节点隐藏，Gallery停止时恢复；
- 黑色底层 + A/B两个非交互图片层；
- `Image.preserveAspect = true`，V1固定 `Contain`，不裁切图片；
- 所有图片层 `raycastTarget = false`。

切图流程：

1. 新图片下载并在备用层解码；
2. LED上报 `Ready`；
3. 收到Server的 `Show` 后遵守可信范围内的计划时间；
4. A/B层在约250ms内交叉淡入淡出；
5. 成功切换后立即释放旧 Sprite/Texture；
6. 上报 `Showing`。

协议只携带当前目标资产，没有模块完整图集列表。因此V1不在显存中同时预解码上一张和下一张；所有图片先由Manifest落盘缓存，当前目标再通过 `Prepare` 解码到备用层。这样避免三张4K纹理长期占用显存，同时仍保证 `Show` 前已完成目标图片解码。

## 5. 失败与恢复

- 下载、Size/SHA、格式或Unity解码失败时上报 `Failed`；
- 切换新图失败时保留上一张已成功图片，不切白屏；
- 第一张失败时Gallery Canvas从未启用，继续显示原待机界面；
- `Show` 在进程重启后先于内存 `Prepare` 状态到达时，利用Show命令的完整资产信封重新准备并显示；
- `Stop`取消准备/转场、释放两组纹理并恢复待机界面；
- 图片尺寸上限为单边8192像素，避免异常图片导致不可控纹理分配。

## 6. 播放优先级

Server仍是业务互斥权威。由于正式播放、欢迎和Gallery使用独立长轮询通道，跨通道到达顺序不能作为画面安全保证，所以LED本地再执行一次只读优先级保护：

- 收到正式讲解 `Prepare / PlayVideo / PlayNarration`，立即撤下Gallery；
- 收到欢迎 `Prepare / Play`，立即撤下Gallery；
- 不修改 `PlaybackCoordinator`、AVPro或欢迎状态机；
- 不占用、不拦截UDP 7002。

## 7. 文件职责

- `LedImageDisplayController.cs`：Gallery命令、缓存/解码、双缓冲、回执、资源生命周期；
- `LedApiClient.cs`：Gallery独立长轮询和状态上报；
- `LedContentCache.cs`：本地URL/路径也执行完整性校验；
- `LedStatusOverlay.cs`：Gallery活动期间隐藏并暂停待机内容；
- `LedRuntimeBootstrap.cs`：创建和注入Gallery控制器；
- `GalleryCVerification.cs`：确定性Unity边界验证；
- `scripts/Verify-GalleryCRuntime.ps1`：隔离数据目录下的Server + Windows Player真实链路验收。

## 8. 下一阶段边界

Gallery D只实现 TouchClient：模块图集入口、缩略图预览、投放、前后切换、轮播控制、停止和活动状态恢复。不得把图片渲染状态机复制到Touch端。

Gallery E再进行三机现场视觉、4K长时间、断网/重连、Server/LED/Touch重启、Rollback、正式讲解抢占和UDP回归。
