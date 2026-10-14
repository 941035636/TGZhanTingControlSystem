# AdminWeb 大屏素材编辑器精简记录（2026-10-14）

## 目标

模块内容编辑页只保留大屏展示素材维护，管理员可上传、查看和移除视频或图片；不再向管理员提供讲解文案、讲解音频、TTS Provider、候选语音或人工音频上传入口。

## 实现结果

- 内容编辑入口统一改为“编辑大屏素材”。
- “讲解节点”在管理界面改称“展示节点”，保留原有节点 ID、顺序、故障策略和草稿同步语义。
- 编辑页只显示节点名称、顺序、故障策略以及大屏素材区。
- 素材类型只允许选择“宣传视频”或“展示图片”。
- 新建节点不再生成讲解文案或音频绑定，默认保留视频原声、视频音量为 1。
- 顶栏移除语音合成服务状态；内容中心移除讲解语音发布统计，改为通用内容发布检查。
- 服务端返回的 `video_without_narration_audio` 历史兼容提醒不再显示，因为当前产品不再要求视频节点配置讲解语音。
- 历史 Contracts 字段、Server TTS API 和历史版本数据未删除，保证旧版本读取、回滚和协议兼容；这些能力不再从当前管理页面进入。

## 兼容边界

- 本次没有修改 Server、TouchClient、LedPlayer 或正式播放协议。
- 图片可以上传、保存到草稿并作为节点资产维护。
- 当前正式自动讲解管线仍只把 Video/Animation 视为可播放节点；“模块图集投放到 LED”仍按 `docs/Architecture/Deferred-Image-Gallery-Playback-Plan.md` 封存，不能把本次管理端精简解释为已实现图片投放播放。
- 因此发布前端继续要求每个正式讲解节点至少包含一个可播放视频，图片可作为同时维护的补充素材。

## 验证结果

- `npm test`：PASS，15/15。
- `npm run build`：PASS。
- `dotnet build src/Server/TG.Control.Server/TG.Control.Server.csproj -c Debug`：PASS，0 warning / 0 error；构建过程已重新生成并复制 AdminWeb。
- 浏览器实际登录：PASS。
- 内容中心不显示“讲解文案”“讲解音频”“语音合成服务”：PASS。
- 模块编辑弹窗只显示视频/图片素材类型：PASS。
- 弹窗无 `#narration-text`、`#audio-file`、`#tts-workspace`：PASS。
- 现有视频/图片资产列表读取：PASS。
- 实际上传视频/图片：NOT RUN（避免在用户现有正式草稿中制造测试资产）。
- 发布新版本：NOT RUN（避免改变用户当前正式内容版本）。
