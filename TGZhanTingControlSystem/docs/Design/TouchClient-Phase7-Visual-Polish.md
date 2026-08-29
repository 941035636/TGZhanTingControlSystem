# TouchClient Phase 7 全局视觉与交互精修

日期：2026-08-29

## 修改前审计

Phase 3～6 已形成深蓝企业科技风基础，但仍有四处产品感断层：TopBar 和状态徽标偏后台管理 Header，侧边导航缺少活动讲解提示；接待首页路线卡与快速接待的 CTA 层级不够统一；路线编辑器在 12 张卡片上重复展示内容警告，视觉噪声较大；讲解执行页主视觉留白偏多，当前主题焦点不够集中。SystemStatusPage 已较成熟，本阶段只统一密度和语义组件。

## Design Tokens

- 字体：Display 40、PageTitle 28、SectionTitle 23、CardTitle 20、Body 18、Secondary 16、Caption 14、ButtonText 17。
- 间距：统一使用 8 / 12 / 16 / 24 / 32 / 48；页面边距 24，卡片间距 16，面板内边距 24。
- 颜色：固定 AppBackground、NavigationBackground、Surface、SurfaceElevated、Border、Primary、PrimaryHover、PrimaryPressed、Success、Warning、Error、TextPrimary、TextSecondary、TextMuted、Disabled 和 DisabledSurface。
- 触控：普通按钮高度 64，主 CTA 72，紧凑按钮 56，导航项高度 68。
- Server Accent 只保留在允许配置的品牌强调区域，不覆盖导航结构色、主按钮层级及 Success / Warning / Error。

## 页面精修

- App Shell：统一背景层级、圆角表面与边框；TopBar 增加克制的本地时间容器；SideNavigation 统一行高、选中指示条、禁用态，并为真实活动讲解显示提示点。
- ReceptionHome：压缩 Hero 和快速接待区域，常用路线调整为稳定的两列卡片布局，统一路线卡 CTA 和状态面板密度；保留深蓝中性科技占位和图片失败回退。
- RouteEditor：保留 12 模块四列编排与路线顺序语义；把未配置数量汇总到区块标题，卡片内只保留短状态；统一选中顺序、上移、下移、移除、保存、另存和删除的视觉权重。
- Playback：当前主题升级为最强视觉焦点，当前节点为次级焦点；明确“主题进度”和“整条路线节点进度”，不推测媒体百分比或剩余时间；暂停态使用 Warning 语义并保持 Stop 物理隔离。
- SystemStatus：保持四张真实状态卡的信息架构，仅统一总体结论、徽标、卡片密度和说明文字。
- ErrorBanner：适配长错误信息并限制最小可读字号，不显示协议与堆栈信息。

## 验证

- Windows Player 1920×1080 六页截图：ReceptionHome、RouteEditor、Playback 正常、Playback 暂停、SystemStatus 正常、SystemStatus 受限。
- 视觉压力：0 / 1 / 多路线、12 模块、超长路线名、超长主题名、超长节点名、封面加载失败、长 ErrorBanner、Server 断开、LED 离线、受限可用、活动 Session 和暂停 Session，均未发现页面级裁切或文字穿出。
- 路线回归：临时组合、顺序、上移、下移、移除、清空、未保存离开确认、保存、覆盖保存、另存、删除二次确认、临时组合开始及全部主题开始均通过；测试路线已清理。
- 播放回归：开始、暂停、继续、重试、跳过、终止确认/取消、页面切走再返回和单活动 Session 保护均通过。
- 恢复回归：暂停中的活动 Session 跨 Server 重启恢复成功；SystemStatus 在真实 Server 断开后显示连接异常，服务恢复后自动恢复真实状态。
- 门禁回归：真实 LED 离线 Readiness 下无法建立新 Session，并向操作员显示原因；真实 LED 版本不一致时展示受限可用。

## 边界

本阶段只修改 TouchClient 表现层和设计 Token。未修改 Server、Contracts、LedPlayer、PlaybackCoordinator、NarrationAudioPlayer、TTS、同步协议、API 路径或任何业务状态机，也未引入新页面和设备控制能力。
