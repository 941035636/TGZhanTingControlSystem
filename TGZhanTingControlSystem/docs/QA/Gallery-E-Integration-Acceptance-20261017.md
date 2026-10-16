# Gallery E — 图集播放集成验收记录

> 日期：2026-10-17
>
> 分支：`codex/product-upgrade`
>
> 开发基线：`2bb50bc0233096800d82249c7fa139d8f95a4448`
>
> 状态：`LOCAL PASS / 三台现场设备验收未完成`

## 1. 本阶段范围

Gallery E不新增页面或产品功能，只验证Gallery A～D形成的完整链路：

```text
AdminWeb正式图集
  → Server权威会话
  → Prepare / Ready / Show
  → LedPlayer缓存、解码和双缓冲
  → TouchClient操作、状态及恢复
```

本轮使用当前源码重新生成的Windows Player，不使用历史构建目录。隔离运行数据位于系统临时目录，测试结束后自动删除，不读取或覆盖甲方正式数据。

## 2. 测试环境与素材

| 项目 | 实际环境 |
| --- | --- |
| 操作系统 | Windows，本机隔离三进程 |
| Unity | 2020.3.35f1c2 |
| Server | Release `net8.0`构建，由本机.NET 9 SDK运行 |
| LedPlayer图形设备 | Direct3D 11，AMD Radeon(TM) 530，约2GB显存 |
| TouchClient | 最新Windows x64 Player，1280×720窗口运行 |
| 图集素材 | 3个真实3840×2160 PNG，每个8,927,457字节 |
| UDP兼容端口 | 正式端口7002 |

本轮没有使用小图冒充4K图片。测试源文件为工程现有的真实3840×2160科技背景，复制为三个独立URL和缓存项进行切换。

## 3. 构建与静态回归

| 检查项 | 状态 | 结果 |
| --- | --- | --- |
| Server Release + AdminWeb生产构建 | PASS | 0警告、0错误；Vite生产构建成功 |
| LedPlayer Unity专项验证 | PASS | `GALLERY_C_VERIFICATION_PASS` |
| TouchClient Unity专项验证 | PASS | 退出码0，Gallery D边界全部通过 |
| LedPlayer Windows x64构建 | PASS | `Builds/GalleryE-LedPlayer/TG.Control.LedPlayer.exe` |
| TouchClient Windows x64构建 | PASS | `Builds/GalleryE-TouchClient/TG.Control.TouchClient.exe` |
| Deployment Security | PASS | 10/10 |
| Phase 9A～9E | PASS | 20/20、23/23、11/11、33/33、6/6 |
| Gallery A | PASS | 9/9 |
| Gallery B | PASS | 14/14，含本轮新增恢复回归 |
| AdminWeb | PASS | 15/15 |

## 4. Windows实际运行结果

可重复执行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\Verify-GalleryERuntime.ps1 -LegacyUdpPort 7002
```

| 场景 | 状态 | 实际结果 |
| --- | --- | --- |
| 三端同时启动 | PASS | Development Server、最新LedPlayer和最新TouchClient进程均稳定运行 |
| 第一张4K图片投放 | PASS | 完成Prepare、Ready、Show及Showing回执 |
| 手动下一张 | PASS | 第二张4K图片通过双缓冲链路替换第一张 |
| 4K短时自动轮播 | PASS | 三张4K图片均被轮播到，暂停后保持当前图片 |
| 短时内存观察 | PASS（短测） | 本轮轮播窗口LedPlayer工作集变化约0.4MB；不能替代8小时长稳 |
| TouchClient重启 | PASS | 从Server活动会话恢复并自动进入图片展示页面 |
| LedPlayer退出并重启 | PASS | 复用现有缓存，重新完成Prepare/Ready/Show并恢复同一图片 |
| Server重启 | PASS | 持久化会话恢复，LED重新确认同一图片为Showing |
| UDP 7002隔离 | PASS | `VoiceAdd`由旧协议桥接接收，图集会话未被终止或污染 |
| 欢迎流程抢占 | PASS（受控） | Server先停止图集；隔离数据未伪造欢迎音频，因此未声明真实听音通过 |
| 内容版本/顺序变化 | PASS（受控） | Server重启后安全丢弃不兼容的旧图集会话，没有把旧图片绑定到新版本 |

机器可读证据：`artifacts/gallery-e-runtime-verification.json`。

主要日志：

- `Builds/GalleryE-Runtime-Server-1.stdout.log`
- `Builds/GalleryE-Runtime-Server-2.stdout.log`
- `Builds/GalleryE-Runtime-Server-3.stdout.log`
- `Builds/GalleryE-Runtime-LedPlayer-1.log`
- `Builds/GalleryE-Runtime-LedPlayer-2.log`
- `Builds/GalleryE-Runtime-TouchClient-1.log`
- `Builds/GalleryE-Runtime-TouchClient-2.log`

## 5. 本轮发现并修复的缺陷

### Server重启后图集可能停留在Preparing

复现步骤：

1. 图集正在正常显示；
2. 保持LedPlayer运行并重启Server；
3. LedPlayer立即以内容版本V0、未就绪状态向新Server注册；
4. Server恢复活动图集，但严格内容版本门禁暂时拒绝向LED重发；
5. LedPlayer稍后完成V501同步并再次报告就绪，Server此前只识别“新进程实例”，没有再次触发恢复。

根因：`CommandBroker.Register`只在`InstanceId`变化时返回恢复信号，没有把同一实例的“未就绪 → 就绪”或“就绪内容版本变化”视为恢复时机。

最小修复：

- 同一客户端从未就绪变为就绪时触发一次恢复；
- 同一客户端以新内容版本进入就绪时触发一次恢复；
- 稳定的重复就绪心跳不重复触发恢复命令；
- 既有新实例恢复语义保持不变；
- Playback和Gallery共用该安全恢复时机，不修改两套状态机。

针对性回归：Gallery B新增“same LED instance becoming ready retriggers recovery”，并验证稳定就绪心跳不会产生重复恢复。修复后Gallery E完整链路重新执行，Server重启恢复PASS。

## 6. 未执行与现场待验

以下项目没有被写成PASS：

| 项目 | 状态 | 原因/现场要求 |
| --- | --- | --- |
| 三台物理电脑真实网络断开与恢复 | NOT RUN | 本机进程重启不能替代交换机/网卡级断网 |
| 8小时4K轮播和显存曲线 | NOT RUN | 当前只执行短时三图循环 |
| LED大屏人工视觉 | NOT RUN | 需要现场确认Contain黑边、250ms淡入淡出、无白屏和画质 |
| 真实MP4讲解抢占图集 | NOT RUN | 当前干净仓库没有真实MP4测试素材 |
| 真实欢迎音频播放抢占 | NOT RUN | 本轮只验证Server优先级，没有伪造音频或声称听音通过 |
| 管理端实际Rollback后三端一致性 | NOT RUN | Gallery A自动化与内容版本安全通过，真实管理端操作仍需现场验证 |
| 旧中控完整播放/暂停/继续 | NOT RUN | 仅确认UDP 7002通道与图集隔离；需要现场旧安卓中控和真实视频 |
| 物理触控与视觉截图 | NOT RUN | 需要现场触摸屏和LED大屏 |

## 7. 当前结论

Gallery E的本机可自动化集成范围已通过，且修复了一个由实际Server重启测试发现的恢复缺陷。当前代码具备进入三台甲方设备现场验收的条件，但在第6节项目完成前，图集功能不能标记为最终现场验收通过，也不应直接生成最终交付结论。
