# 三台电脑独立部署说明

## 1. 部署拓扑

正式现场按三台Windows电脑拆分：

| 电脑 | 安装包 | 包含内容 | 默认安装目录 |
| --- | --- | --- | --- |
| 服务器 | `TG智慧展厅服务端_Setup.exe` | Server、管理端网页、本地TTS Worker | `C:\Program Files\TG Exhibition Server` |
| 中控机 | `TG智慧展厅中控端_Setup.exe` | TouchClient、运行守护程序 | `C:\Program Files\TG Exhibition Touch` |
| 播放机 | `TG智慧展厅播放端_Setup.exe` | LedPlayer、AVPro、运行守护程序 | `C:\Program Files\TG Exhibition Led` |

管理端网页由Server托管，不需要第四套安装包。任意管理电脑使用浏览器访问服务端地址即可。

## 2. 生成三套安装包

构建机需要安装：

- .NET 8 SDK；
- Node.js/npm；
- Unity `2020.3.35f1c2`；
- Inno Setup 6或7；
- 已准备好的MeloTTS离线运行包，或允许构建脚本生成它。

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-SplitProductionPackages.ps1 `
  -Version 1.0.0 `
  -MeloTtsBundleSource "D:\ReleaseInputs\MeloTtsLocal"
```

输出目录为`artifacts\ThreeMachineDeployment`。其中：

- `Installers`包含三套`Setup.exe`和三个现场配置模板；
- `Packages`包含三套未经压缩的可核验运行目录；
- 每套运行目录有独立的`package-manifest.json`，记录文件大小和SHA-256。

如果只验证包结构、不生成`Setup.exe`，可增加`-SkipInstaller`。这不等同于完成正式安装包交付。

## 3. IP与现场配置文件

安装程序不把服务端IP写死在Unity程序中。安装前直接编辑安装程序旁边的JSON：

- 服务端：`server.site-install.json`；
- 中控端：`touch.site-install.json`；
- 播放端：`led.site-install.json`。

服务端示例：

```json
{
  "serverBaseUrl": "https://192.168.1.100:5443",
  "certificateSubject": "CN=TG Exhibition Server",
  "terminalApiKey": ""
}
```

`serverBaseUrl`可以使用固定IP或能被三台电脑解析的主机名，也可以修改端口。正式部署建议给服务器设置固定IP。使用HTTPS且地址为IP时，服务端安装程序会把该IP写入自签名证书的IP地址SAN。

终端示例：

```json
{
  "serverBaseUrl": "https://192.168.1.100:5443",
  "terminalApiKey": "从服务器复制的终端接入密钥",
  "trustedCertificatePath": "server-public.cer"
}
```

配置文件中的密钥只用于安装。安装程序会在中控机和播放机上分别用Windows DPAPI LocalMachine加密后写入运行配置，不能把一台电脑生成的DPAPI密文直接复制到另一台电脑。

## 4. 正确安装顺序

### 4.1 先安装服务端

1. 编辑`server.site-install.json`，填入服务器固定IP和端口。
2. 保持`server.site-install.json`与服务端`Setup.exe`在同一目录。
3. 以管理员身份运行服务端安装程序。
4. 安装完成后，从以下位置取出三个文件：

   - `C:\ProgramData\TG Exhibition\Config\initial-credentials.txt`：管理端初始账号与随机密码；
   - `C:\ProgramData\TG Exhibition\Config\terminal-enrollment.txt`：终端地址与接入密钥；
   - `C:\ProgramData\TG Exhibition\Config\server-public.cer`：服务端HTTPS公钥证书。

这些文件仅管理员可读。接入密钥和初始密码必须通过受控方式转交，不能发到公开群或保存在公共共享盘。

服务端会注册Windows自动服务、服务异常自动恢复策略，以及仅适用于专用网络的入站端口规则。

### 4.2 安装中控端

1. 把`terminal-enrollment.txt`中的`serverBaseUrl`和`terminalApiKey`填入`touch.site-install.json`。
2. 把`server-public.cer`、`touch.site-install.json`和中控端`Setup.exe`放在同一目录。
3. 以管理员身份安装。
4. 安装程序会导入服务端证书、加密终端密钥、设置中控端开机启动。

### 4.3 安装播放端

1. 把相同的服务端地址和接入密钥填入`led.site-install.json`。
2. 把`server-public.cer`、`led.site-install.json`和播放端`Setup.exe`放在同一目录。
3. 以管理员身份安装。
4. 安装程序会导入证书、加密终端密钥、创建本地媒体缓存目录并设置播放端开机启动。

## 5. 后续修改服务器IP

建议先停止中控端和播放端的运行管理程序，再修改：

- 中控机：`C:\ProgramData\TG Exhibition\Config\touch-client.json`中的`serverBaseUrl`；
- 播放机：`C:\ProgramData\TG Exhibition\Config\led-player.json`中的`serverBaseUrl`；
- 两台终端各自的`launcher.json`中的`serverHealthUrl`和`adminUrl`。

修改完成后重新启动运行管理程序。终端密钥没有变化时，不需要重新生成密钥。

如果HTTPS地址中的IP或主机名发生变化，旧证书将不再匹配。此时应编辑三份`*.site-install.json`并依次重新运行服务端、中控端、播放端安装程序，由服务端生成匹配新地址的证书，再把新的`server-public.cer`分发到两台终端。不要关闭证书校验来绕过地址不匹配。

## 6. 验收清单

安装完成后至少检查：

1. 服务器重启后，`TG Exhibition Control Server`服务自动运行。
2. 从中控机浏览器访问`serverBaseUrl`可以打开管理端并登录。
3. 中控端状态显示服务端已连接。
4. 播放端在管理端显示在线、Ready，内容版本同步成功。
5. 三台电脑Windows时间同步正常。
6. 执行一条完整路线，验证开始、暂停、继续、音量、跳过和完成。
7. 三台电脑分别重启后，无需人工重新配置即可恢复连接。

## 7. 运行文件位置

所有可变数据均在`C:\ProgramData\TG Exhibition`，不会写回Program Files：

- 服务端配置：`Config\server.site.json`；
- 中控端配置：`Config\touch-client.json`；
- 播放端配置：`Config\led-player.json`；
- 运行守护配置：`Config\launcher.json`；
- 播放缓存：`Cache\LedPlayer\Content`；
- 日志：`Logs`；
- 服务端内容数据：`Data`。

升级安装默认保留这些现场数据。卸载时只有明确选择删除现场数据，才会移除ProgramData目录。
