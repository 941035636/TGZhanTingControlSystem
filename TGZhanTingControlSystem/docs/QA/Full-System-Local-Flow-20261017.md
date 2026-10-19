# 全系统本机流程测试记录（2026-10-17）

> 分支：`codex/product-upgrade`  
> 基线：`fe516904913f020341003bde2346c1acadd2bfb7`  
> 范围：只测试、只记录；未修改产品源码、配置规则或业务逻辑。  
> 结论：`LOCAL PASS WITH FINDINGS / 现场硬件验收未完成`

## 1. 测试环境与当前数据

| 项目 | 实际状态 |
| --- | --- |
| Server | Development，`http://127.0.0.1:5080` |
| TouchClient / LedPlayer | Unity 2020.3.35f1c2 编辑器运行实例 |
| 正式内容 | 第4版，12个模块，1个展示节点 |
| 正式素材 | 10张图集图片 + 1个487,572,696字节MP4 |
| 素材完整性 | 11/11均有AssetId、SHA-256、Size和MediaType |
| LED视频后端 | AVPro，`MF-MediaEngine-Hardware`，1920×1080@25fps |
| 隔离图集测试 | Server 5081，UDP 17002，3张真实3840×2160 PNG |

## 2. 构建与自动回归

| 测试项 | 状态 | 结果 |
| --- | --- | --- |
| Server Release + AdminWeb生产构建 | PASS | 0警告、0错误 |
| Launcher Release构建 | PASS | 0警告、0错误 |
| AdminWeb自动测试 | PASS | 15/15 |
| Phase 9A | PASS | 20/20 |
| Phase 9B | PASS | 23/23 |
| Phase 9C | PASS | 11/11 |
| Phase 9D | PASS | 33/33，含视频音量跨节点继承 |
| Phase 9E | PASS | 6/6 |
| Gallery A | PASS | 9/9 |
| Gallery B | PASS | 14/14 |
| Deployment Security | PASS | 10/10 |

首次并行运行多个.NET测试时，多个项目同时触发`npm ci`，导致`node_modules`目录争用并出现`EPERM`。改为串行、复用已经成功完成的AdminWeb生产构建后，全部测试通过。该现象属于测试编排冲突，不计为产品功能失败。

## 3. 管理端浏览器实际检查

| 场景 | 状态 | 结果 |
| --- | --- | --- |
| `admin / tg001`登录 | PASS | 成功进入管理平台 |
| 内容中心 | PASS | 12模块正确显示 |
| 大屏素材编辑 | PASS | 仅显示视频/图片编辑，无讲解文案与讲解音频编辑区 |
| 图集管理 | PASS | “一路向新”10张图片、名称、顺序、预览入口均正常读取 |
| 发布与回滚 | PASS（只读） | 第0～4版历史可读取，当前第4版 |
| 终端与运行 | PASS | Touch和LED均在线、就绪、内容第4版 |
| 终端界面设置 | PASS（只读） | 页面可打开并读取全部字段；服务器界面配置版本为v0 |
| 实际发布/回滚按钮 | NOT RUN | 本轮不允许修改正式数据；原子性和回滚由Phase 9D自动测试覆盖 |

## 4. 正式视频讲解链路

使用当前正式第4版“一路向新”MP4和正在运行的Unity LedPlayer执行。

| 场景 | 状态 | 结果 |
| --- | --- | --- |
| 单模块开始讲解 | PASS | 生成1节点Session，LED完成Received/Ready/Playing |
| AVPro实际打开视频 | PASS | 本地缓存MP4由`MF-MediaEngine-Hardware`打开并产生首帧 |
| 暂停 | PASS | LED回报Paused |
| 继续 | PASS | LED恢复Playing |
| 音量调到30% | PASS | Server会话为0.3，LED实时接收 |
| 重试当前节点 | PASS | 重新Prepare/Ready/Play，音量仍保持0.3 |
| 终止 | PASS | 会话清除 |
| 12模块“全部讲解”请求 | PASS（数据受限） | 请求包含12个模块，但当前只有1个节点，因此NodeCount=1 |
| 单活动讲解保护 | PASS | 第二次并发Start返回HTTP 409 |
| 跳过 | PASS | 单节点会话完成并清除 |
| Server重启恢复 | PASS | 原Session恢复，LED重新Prepare/Ready/Playing，随后可正常终止 |
| 最近2000行Unity日志关键异常扫描 | PASS | 未发现401、No MediaReference、AVPro播放错误、D3D/插件异常或NullReference |

## 5. 图集三端实际链路

机器可读证据：`artifacts/gallery-e-runtime-verification.json`。

| 场景 | 状态 | 结果 |
| --- | --- | --- |
| Server + Touch + LED三进程启动 | PASS | 隔离端口运行成功 |
| 首张4K图片 | PASS | Prepare/Ready/Show/Showing完整 |
| 手动下一张 | PASS | 双缓冲切到第二张 |
| 4K短时轮播与暂停 | PASS | 3张图片均轮播，暂停保持；本轮工作集变化约0.6MB |
| Touch重启恢复 | PASS | 自动返回活动图片展示页 |
| LED重启与缓存复用 | PASS | 同一图片重新Prepare/Ready/Show，复用缓存 |
| Server重启恢复 | PASS | 持久化Session恢复，LED重新确认Showing |
| 旧UDP隔离 | PASS | `VoiceAdd`被兼容层接收，不影响Gallery Session |
| 欢迎流程优先级 | PASS（受控） | 先终止Gallery；未伪造欢迎音频 |
| 内容版本/顺序变化 | PASS | 不兼容旧Session被安全丢弃 |

## 6. 路线与就绪门禁

在5082隔离数据目录执行，未触碰正式路线数据。

| 场景 | 状态 | 结果 |
| --- | --- | --- |
| 新建路线 | PASS | 3模块路线成功保存 |
| 覆盖保存与排序 | PASS | 顺序由01/02/03变为03/01/02 |
| 中文路线名称 | PASS | UTF-8往返保持“中文路线一号” |
| 删除路线 | PASS | 删除后列表为空 |
| LED离线门禁 | PASS | `CanStart=false`，提示“LED播放端离线” |
| LED在线但版本不一致/未Ready | PASS | `CanStart=true`，明确提示受限可用并按需下载 |
| Touch端保存/另存/删除按钮人工触控 | NOT RUN | 当前正式数据无路线；Windows UI自动化被高权限Unity窗口拒绝访问，本轮未冒险关闭编辑器 |

## 7. 本轮发现的问题（未修复）

### QA-001：正式内容只有1个可播放节点

- 状态：`FAIL（内容完整度）`
- 严重度：高，影响现场“全部讲解”验收。
- 现象：12个模块均启用，但仅“一路向新”有1个展示节点；其余11个模块为0节点。
- 结果：点击“全部讲解”技术链路正常，但实际只生成1个节点并只播放一个视频。
- 处理建议：现场测试前为需要讲解的模块补齐视频/图片节点并发布新版本；本轮未修改内容。

### QA-002：Server无权读取ProgramData现场配置

- 状态：`FAIL（本机部署配置）`
- 严重度：中。
- 现象：每次Development Server启动均提示无法读取`C:\ProgramData\TG Exhibition\Config\server.site.json`，原因是Access denied。
- 当前影响：Development环境回退到仓库配置后仍可运行；正式安装服务账户是否同样受影响需单独核查。

### QA-003：部分图集操作日志缺少动作和发起人

- 状态：`FAIL（审计展示）`
- 严重度：低到中。
- 现象：管理端运行日志出现“图集操作已接受：。”；图集开始日志的“发起人”为空。
- 当前影响：不影响图片投放，但降低现场问题追溯能力。

### QA-004：Gallery E脚本不能在Windows PowerShell 5直接运行

- 状态：`FAIL（测试工具兼容性）`
- 严重度：低，不影响产品运行。
- 原因一：脚本为UTF-8无BOM，Windows PowerShell 5按系统编码读取中文字符串时会破坏引号。
- 原因二：使用PowerShell 7才支持的`-SkipHttpErrorCheck`。
- 本轮处理：未改文件；按UTF-8在内存中加载，并使用等价的PS5异常状态码处理后，完整链路PASS。

### QA-005：当前没有已发布的自定义终端界面配置

- 状态：`BLOCKED（数据状态待确认）`
- 严重度：中。
- 现象：管理端显示“服务器版本 v0”，字段来自默认配置。
- 当前影响：不能用本轮只读测试证明“修改并发布后重新打开仍能恢复”的实际持久化效果。

## 8. 未执行/现场待验

| 项目 | 状态 | 原因 |
| --- | --- | --- |
| 三台物理电脑断网/恢复 | NOT RUN | 本机多进程不能替代真实交换机、无线AP和网卡中断 |
| 8小时4K图集压力与显存曲线 | NOT RUN | 本轮仅短时3图循环 |
| 物理LED视觉、黑边、淡入淡出、白屏 | NOT RUN | 需要甲方LED大屏人工观察 |
| 实际大屏音响与欢迎音频 | BLOCKED | 当前界面配置未绑定欢迎音频 |
| 180秒空闲自动返回人工计时 | NOT RUN | 当前Unity窗口无法由自动化工具安全控制 |
| 模块02～12正式视频/图片播放 | BLOCKED | 当前正式内容无节点 |
| 旧安卓中控完整播放/暂停/继续/音量现场回归 | NOT RUN | 仅本机验证UDP通道隔离，需要旧安卓设备与现场网络 |
| 本轮重新构建Unity Windows Player | NOT RUN | Touch与LED项目正在Unity编辑器中打开，未强制关闭可能存在未保存状态的编辑器；实际编辑器运行和AVPro播放已验证 |

## 9. 最终结论

当前源码的Server、AdminWeb、正式视频控制、AVPro播放、Session控制与恢复、图集协调/缓存/恢复、路线Repository和门禁规则在本机测试范围内通过。当前不应宣称“12模块全内容现场验收完成”，主要阻碍是正式内容仅1个节点，以及物理三机、LED画面、音响、旧安卓和长时压力仍需现场验证。

本轮唯一新增文件为本QA记录；产品源码未修改，未提交、未推送。
