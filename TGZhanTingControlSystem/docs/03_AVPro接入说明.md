# LED播放器接入与迁移说明

## 播放基线

- LED输出：1920×1080；
- Unity：2020.3.35f1c2；
- Windows 64位构建；
- 当前播放器：LibVLC/UniversalMediaPlayer；
- 大文件先下载到NTFS本地磁盘；
- 播放器接收本地绝对路径或`file:///`本地URL。

## AVPro验证结论

项目曾从既有工程提取AVPro Video 1.8.9，并完成Unity 2020.3脚本兼容处理。实际播放时，
Media Foundation和DirectShow路径均能打开、解码视频，但向Unity提交纹理时持续出现
`Unsupported D3D format 0x58`。关闭硬件解码后问题仍存在，因此确认根因是旧版AVPro
原生纹理接口与当前Unity 2020/DX11组合不兼容，而不是URL、缓存、视频大小或编码问题。

## 当前LibVLC实现

- `LedRuntimeBootstrap`运行时创建LibVLC播放器和1920×1080全屏`RawImage`；
- `UniversalMediaPlaybackAdapter`实现既有`IMediaPlaybackAdapter`，上层协议无需修改；
- 支持预加载、播放、暂停、继续、停止、Seek和播放结束检测；
- Windows x64原生库和VLC解码模块随工程打包，目标电脑无需单独安装VLC；
- 视频帧使用Unity支持的BGRA32纹理输出，绕开AVPro的D3D `0x58`问题；
- AVPro资源暂时保留为历史回退参考，但运行时不再实例化AVPro播放器。

### 2026-09-04 回归修复

`dcc40ba` 曾将默认接入改回 AVPro 软件解码，重新引入 `Unsupported D3D format 0x58`。
本轮恢复上述已有 LibVLC 接入，并为其补齐现有首帧诊断接口；缓存、讲解音频和控制协议不变。
实际 Windows 构建完成两轮播放控制回归，D3D 错误为零。旧版在本机仍可显示工程测试视频，
因此没有把报错复现扩大为“现场持续黑屏已在本机完全复现”。详见
[LED D3D 专项回归记录](QA/LED-D3D-Playback-Regression.md)。

## 视频规范建议

- MP4容器；
- H.264视频；
- AAC音频；
- 1920×1080；
- 固定25或30fps；
- 控制码率并保留关键帧，避免超长GOP导致跳转缓慢。

## 当前验证结果

- Unity 2020.3.35f1c2已完成资源导入和程序集重载；
- `Assembly-CSharp.dll`及编辑器程序集编译成功；
- LibVLC播放器、适配层和Windows x64原生库已导入；
- `Assembly-CSharp.dll`及编辑器程序集编译成功；
- 最新编译后无C#错误、DLL缺失、入口缺失或新增的`Unsupported D3D format 0x58`；
- 尚需使用现场GPU和客户实际大于2GB的视频做运行、跳转、声画同步及长时间稳定性测试。
