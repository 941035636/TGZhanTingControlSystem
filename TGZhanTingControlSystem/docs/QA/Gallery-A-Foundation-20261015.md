# Gallery A — 领域与发布基础验收记录

日期：2026-10-15  
分支：`codex/product-upgrade`  
开发前基线：`da48e46f2adb424105e93b8e3306e430f40f782a`

## 1. 本阶段范围

本阶段只实现图集的领域与发布基础：

- `ExhibitionModule` 增加向后兼容的 `GalleryImages` 字段；
- AdminWeb 增加模块级图片图集管理；
- 支持 JPG/JPEG/PNG 批量上传、预览、改名、删除、按钮排序与拖动排序；
- 图集进入内容草稿、正式内容版本和 LED 素材 Manifest；
- 正式版本、历史版本或草稿仍引用图集图片时，素材删除保护继续生效；
- 发布前验证图片类型、支持格式、文件存在、Size 与 SHA-256；
- Rollback 恢复历史图集顺序及历史资产引用；
- Unity 共享契约增加 `galleryImages` 字段。

本阶段没有实现 Server 图集会话、LED 图片显示控制器或 TouchClient 图集页面。

## 2. 兼容策略

- 旧正式内容和旧草稿没有 `galleryImages` 字段时统一归一化为空数组；
- 原有讲解节点、视频播放、欢迎词和 UDP 7002 协议未修改；
- 图片继续使用现有不可变资产标识、存储、SHA-256与文件大小校验；
- 图集首版只开放 JPG/JPEG、PNG，WebP 会在发布阶段被阻止。

## 3. 自动化结果

| 检查项 | 状态 | 结果 |
| --- | --- | --- |
| Gallery A 专项测试 | PASS | 9/9：旧正式内容与旧草稿、顺序、Manifest、格式、完整性、删除保护、Rollback |
| Server既有回归 | PASS | Deployment Security 10/10；Phase 9A 20/20；9B 23/23；9C 11/11；9D 33/33；9E 6/6 |
| AdminWeb测试 | PASS | 15/15 |
| AdminWeb生产构建 | PASS | TypeScript与Vite构建成功 |
| Server Release构建 | PASS | 0警告、0错误 |
| TouchClient Unity脚本编译 | PASS | Unity 2020.3.35f1c2，Batchmode正常退出 |
| LedPlayer Unity脚本编译 | PASS | Unity 2020.3.35f1c2，Batchmode正常退出 |

## 4. 人工方向确认入口

在管理端“内容中心”的任一模块卡片点击“管理图片图集”，重点确认：

1. 模块图集与“编辑大屏素材”彼此独立；
2. 批量上传入口只接受 JPG/JPEG/PNG；
3. 图片顺序、名称、预览和删除操作符合使用习惯；
4. 模块卡片显示正确的图集图片数量；
5. 发布校验能阻止缺失文件、错误 Size/SHA 或不支持格式。

## 5. 尚未执行

- `NOT RUN`：真实浏览器人工操作和视觉确认，等待用户验证方向；
- `NOT RUN`：Server到LED图集命令，因为属于 Gallery B/C；
- `NOT RUN`：TouchClient图集选择与投放，因为属于 Gallery D；
- `NOT RUN`：三端安装包和现场端到端，因为按约定在图集功能完成后统一执行。
