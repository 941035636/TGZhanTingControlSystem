# Gallery D — TouchClient图片展示工作台

> 实施日期：2026-10-16
> 状态：`COMPLETE / 等待人工确认`
> 基线：Gallery A commit `d1a6db90a6b60ebae29ce4f23afc819dfea4e944`，Gallery B/C为当前同一未提交工作树上的已验收增量

## 1. 阶段边界

本阶段只实现TouchClient对Gallery B权威会话的操作界面和状态消费：

- 独立“图片展示”导航入口；
- 按正式内容读取有图集的模块；
- 缩略图浏览和本机大预览；
- 显式投放、上一张、下一张；
- 8秒自动轮播、暂停轮播、停止展示；
- Gallery活动会话恢复和状态刷新；
- Server/LED/欢迎/正式讲解冲突的用户反馈。

本阶段没有修改Gallery C图片渲染、AVPro、正式讲解协议、欢迎流程、MeloTTS或UDP 7002，也没有进入Gallery E三机现场压力和故障验收。

## 2. 业务边界

调用链保持为：

```text
ImageGalleryPage
  → 页面操作事件
  → TouchOperatorUi
  → TouchControlFacade
  → TouchApiClient
  → Gallery B Server API
```

`ImageGalleryPage`不引用`TouchApiClient`，不创建第二套图集状态机。Touch端只保存“当前正在本机预览哪一张”这一表现状态；当前大屏实际展示的模块、图片、轮播和错误均以Server的`GallerySessionStatus`为权威。

## 3. 交互规则

### 3.1 模块与缩略图

- 模块列表只显示当前正式内容中`enabled=true`且`galleryImages`非空的模块；
- 图集顺序严格使用发布内容中的数组顺序；
- 点击缩略图只更新TouchClient本机预览，不发送任何LED控制请求；
- 点击“投放到大屏”才会启动Gallery会话或在当前同模块会话中执行`Show`；
- 活动图集属于其他模块时，必须先停止当前图集，避免隐式切换模块。

### 3.2 控制

- 上一张/下一张由Server把相对动作转换为确定图片；
- 自动轮播首版使用固定8秒间隔，Server仍执行3～3600秒有效范围校验；
- 暂停轮播只停止自动换图，不停止当前图片展示；
- “停止展示”结束Gallery会话，LED恢复既有待机画面；
- 正式讲解或欢迎流程抢占时仍由Gallery B/C执行优先级，Touch随后从Server刷新到已结束状态。

## 4. 状态与恢复

`TouchControlFacade`新增：

- `ActiveGallerySessionId`；
- `CurrentGallery`；
- `HasActiveGallery`；
- `GalleryChanged`；
- 启动、Show、Previous、Next、Start/PauseSlideshow、Stop命令；
- 1秒活动会话监控；
- TouchClient启动时`GET /api/gallery/active`恢复。

`TouchUiPresenter`将该状态投影到`TouchUiState.Gallery / HasActiveGallery`。恢复到活动图集时，如果App Shell仍被欢迎页隐藏，UI会退出欢迎页并打开图片展示页面。活动图集存在时，180秒空闲欢迎不会自动触发。

## 5. 图片加载

图集预览复用`TouchImageLoader`的内存缓存和并发下载合并。本阶段新增`LoadContain`：

- 大图和缩略图保持宽高比，不裁切；
- 同一Image目标切换URL时，较早完成的异步请求不会覆盖新选择；
- 下载失败保持稳定占位，不影响Server/LED端正式资产缓存。

## 6. 导航和视觉

- App Shell新增真实功能入口“图片展示”；
- 图集活动时入口显示绿色活动点；
- 页面复用现有深蓝科技主题、触控按钮和ContentHost；
- 页面区域为模块列表、大预览、横向缩略图、状态与操作区；
- 所有图片仍是独立UGUI交互元素，没有把页面烘焙成整图。

现有素材包没有独立Gallery导航图标，首版复用现有“主题/图层”图标。该视觉差异应在后续正式素材补齐时替换，不影响业务验收。

## 7. 下一阶段边界

Gallery E只做完整集成验收：真实三机、4K长时间、断网与重连、Server/LED/Touch重启、Rollback、正式讲解/欢迎抢占和UDP 7002回归。不得借Gallery E重新设计当前页面或改写Gallery B/C状态机。
