# TG展厅自动讲解系统

新版系统面向55英寸Windows触控终端、1920×1080 LED大屏和Windows服务器。首期聚焦12模块内容管理、人工组合讲解、TTS语音、LED画面同步和大文件视频播放；旧平板中控继续独立负责现场设备。

## 工程结构

| 工程 | 技术 | 部署位置 | 职责 |
| --- | --- | --- | --- |
| `src/TouchClient` | Unity 2020.3.35f1c2 | 55英寸Windows触控终端 | 模块选择、人工启动和讲解控制 |
| `src/LedPlayer` | Unity 2020.3.35f1c2 + AVPro Video | LED控制主机 | 1920×1080视频与讲解音频缓存、预加载、同步播放和状态反馈 |
| `src/Server/TG.Control.Server` | ASP.NET Core 8 | Windows服务器 | 内容草稿/发布、客户端通信、讲解编排、统一起播和TTS生产内核 |
| `src/AdminWeb` | TypeScript + Vite | 由Windows服务托管 | 电脑端模块、内容和版本管理 |

共享定义位于：

- `src/Shared/TG.Control.Contracts`：服务端.NET协议库；
- `src/Shared/UnityContracts`：两个Unity客户端共用的本地UPM包。

## 当前已实现

- 默认创建12个展厅模块；
- JSON内容仓库与原子发布；
- 电脑端模块查看、新增、启停、简介编辑、Server草稿保存和显式发布；
- 讲解节点新增、删除、排序、文案和故障策略编辑；
- 宣传视频、图片、动画及讲解音频流式上传与SHA-256校验；
- 管理端账号登录、12小时会话和发布权限校验；
- 触控端基于UGUI的12模块选择、常用路线、任意组合、一键全讲及连接状态界面，按1920×1080设计；
- LED端全屏视频画布和UGUI待机层，讲解结束、终止或播放失败后自动返回待机界面；
- 管理端可独立发布两端标题、背景、配色，以及LED待机图片/循环视频；
- 触控端与LED端注册、断线重试和长轮询指令；
- 任意顺序模块启动；
- 活动讲解暂停、继续、跳过当前节点和终止；
- 服务端为LED主机上的视频与讲解音频生成相同计划起播时间；
- 多节点顺序推进；
- 节点失败后按`Skip/Stop`策略处理；
- LED端下载讲解音频，并与视频在同一播放主机按计划时间同步起播；
- LED端大文件断点下载和本地URL播放；
- AVPro播放器适配层；
- Windows服务宿主支持；
- 管理端静态文件由服务端统一托管。
- LED终端在线即可开始讲解；内容版本不一致或部分素材同步失败时进入“受限可用”，当前节点素材按需下载，仍失败则按节点策略跳过或停止；
- 活动讲解会话持久化，Windows服务重启后可恢复当前节点；
- 常用路线支持管理端维护，中控端支持临时组合、保存、另存和一键讲解；
- 内容版本保留历史并支持回滚，回滚操作生成新的正式版本；
- 正式发布、历史回滚和终端界面发布均校验服务器素材文件；文件缺失、为空或大小不一致时阻止发布，并定位到模块、节点和素材；
- 关键发布、路线和讲解操作写入运行日志；
- 管理端按“内容中心、讲解路线、发布与回滚、终端与运行”划分工作区；
- 管理端未发布草稿持久化在Server，并以草稿修订号进行乐观并发保护；浏览器localStorage只保留同一修订号下的意外刷新恢复副本；
- 管理端支持动态Provider/Voice、异步生成任务、Candidate试听、显式采用和Server权威Fresh/Stale状态；生成、采用和发布彼此独立；

## 本地构建

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-All.ps1
```

运行服务：

```powershell
dotnet run --project src\Server\TG.Control.Server\TG.Control.Server.csproj
```

访问：

- 管理端：`http://localhost:5080/`
- 健康检查：`http://localhost:5080/api/health`

本地演示账号：`admin`，初始密码：`TG@2026`。部署到现场前必须在
`src/Server/TG.Control.Server/appsettings.json`中修改初始密码。

两个Unity终端默认使用请求头`X-TG-Terminal-Key`访问终端接口。开发环境默认密钥为
`TG-TERMINAL-2026`，现场部署前必须同时修改服务端配置和两个Unity客户端Inspector中的密钥。

## Unity

使用Unity `2020.3.35f1c2`分别打开：

- `src/TouchClient`
- `src/LedPlayer`

两个工程采用运行时自动引导脚本，即使当前场景为空，也会创建基础通信、播放组件和
可操作的UGUI界面。触控端与LED端统一以1920×1080为设计基准，均不依赖场景天空盒。

LED工程正式运行前必须导入已授权的AVPro Video，并按`docs/03_AVPro接入说明.md`配置。

## 重要边界

- 新系统首期不接入TCP、串口、继电器、红外或WOL设备；
- 旧平板中控与新版系统并存；
- 讲解仅人工启动；
- 不开发移动端后台；
- 首期仅支持中文；
- 声画同步验收误差小于1秒，内部目标为500毫秒；
- 正式视频先下载到LED主机本地，再交给AVPro播放，不直接依赖Wi-Fi流式播放超大文件。
