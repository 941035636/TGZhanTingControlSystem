# Phase UI-12B — 深蓝科技首页最终视觉精修测试报告

日期：2026-10-10  
分支：`codex/product-upgrade`  
基线 HEAD：`9e290df95d35dc431735adbff9ef50f166426a73`  
说明：Phase UI-11/12 修改仍处于未提交工作树，本轮未提交、未推送，也未清理任何既有修改。

## 本轮范围

- 保留 `Home_Hall_1920x1080.png`，未更换背景。
- 保留12张摄影卡片、4×3布局、模块数据和所有选择/讲解业务事件。
- 仅调整首页玻璃层透明度、资源导入尺寸、视觉模式刷新和装饰层 Raycast。
- 未修改待机页、欢迎词流程、180秒返回、Server、LedPlayer、MeloTTS、Session或播放协议。

## 底部浅灰带根因与修复

根因不是 Safe Area，也不是 Canvas Scaler：

1. 1920×1080背景被 Unity 默认 NPOT 规则缩放为2048×1024；
2. `Image.preserveAspect = true` 在16:9 Canvas中因此产生上下留白；
3. UI配置刷新后下层背景色可能回到浅色，留白显示成浅灰色带。

修复：

- 背景关闭 NPOT 缩放和 Mipmap，运行时保持真实1920×1080；
- 首页视觉模式即使状态值未改变也重新应用，避免配置刷新覆盖深蓝底色；
- 未添加任何遮挡矩形。

运行日志确认：

`screen=1920x1080 canvas=1920x1080 backgroundSprite=1920x1080 backgroundRect=1920x1080`

1280×720与2560×1440截图底部最后一行均为深蓝场景像素，不再出现浅灰区域。

## 视觉精修

- 将内容、标题、底栏、顶部/侧栏玻璃着色强度分别收敛到独立 Home Token；
- 保留PNG自带的精细双层科技线，不再叠加额外 Outline/Shadow；
- 玻璃面板保持24px九宫格边界，避免边角在长宽拉伸时变形；
- 主内容玻璃透明度降低，穹顶、侧窗和地面空间层次更明显；
- 卡片本体、卡片描边、选中顺序与点击热区未改变；
- 内容框及全部纯装饰 Image 的 Raycast Target 已关闭。

## 真实 Windows Player 截图

- `01-Home-Glass-1920x1080.png`
- `02-Home-Glass-Three-Selected-1920x1080.png`
- `03-Home-Glass-1280x720.png`
- `04-Home-Glass-2560x1440.png`
- `06-Standby-To-Home-Transition-1920x1080.png`
- `07-NonHome-Light-Shell-Restore-1920x1080.png`

2560×1440由Unity Player在1280×720画面上使用2倍高分辨率帧捕获生成；QA显示器物理分辨率为1920×1080，因此没有声称其为物理2560窗口截图。

## 构建与回归

| 项目 | 结果 | 证据/说明 |
|---|---|---|
| TouchClient脚本编译 | PASS | Unity 2020.3.35f1c2 Windows Player构建无C#错误 |
| Windows Player构建 | PASS | `artifacts/phase-ui12b-final-build.log`：Build Successful |
| 1920×1080背景覆盖 | PASS | 背景Sprite、Rect、Canvas均为1920×1080 |
| 1280×720裁切 | PASS | 无底部灰带、卡片无裁切、按钮无重叠 |
| 2560×1440高分辨率渲染 | PASS | 2倍帧捕获，无灰带与布局溢出 |
| 12模块显示 | PASS | 运行日志 `cards=12` |
| 三模块选择 | PASS | 真实截图显示01/02/03选择顺序 |
| 清空选择 | PASS | `QA_UI12B_CLEAR_SELECTION activeBadges=0` |
| 导航离开首页 | PASS | 系统状态页可进入，首页场景背景自动停用 |
| 装饰层Raycast | PASS | 12个检查对象全部 `raycastTarget=False` |
| 待机进入首页 | PASS | 真实截图记录切换完成后的首页 |
| LED离线实际开始讲解 | BLOCKED | 真实LED状态为离线，未绕过门禁 |
| 180秒完整计时等待 | NOT RUN | 本轮未修改该逻辑，仅验证待机/首页切换路径 |

## 已知限制

- HomeDarkV2背景是概念素材裁切放大的临时视觉验证资源，不是原生4K素材；2560×1440及更高分辨率会显得偏软。
- 玻璃PNG自带内外两条细科技线。本轮已去除代码侧重复描边并降低整体光强，但没有篡改原始素材像素。
- LED离线时按钮状态忠实显示真实业务门禁，未为截图强制启用。

