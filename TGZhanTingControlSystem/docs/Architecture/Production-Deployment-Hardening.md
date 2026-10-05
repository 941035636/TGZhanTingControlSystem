# 正式部署安全与打包基线

日期：2026-09-27

本文是当前生产部署依据。Phase 9F/9G文档中的HTTP 5080、明文密码和LibVLC内容属于当时审计记录，不再作为当前配置模板。

## 网络与证书

- 正式安装默认监听`https://0.0.0.0:5443`，本机访问地址为`https://localhost:5443`；
- 安装器在`LocalMachine\My`创建或复用五年有效的RSA 3072位证书，并将公钥加入`LocalMachine\Root`；
- Windows防火墙只为Server程序在Private配置文件开放配置端口；
- AdminWeb由Server同源托管，Server不再启用任意来源CORS；
- MeloTTS Worker仍只监听`127.0.0.1:5091`，不得开放到局域网。

现场若使用多机部署或正式域名，应由客户证书替换本机证书，并把终端`serverBaseUrl`改为证书SAN覆盖的主机名。DPAPI LocalMachine密文只能在加密它的Windows设备上解密，因此多机部署应在每台终端本地生成并保护终端配置，不能直接复制密文。

## 账号与角色

生产配置使用`Admin.Accounts[]`，每个账号包含`Username`、`PasswordHash`和`Roles`。支持角色：

| 角色 | 权限 |
|---|---|
| `Administrator` | 所有管理、编辑、发布和运行权限 |
| `Publisher` | 发布终端界面、发布内容、版本回滚 |
| `Editor` | 草稿、素材、路线和TTS生产操作 |
| `Operator` | 启动、暂停、继续、跳过、重试和停止讲解 |
| `Viewer` | 已认证的只读状态与历史查询 |

密码格式为`$pbkdf2-sha256$<iterations>$<salt>$<hash>`，当前安装器使用210,000次迭代、16字节随机盐和32字节SHA-256派生值。生产配置将`AllowLegacyPlaintextPassword`设为`false`。仓库`appsettings.json`保留的明文账号只服务本地开发。

账号当前由受ACL保护的`server.site.json`维护，尚无Web账号维护界面。管理员可运行
`Tools\New-AdminAccount.ps1 -Username <name> -Roles Editor,Publisher`，按隐藏提示输入两次密码，将输出的JSON对象加入
`Admin.Accounts`后重启Server。新增账号必须使用独立密码，不得复用初始管理员密码。

## 敏感配置

- 管理员明文密码只在首次安装时写入`initial-credentials.txt`，该文件仅SYSTEM和Administrators可读；Server配置只保存散列；
- 终端API密钥随机生成后，以Windows DPAPI LocalMachine保护，Server、TouchClient和LedPlayer在内存中解密使用；
- `server.site.json`只允许SYSTEM和Administrators读取；终端配置允许现场运行用户读取，但磁盘上不含明文终端密钥；
- 从旧版本升级时，安装脚本先备份配置，再把旧管理员明文密码迁移为散列、把终端密钥迁移为DPAPI密文，并切换HTTPS配置。
- `appsettings.Development.json`只用于显式Development环境，发布时不会进入生产包；包验证同时拒绝开发默认密钥文本。

## 播放器和包验收

LedPlayer当前正式后端是AVPro Video 3.2。生产包和完整性检查必须包含：

- `LedPlayer_Data\Plugins\x86_64\AVProVideo.dll`；
- `LedPlayer_Data\Plugins\x86_64\AVProVideoWinRT.dll`；
- `LedPlayer_Data\Plugins\x86_64\Audio360.dll`。

打包脚本同时接受仓库`artifacts`目录下的相对或绝对`OutputRoot`，仍拒绝清理该目录以外的路径。播放器回归脚本根据实际日志确认活动后端已输出视频帧，不再硬编码LibVLC。

## 尚未解除的外部交付门禁

- 干净Windows机器的安装、升级、重启、恢复和卸载验收；
- 安装程序与二进制代码签名；
- AVPro Video和Inno Setup商业授权确认；
- >2GB客户视频、现场GPU/大屏、8小时长稳和实测声画同步验收。

这些项目仍必须标为`BLOCKED`或`NOT RUN`，不能因源码修复而写成`PASS`。

## 本轮验证记录

| 检查 | 状态 | 结果 |
|---|---|---|
| Server/AdminWeb Release构建 | PASS | 0警告、0错误 |
| TouchClient Windows x64构建 | PASS | Unity 2020.3.35f1c2 `Build Successful` |
| LedPlayer Windows x64构建 | PASS | Unity 2020.3.35f1c2 `Build Successful`，三个AVPro原生DLL齐全 |
| AVPro实际Player控制回归 | PASS | 非黑屏、Pause、Resume、Skip、Retry、多节点完成、无D3D异常、活动后端帧确认，共10项通过 |
| Phase 9A—9E业务回归 | PASS | 20+23+11+25+6，共85项通过 |
| 部署安全回归 | PASS | 密码散列、安装器格式兼容、错误密码、明文禁用、多账号角色与开发兼容，共10项通过 |
| 生产包绝对输出路径 | PASS | 在仓库`artifacts`下使用绝对`OutputRoot`成功生成 |
| 生产包完整性 | PASS | 18,759个文件、2,837,628,230字节全部通过大小与SHA-256校验；开发配置和默认开发密钥未进入包 |
| Inno Setup生成 | NOT RUN | 当前机器未安装Inno Setup编译器；本轮使用`-SkipInstaller`验证Package层 |
| HTTPS安装/升级实机 | NOT RUN | 安装脚本已实现并通过语法检查，尚未在干净管理员环境执行证书、服务和防火墙变更 |
