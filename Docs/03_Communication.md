# 通信协议设计

## 1. 通信通道

- Unity 客户端与服务器：SignalR/WebSocket 长连接。
- 管理后台与服务器：HTTPS API + SignalR 实时状态。
- 媒体发布：HTTPS 分块下载，支持断点续传、文件大小及 SHA-256 校验。
- 设备层：串口、TCP 或后续设备协议，由驱动内部处理。

## 2. 通用消息信封

```json
{
  "protocolVersion": "1.0",
  "messageId": "01J...",
  "correlationId": "01J...",
  "type": "StartSegment",
  "source": "control-server",
  "target": "display-main",
  "sentAtUtc": "2026-08-20T02:00:00.000Z",
  "executeAtUtc": "2026-08-20T02:00:02.000Z",
  "sessionId": "01J...",
  "sequence": 128,
  "payload": {}
}
```

要求：

- `messageId` 全局唯一，用于去重。
- `correlationId` 关联请求、确认和结果。
- `sessionId` 标识一次讲解运行。
- `sequence` 用于检测乱序和缺失。
- `executeAtUtc` 仅用于需要同步执行的命令。
- 客户端必须拒绝不兼容的协议版本并上报原因。

## 3. 核心消息

### 连接与健康

- `RegisterClient` / `RegisterAccepted`
- `Heartbeat` / `HeartbeatAck`
- `TimeSyncRequest` / `TimeSyncResponse`
- `ClientStatusReport`
- `FaultReport`

### 内容与发布

- `ContentManifestChanged`
- `RequestContentManifest`
- `ContentDownloadProgress`
- `ContentReady`
- `ContentValidationFailed`

### 讲解控制

- `CreateTourSession`
- `PrepareSegment` / `Prepared`
- `StartSegment`
- `PauseSession`
- `ResumeSession`
- `SeekSession`
- `SkipToModule`
- `StopSession`
- `SessionStateChanged`
- `PlaybackPositionReport`

### 设备控制

- `ExecuteDeviceAction`
- `DeviceActionAccepted`
- `DeviceActionCompleted`
- `DeviceStateChanged`
- `DeviceFaulted`

## 4. 确认、超时与重试

- 收到命令后先返回“已接收”，执行结束后返回“成功或失败”。
- 客户端按 `messageId` 去重，重复收到时返回第一次执行结果。
- 查询、读取状态等幂等命令可自动重试。
- 继电器脉冲等可能产生副作用的命令，必须由驱动判断是否允许重试。
- 超时、重试次数和退避时间按设备或命令模板配置。

## 5. 同步协议

1. 客户端注册后进行多次时间采样，估算与服务器的时钟偏移。
2. 服务器发送 `PrepareSegment`，大屏完成预加载后回复 `Prepared`。
3. 服务器选择未来的统一执行时间并发送 `StartSegment`。
4. 大屏按本地校正后的时间执行，并周期上报媒体位置。
5. 漂移小于阈值时只记录；超过校正阈值时微调或 Seek；超过故障阈值时暂停并重新同步。

具体阈值在现场性能测试后确定，不在第一版协议中写死。

## 6. 版本兼容

- 协议采用主版本和次版本。
- 新增可选字段不提升主版本。
- 删除字段、改变语义或改变状态机时提升主版本。
- 服务器维护允许连接的客户端最低版本。

