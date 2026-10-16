# Gallery D — TouchClient图片展示验收记录

> 日期：2026-10-16
> 状态：`ACCEPTED / 2026-10-17用户确认完成`

## 1. 自动验证结果

| 项目 | 状态 | 结果 |
| --- | --- | --- |
| Unity脚本编译 | PASS | Unity 2020.3.35f1c2，0个编译错误 |
| Gallery D Editor专项验证 | PASS | 模块过滤、缩略图仅预览、显式投放、活动控制、导航入口和API边界通过 |
| TouchClient Windows x64构建 | PASS | `Builds/GalleryD-TouchClient/TG.Control.TouchClient.exe` |
| Gallery A回归 | PASS | 9/9 |
| Gallery B回归 | PASS | 13/13 |
| Server Release + AdminWeb生产构建 | PASS | 0警告，0错误 |
| `ImageGalleryPage`不直接调用API | PASS | 专项验证读取页面源码并检查边界 |
| Touch端未修改AVPro/UDP/TTS | PASS | 本阶段差异审计 |

## 2. Editor专项覆盖

批处理入口：

```text
TG.Control.Editor.GalleryDVerification.Run
```

覆盖：

1. 仅有图集的正式模块进入模块列表；
2. 点击第二张缩略图不会发出投放事件；
3. 点击“投放到大屏”发出正确`moduleId + imageIndex`；
4. 活动图集状态下“下一张”可用并发出控制意图；
5. App Shell存在“图片展示”导航；
6. `ImageGalleryPage`不引用`TouchApiClient`。

证据：

- `artifacts/gallery-d-editor-verification.json`；
- `artifacts/gallery-d-editor.log`；
- `artifacts/gallery-d-compile.log`。

## 3. 构建

- Unity版本：2020.3.35f1c2；
- Windows Player：`Builds/GalleryD-TouchClient/`；
- Windows构建日志：`Builds/GalleryD-TouchClient-build.log`；
- 生成的EXE：`TG.Control.TouchClient.exe`（653,824字节）。

## 4. 已验证行为

| 场景 | 状态 | 说明 |
| --- | --- | --- |
| 模块图集读取和排序 | PASS | 使用正式`PublishedContent.modules[].galleryImages` |
| 缩略图预览不误投屏 | PASS | 只有显式CTA发出业务事件 |
| 同模块选择图片投放 | PASS | 活动会话走`Show`，首张走`Start` |
| 上一张/下一张 | PASS | 页面意图、Facade及API接线完成；Server B回归通过 |
| 开始/暂停轮播 | PASS | 固定8秒UI意图，Server B状态机回归通过 |
| 停止展示 | PASS | Facade清理本地会话状态，Server/LED恢复逻辑由B/C负责 |
| Touch重启恢复 | PASS（代码路径+Server回归） | 启动读取`/api/gallery/active`并继续监控 |
| 图片异步切换防旧请求覆盖 | PASS（代码审计） | `TouchImageLoader`按Image实例和URL确认当前请求 |
| 活动图集阻止180秒欢迎 | PASS（代码路径） | `HasActiveGallery`加入空闲门禁 |

## 5. 未在Gallery D宣称通过

| 项目 | 状态 | 原因/后续阶段 |
| --- | --- | --- |
| 三台真实设备Touch→Server→LED人工触控链路 | NOT RUN | Gallery E现场集成验收 |
| 4K图片长时间轮播、显存和触控压力 | NOT RUN | Gallery E |
| Server/LED/Touch三端断网和进程重启实测 | NOT RUN | Gallery E |
| Rollback后Touch缩略图和LED画面一致性 | NOT RUN | Gallery E |
| 正式讲解/欢迎抢占的三端视觉实测 | NOT RUN | B/C自动与代码路径已覆盖，完整实测留Gallery E |
| UDP 7002现场回归 | NOT RUN | 本阶段未修改UDP，Gallery E统一回归 |
| 1920×1080现场屏人工视觉确认 | NOT RUN | 等待用户在Unity/Windows Player中确认 |

本阶段于2026-10-17获得用户确认，已进入Gallery E集成验收。
