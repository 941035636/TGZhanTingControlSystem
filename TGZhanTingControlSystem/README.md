# TG展厅自动讲解系统

新版系统面向55英寸Windows触控终端、1920×1080 LED大屏和Windows服务器。首期聚焦12模块内容管理、人工组合讲解、TTS语音、LED画面同步和大文件视频播放；旧平板中控继续独立负责现场设备。

## 工程结构

| 工程 | 技术 | 部署位置 | 职责 |
| --- | --- | --- | --- |
| `src/TouchClient` | Unity 2020.3.35f1c2 | 55英寸Windows触控终端 | 模块选择、人工启动、讲解控制、TTS音频播放 |
| `src/LedPlayer` | Unity 2020.3.35f1c2 + LibVLC | LED控制主机 | 1920×1080视频缓存、预加载、播放、状态反馈 |
| `src/Server/TG.Control.Server` | ASP.NET Core 8 | Windows服务器 | 内容发布、客户端通信、讲解编排、统一起播、TTS接口 |
| `src/AdminWeb` | TypeScript + Vite | 由Windows服务托管 | 电脑端模块、内容和版本管理 |

共享定义位于：

- `src/Shared/TG.Control.Contracts`：服务端.NET协议库；
- `src/Shared/UnityContracts`：两个Unity客户端共用的本地UPM包。

## 当前已实现

- 默认创建12个展厅模块；
- JSON内容仓库与原子发布；
- 电脑端模块查看、新增、启停、简介编辑和直接发布；
- 讲解节点新增、删除、排序、文案和故障策略编辑；
- 宣传视频、图片、动画及讲解音频流式上传与SHA-256校验；
- 管理端账号登录、12小时会话和发布权限校验；
- 触控端12模块选择、任意组合路线、一键全讲及连接状态界面；
- LED端LibVLC全屏画布和待机/连接状态界面；
- 触控端与LED端注册、断线重试和长轮询指令；
- 任意顺序模块启动；
- 活动讲解暂停、继续、跳过当前节点和终止；
- 服务端为TTS和LED生成相同计划起播时间；
- 多节点顺序推进；
- 节点失败后按`Skip/Stop`策略处理；
- 触控端下载并播放TTS音频；
- LED端大文件断点下载和本地URL播放；
- LibVLC播放器适配层，播放控制接口与上层业务解耦；
- Windows服务宿主支持；
- 管理端静态文件由服务端统一托管。

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

## Unity

使用Unity `2020.3.35f1c2`分别打开：

- `src/TouchClient`
- `src/LedPlayer`

两个工程采用运行时自动引导脚本，即使当前场景为空，也会创建基础通信、播放组件和
可操作的演示界面。触控端以2800×1840为设计基准，LED端以1920×1080为设计基准。

LED工程已经包含Windows x64 LibVLC运行库，不需要在目标电脑另行安装VLC或导入AVPro。
播放器迁移原因和验证状态见`docs/03_AVPro接入说明.md`，最新开发进度见
`docs/05_开发进度_2026-08-25.md`。

## 重要边界

- 新系统首期不接入TCP、串口、继电器、红外或WOL设备；
- 旧平板中控与新版系统并存；
- 讲解仅人工启动；
- 不开发移动端后台；
- 首期仅支持中文；
- 声画同步验收误差小于1秒，内部目标为500毫秒；
- 正式视频先下载到LED主机本地，再交给LibVLC播放，不直接依赖Wi-Fi流式播放超大文件。
