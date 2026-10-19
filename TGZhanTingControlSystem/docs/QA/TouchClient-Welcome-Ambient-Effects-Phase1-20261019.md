# TouchClient 欢迎页面动态科技特效第一阶段验证记录

> 日期：2026-10-19  
> 分支：`codex/product-upgrade`  
> 基线：`5ad3a7a7b37ad985a52dbafa5c507339ff760f7e`  
> 状态：`LOCAL PASS / 等待人工视觉确认`  
> 本轮未提交、未推送。

## 1. 当前架构事实

- Unity：`2020.3.35f1c2`。
- 渲染管线：Built-in Render Pipeline，未配置URP/HDRP RenderPipelineAsset。
- TouchClient运行时Canvas：`ScreenSpaceOverlay`，`sortingOrder=100`。
- CanvasScaler：`ScaleWithScreenSize`，设计基准`1920×1080`，宽高各占50%。
- 欢迎页入口：`WelcomeExperiencePage`，由`TouchOperatorUi.BuildUi`创建。
- 背景：继续使用`Resources/Touch/standby-clean-background.png`，1920×1080；未替换视觉设计。

普通世界空间`ParticleSystemRenderer`不能可靠插入ScreenSpaceOverlay Canvas的背景和按钮之间。第一阶段因此使用单一的预分配UGUI粒子Graphic，配合轻量UI Shader与UGUI光晕图片。该方案不改变Canvas模式，不建立Camera/RenderTexture，也不引入第三方插件。

## 2. 实现内容

### 地球周围粒子

- 固定24个柔光粒子；不在运行期间扩容或创建新粒子。
- 围绕地球外缘使用多组椭圆半径缓慢运动，少量粒子反向运动。
- 可独立开关，并可调速度、大小、透明度。

### 底座光环呼吸

- 复用现有运行时柔光与光环Sprite，不修改背景图。
- 默认4秒一个平滑呼吸循环。
- 两个固定扩散波纹错相循环。
- 可独立开关，并可调周期和透明度。

### 星空微闪

- 固定26个星点，限定在画面上方星空区域。
- 使用确定性随机布局及不同相位、速度和尺寸，缓慢改变亮度。
- 可独立开关，并可调速度、大小、透明度。

### 公共约束

- 总强度参数：`OverallIntensity`。
- 所有装饰Graphic的`raycastTarget=false`。
- 特效层在背景之后、文字与按钮之前。
- Shader Material只创建一次，并在组件销毁时释放。
- `Update`中不创建Texture、Material、GameObject或粒子集合。

## 3. 构建与运行验证

| 项目 | 状态 | 结果 |
| --- | --- | --- |
| Unity脚本编译 | PASS | 无C#编译错误 |
| Shader导入/编译 | PASS | 无Shader错误 |
| Windows x64 Player构建 | PASS | `BuildPlayerGenerator: Succeeded` |
| 1920×1080 Windows实际运行 | PASS | Direct3D 11，Player正常启动并自动退出测试探针 |
| 特效组件创建 | PASS | `effect=True` |
| 粒子数量稳定 | PASS | 固定50个UI粒子；第2秒到第4秒无增生 |
| 子对象数量稳定 | PASS | `children=3->3` |
| 装饰触摸拦截 | PASS | `raycastTargets=0` |
| 短时Gen0 GC | PASS | 两秒观测窗口`gen0Collections=0` |
| 短时托管内存 | PASS（短测） | 两秒变化32768字节；不能替代长时Profiler测试 |
| 欢迎文字/按钮层级 | PASS | 真实截图中文字和中央按钮正常显示、未被遮挡 |
| 原按钮点击业务 | NOT RUN | 本轮不触发欢迎音频/Server业务；按钮代码和事件未修改 |
| 长时GPU/GC Profiler | NOT RUN | 等待人工视觉确认后再决定是否做长稳测试 |

运行日志：

- `Builds/WelcomeAmbientPhase1/unity-final-build.log`
- `Builds/WelcomeAmbientPhase1/player-instrumented.log`

最终无测试探针Windows Player：

- `Builds/WelcomeAmbientPhase1/TG.Control.TouchClient.exe`

## 4. 真实运行截图

- 第2秒：`Builds/WelcomeAmbientPhase1/Screenshots/welcome-effects-t2.png`
- 第4秒：`Builds/WelcomeAmbientPhase1/Screenshots/welcome-effects-t4.png`

两帧区域变化统计（排除右上角时钟区域）：

- 上方星空：0.009%像素发生可见变化，保持克制微闪。
- 地球及周边：3.225%像素发生可见变化。
- 底座及地面：10.310%像素发生可见变化，包含呼吸与扩散波纹。

## 5. 边界与已知项

- 未实现第二阶段地球两侧能量光带流动。
- 未引入3D地球、RenderTexture、后处理或第三方特效插件。
- 未修改Server、LedPlayer、Session、180秒空闲返回和欢迎音频业务。
- Windows运行时读取当前现场HTTPS配置出现既有的本机证书校验错误；不影响欢迎页特效渲染，本轮未改网络配置。
- 既有未提交的`Unity_4K_Standby_Layers`素材保持原样，本轮未引用、未修改。
- 截图探针只用于临时验证副本，已经移除；最终Player不包含截图辅助代码。

当前结论为第一阶段本机实现与运行验证通过，等待人工视觉确认，不进入第二阶段流光效果。

## 6. 可见度增强（仍属第一阶段）

- 根据人工视觉反馈，原始粒子在明亮的展厅背景上过于克制。本次不修改欢迎流程、业务逻辑或背景素材。
- 地球周边粒子从固定24个调整为固定34个，采用柔光外晕、亮点核心和短切向尾迹，轨迹运动更容易被观察到。
- 星空微闪从固定26个调整为固定42个；仅高亮峰值星点会短暂呈现小型十字闪点，其余星点仍保持低调。
- 保留原四秒底座呼吸与两圈扩散波纹，仅提高可见度。
- 最终Windows Player构建：PASS（`BuildPlayerGenerator: Succeeded`）。
- 最终运行截图：
  - `Builds/WelcomeAmbientPhase1Visible/Screenshots/welcome-effects-visible-final-t6.png`
  - `Builds/WelcomeAmbientPhase1Visible/Screenshots/welcome-effects-visible-final-t8.png`
- 第二阶段的地球两侧能量光带流动仍未实现。
