# Gallery B — Server协调器验收记录

> 日期：2026-10-15
> 状态：`COMPLETE / 等待人工确认`

## 自动化结果

| 项目 | 结果 | 说明 |
| --- | --- | --- |
| Gallery B专项测试 | PASS | 13/13 |
| Gallery A回归 | PASS | 9/9：领域、发布、Manifest和Rollback |
| Server Release构建 | PASS | 含AdminWeb生产构建，0 warning / 0 error |
| Contracts构建 | PASS | 0 warning / 0 error |
| TouchClient脚本编译 | PASS | Unity 2020.3.35f1c2 batchmode，退出码0 |
| LedPlayer脚本编译 | PASS | Unity 2020.3.35f1c2 batchmode，退出码0；本阶段未实现图片渲染 |

Gallery B专项测试覆盖：

1. 启动下发含真实AssetId/SHA/Size/MediaType的Prepare；
2. Ready后下发计划Show，Showing更新会话；
3. 迟到Ready不能覆盖更新后的目标图片；
4. 显示失败持久化错误并自动暂停轮播；
5. Previous/Next/Show和轮播状态确定性；
6. 正式讲解与欢迎流程阻止图集启动；
7. 高优先级流程先停止图集；
8. 会话持久化、Server重建和LED重连重发；
9. Published Content版本变化时丢弃旧会话；
10. 缺少SHA/Size的图片资产被拒绝；
11. LED离线和严格版本门禁；
12. Gallery/Playback/Welcome三命令通道隔离；
13. 协议枚举值与JSON往返。

## 当前不能验收的项目

| 项目 | 状态 | 原因 |
| --- | --- | --- |
| LED真实图片显示 | NOT RUN | Gallery C尚未实现LedPlayer图片渲染器 |
| 图片缓存、损坏重下、双缓冲 | NOT RUN | Gallery C范围 |
| Touch图集页面 | NOT RUN | Gallery D范围 |
| 三端现场图片投放 | BLOCKED | 依赖Gallery C和Gallery D |

上述项目不得因Server测试通过而标记为PASS。

## 构建证据

- `Builds/GalleryB-TouchClient-compile.log`
- `Builds/GalleryB-LedPlayer-compile.log`
- `dotnet build src/Server/TG.Control.Server/TG.Control.Server.csproj -c Release`
- `dotnet run --project tests/TG.Control.GalleryA.Tests/TG.Control.GalleryA.Tests.csproj -c Release -p:SkipAdminWebBuild=true`
- `dotnet run --project tests/TG.Control.GalleryB.Tests/TG.Control.GalleryB.Tests.csproj -c Release -p:SkipAdminWebBuild=true`

说明：一次将Gallery B测试和Server构建并行执行时，两者同时写入同一Server `obj` 目录，出现临时 `CS2012` 文件锁。关闭MSBuild编译服务器后按顺序执行，最终Release构建成功；该事件不属于源码编译错误。
