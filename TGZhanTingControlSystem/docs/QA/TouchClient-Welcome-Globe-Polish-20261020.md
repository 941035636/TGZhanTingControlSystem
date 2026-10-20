# TouchClient欢迎页地球特效精简验证（2026-10-20）

## 变更范围

- 当前大型椭圆轨道不是`WelcomeAmbientEffects`运行时画出的完整线条，而是已烘焙在旧版`standby-clean-background.png`中。
- 欢迎页改为分层显示：
  - `01_Exhibition_Clean_Background_3840x2160.png`：无地球、无大型轨道的展厅背景；
  - `02_Holographic_Globe_Transparent_3840x2160.png`：透明地球与底座层；
  - `05_Orbital_Light_Rings_Transparent_3840x2160.png`：不加载、不创建运行时对象。
- 保留地球主体、展厅背景、标题、按钮和底座结构。
- 地球微粒由34个降为16个，移除切向尾迹与椭圆环绕运动，改为小范围缓慢漂浮。
- 星空微闪由42个降为24个，移除十字高亮，降低尺寸、速度、亮度和总体强度。
- 删除两圈大型底座扩散波纹，只保留约5秒周期的低亮度呼吸光。
- 总特效、地球微粒、底座呼吸、星空微闪继续具有独立开关。
- 4K背景与地球素材导入上限调整为4096，关闭UI纹理MipMap；透明地球开启Alpha透明边缘处理。

## 验证结果

| 项目 | 状态 | 结果 |
| --- | --- | --- |
| Unity 2020.3脚本与Shader编译 | PASS | 无C#或Shader编译错误。 |
| Windows x64 Player构建 | PASS | `BuildPlayerGenerator: Succeeded`。 |
| 1920×1080真实Player运行 | PASS | 日志：`framebuffer=1920x1080, desktop=1920x1080`。 |
| 大型椭圆轨道移除 | PASS | 最终运行截图中无横穿地球两侧的多层椭圆轨道。 |
| 地球、背景、标题、按钮保留 | PASS | 真实运行截图确认。 |
| 装饰层触摸拦截 | PASS（代码边界） | 地球与全部特效`raycastTarget=false`，未新增可交互装饰对象。 |
| 3840×2160视觉检查 | PASS（受控超采样） | Unity运行时2×超采样截图为3840×2160；不是4K实体显示器验收。 |
| 3840×2160实体屏幕帧缓冲与触控 | NOT RUN | 当前开发机主显示器为1920×1080，需要甲方4K触控设备。 |
| 欢迎按钮、导航、欢迎音频实际业务回归 | NOT RUN | 本轮未触发Server欢迎流程；相关代码未修改。 |
| 180秒空闲返回 | NOT RUN | 相关状态机未修改，本轮未等待180秒。 |

## 截图与日志

- 1920×1080真实Player截图：`Builds/WelcomeGlobePolish/Screenshots/welcome-globe-1920x1080.png`
- 3840×2160受控超采样截图：`Builds/WelcomeGlobePolish/Screenshots/welcome-globe-3840x2160-supersampled.png`
- Unity构建日志：`Builds/WelcomeGlobePolish/unity-build.log`
- Windows Player日志：`Builds/WelcomeGlobePolish/Player.log`

Player日志中仍出现当前已安装现场配置对应的既有SSL证书错误。本轮不修改Server、网络配置或终端认证；该错误不影响欢迎页本地视觉渲染。

当前状态：等待人工视觉确认，未提交、未推送。
