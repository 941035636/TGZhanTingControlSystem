# 当前讲解视频音量跨节点继承修复记录

日期：2026-10-14
分支：`codex/product-upgrade`

## 问题

讲解员在当前讲解中降低“大屏原声音量”后，TouchClient与Server仍显示新的音量值，
但切换到下一个纯视频节点时，LedPlayer实际输出会恢复为100%。

## 根因

- Server已经把操作员音量保存为当前Session的`VideoVolumeOverride`，并会持久化和恢复。
- TouchClient显示的是Server返回的Session有效音量，因此UI保持正确。
- LedPlayer的`ApplyMix`在没有讲解音频时无条件调用`SetVolume(1)`，忽略后续节点命令中的
  `videoVolume`，造成UI状态与实际音量不一致。

## 修复规则

1. 纯视频节点且操作员没有调整音量：Server下发`KeepOriginal + 1.0`，保持原有100%默认行为。
2. 当前Session一旦调整音量：Server在当前节点和后续节点下发`Duck + VideoVolumeOverride`。
3. LedPlayer统一按照命令的`AudioMixPolicy`应用实际视频音量，不再因缺少讲解音频强制恢复100%。
4. 覆盖值继续写入Session快照，因此跳过、重试、切换节点和Server恢复Session后仍然继承。
5. 新Session不继承上一Session的覆盖值，重新采用内容和混音默认规则。

## 修改文件

- `src/Server/TG.Control.Server/PlaybackCoordinator.cs`
- `src/LedPlayer/Assets/Scripts/LedPlaybackController.cs`
- `tests/TG.Control.Phase9D.Tests/Program.cs`

## 验证结果

| 项目 | 状态 | 结果 |
| --- | --- | --- |
| 纯视频初始命令 | PASS | 无覆盖值时为`KeepOriginal + 1.0` |
| 当前节点实时调音 | PASS | `SetVideoVolume`下发并保存0.2 |
| 下一视频节点继承 | PASS | 下一Prepare命令为`Duck + 0.2` |
| Session状态继承 | PASS | 下一节点状态仍返回0.2 |
| Phase 9D自动测试 | PASS | 33/33 |
| Server Release构建 | PASS | 0警告、0错误；AdminWeb生产构建通过 |
| LedPlayer Unity脚本编译 | PASS | Unity 2020.3.35f1c2临时验证工程无C#编译错误 |
| LedPlayer Windows Player构建 | PASS | 临时验证构建成功 |
| 真实多视频人工听感 | NOT RUN | 需在中控与LED编辑器重新进入Play后播放至少两个视频确认 |

## 人工回归

1. 选择至少两个含视频的模块开始讲解。
2. 第一个视频播放时把“大屏原声音量”调到20%。
3. 等待自动进入下一视频，确认UI仍为20%，LED实际输出也保持20%。
4. 验证跳过到下一视频、重试当前视频后仍保持20%。
5. 终止讲解并启动新Session，确认音量恢复新Session默认值，而不是继承上一Session。
