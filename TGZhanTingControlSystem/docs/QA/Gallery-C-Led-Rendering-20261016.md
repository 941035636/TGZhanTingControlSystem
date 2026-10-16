# Gallery C — LED图片渲染验收记录

> 日期：2026-10-16
> 状态：`COMPLETE / 等待人工确认`

## 1. 自动验证结果

| 项目 | 状态 | 结果 |
| --- | --- | --- |
| Unity脚本编译 | PASS | Unity 2020.3.35f1c2，0个编译错误 |
| Gallery C Editor专项验证 | PASS | Canvas层级、双缓冲、Contain、Raycast、命令格式、正式讲解/欢迎撤屏、Size/SHA均通过 |
| LedPlayer Windows x64构建 | PASS | `Builds/GalleryC-LedPlayer/TG.Control.LedPlayer.exe` |
| Windows Player真实链路 | PASS | Server → Prepare → Ready → Show A → Next → Show B → Stop |
| 同大小损坏缓存恢复 | PASS | 预置同Size错误SHA文件，实际检测、删除并重新下载后成功显示 |
| Gallery A回归 | PASS | 9/9 |
| Gallery B回归 | PASS | 13/13 |
| Server Release + AdminWeb生产构建 | PASS | 0警告，0错误 |

## 2. Gallery C Editor专项覆盖

批处理入口：

```text
TG.Control.LedPlayer.Editor.GalleryCVerification.Run
```

覆盖：

1. Gallery Canvas固定在排序层10；
2. 初始隐藏，不遮挡待机画面；
3. 黑底和A/B两个图片缓冲存在；
4. 图片缓冲使用`preserveAspect`，装饰层不拦截输入；
5. 有效PNG/JPEG命令通过；
6. 缺少SHA或不支持的WebP被拒绝；
7. 正式讲解Prepare立即撤下Gallery；
8. 欢迎Prepare立即撤下Gallery；
9. 正确Size/SHA通过，Size错误和SHA错误失败。

证据：`Builds/GalleryC-Verification.log` 中 `GALLERY_C_VERIFICATION_PASS`。

## 3. Windows Player真实链路

可重复脚本：

```powershell
./scripts/Verify-GalleryCRuntime.ps1
```

脚本使用隔离的临时Server数据、独立LED缓存和显式 `TG_LED_PLAYER_CONFIG`，不会读取或覆盖正式数据。实际执行：

1. 创建两个真实PNG不可变资产；
2. 在LED缓存预置一个Size正确但SHA错误的同名文件；
3. 启动Development Server和本轮Windows Player；
4. 等待真实 `led-main` 注册；
5. 启动图集，图片A完成Prepare/Ready/Show；
6. Next到图片B，完成备用层解码和切换；
7. Stop并确认Server清除活动会话；
8. 直接复核缓存文件SHA已经恢复成正式资产SHA。

日志证据：

- `Builds/GalleryC-Runtime-LedPlayer.log`；
- `Builds/GalleryC-Runtime-Server.stdout.log`；
- `Builds/GalleryC-Runtime-Server.stderr.log`。

本机正式ProgramData配置指向旧HTTPS现场地址，首次验收因此出现证书错误。验收脚本随后通过产品已有的 `TG_LED_PLAYER_CONFIG` 注入隔离的127.0.0.1配置并通过；没有为测试修改产品连接逻辑。

## 4. 失败保持和资源规则

| 场景 | 状态 | 说明 |
| --- | --- | --- |
| 新图下载/校验失败保留旧图 | PASS（代码路径+专项校验） | 失败缓冲被释放，活动缓冲不变 |
| 第一张失败继续显示待机 | PASS（代码路径） | Gallery仅在Show转场开始时启用 |
| Stop恢复待机 | PASS（真实链路） | Stop已消费，活动会话清除，Gallery释放并隐藏 |
| 切图释放旧Texture/Sprite | PASS（代码审计） | `CommitTransition`释放旧缓冲 |
| 超大图保护 | PASS（代码审计） | 单边超过8192时失败并释放纹理 |

## 5. 未在Gallery C宣称通过的项目

| 项目 | 状态 | 原因/后续阶段 |
| --- | --- | --- |
| 4K图片长时间轮播和显存曲线 | NOT RUN | Gallery E现场压力测试 |
| LED断网、重连和进程重启后的真实画面恢复 | NOT RUN | Server自动化已覆盖命令恢复，真实三机留Gallery E |
| Server重启、Rollback后的真实图片切换 | NOT RUN | Gallery E |
| TouchClient图集操作 | NOT RUN | Gallery D尚未实现 |
| 正式讲解/欢迎抢占的三端视觉实测 | NOT RUN | LED本地撤屏专项通过，完整三端留Gallery E |
| UDP 7002现场回归 | NOT RUN | 本阶段未修改UDP，Gallery E统一回归 |

## 6. 构建与日志

- Unity版本：2020.3.35f1c2；
- Windows Player：`Builds/GalleryC-LedPlayer/`；
- Windows构建日志：`Builds/GalleryC-LedPlayer-build.log`；
- Editor专项日志：`Builds/GalleryC-Verification.log`。

本阶段未提交、未推送；等待人工确认后再决定是否进入Gallery D。
