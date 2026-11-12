> 历史参考：本记录描述独立跨平台仓库在上一轮的修复结果，不代表当前原版分支已应用这些代码。

# 跨平台源码审查与修复结果 — 2026-11-13

范围：同级 ClassIsland 宿主、SystemTools Windows 原版和本插件；Windows/Linux/macOS，不考虑 Android。

已按用户决策处理全部 11 个审查条目：9 项修复、1 项删除对应功能、1 项保持原行为。另修复 SDK 固定版本格式、宿主 UI 依赖版本差异和帮助文档未输出问题。

| 原编号 | 结果 | 实现与复查 |
| --- | --- | --- |
| 1 目录路径误删 | 已修复 | 规范化路径和解析父路径别名；拒绝根目录、同路径及相互包含；先准备完整副本再替换目标，失败前保留已有数据 |
| 2 同装菜单冲突 | 已修复 | 独立根菜单「SystemTools 跨平台行动」，与宿主按名称唯一的集合契约一致 |
| 3 用户指定的外部网页行动 | 已删除 | 实现、设置类型、设置控件、注册、菜单、关于页链接和文档均已清除 |
| 4 Windows 退出取消全局计划 | 按用户要求保留 | 普通退出仍无条件同步执行 shutdown /a，不增加计划所有权判断 |
| 5 高级倒计时与实际计划分离 | 已修复 | 普通与高级行动共享计划中心，创建、替换、取消、到期统一更新；界面从同一快照读取期限和进度 |
| 6 授权等待超时 | 已修复 | 命令异步等待，Unix 授权最多两分钟；主动终止的超时返回失败；宿主中断会取消等待并终止进程 |
| 7 同组快照覆盖 | 已修复 | 按目标和具体行动项记录持有关系；最后一个持有者恢复基线；同时处理交错行动组及非后进先出恢复 |
| 8 工作流索引漂移 | 已修复 | 保存和恢复按目标 GUID；旧配置按唯一名称迁移；目标删除或名称不唯一时拒绝猜测，设置页使用相同解析逻辑 |
| 9 等待音频无法中断 | 已修复 | 等待模式传递 InterruptCancellationToken；非等待模式保持独立播放 |
| 10 macOS 置底无实现 | 已降级 | 明确显示「普通层级」，说明只撤销置顶；不调用未实现的置底分支，Windows/Linux 保持置底 |
| 11 链接环与隐式展开 | 已修复 | 写入前检查源树及待替换目标树，拒绝符号链接、目录联接和 reparse point，递归阶段再次检查 |

关键实现：[Helpers/DirectoryTransfer.cs](</Library/root directory/My github project/SystemTools-Cross-platform/src/SystemTools.CrossPlatform/Helpers/DirectoryTransfer.cs>)、[Helpers/ActionRestoreStore.cs](</Library/root directory/My github project/SystemTools-Cross-platform/src/SystemTools.CrossPlatform/Helpers/ActionRestoreStore.cs>)、[Helpers/WorkflowTargetResolver.cs](</Library/root directory/My github project/SystemTools-Cross-platform/src/SystemTools.CrossPlatform/Helpers/WorkflowTargetResolver.cs>)、[Actions/ShutdownPlanCenter.cs](</Library/root directory/My github project/SystemTools-Cross-platform/src/SystemTools.CrossPlatform/Actions/ShutdownPlanCenter.cs>)、[Actions/PowerCommandRunner.cs](</Library/root directory/My github project/SystemTools-Cross-platform/src/SystemTools.CrossPlatform/Actions/PowerCommandRunner.cs>)、[Services/FloatingWindowLayerSupport.cs](</Library/root directory/My github project/SystemTools-Cross-platform/src/SystemTools.CrossPlatform/Services/FloatingWindowLayerSupport.cs>)。

验证：

- 回归程序直接编译上述核心实现，48 项检查通过。包括目录误删、嵌套目标、取消保护、链接环及路径别名、计划取消和替换、到期单次消费、快照分离和交错恢复、GUID/旧配置定位、macOS 层级文案、超过原三秒限制的命令等待、超时与主动中断。
- 整个解决方案 Release 构建通过：0 错误，26 个既有可空引用警告；没有把这些警告描述为全部修复。
- 对当前 ClassIsland 宿主 SDK/Core 源码单独构建核对，使用与宿主一致的 Avalonia 12.1.4；0 错误，29 个警告（包括宿主依赖警告）。
- 另增加真实 SDK/插件行动的 6 项宿主契约检查：菜单同装、transient 实例恢复、同组重复行动恢复及音频中断令牌，均使用服务代理，不播放实际音频。
- 三个帮助文件均存在于 Release 输出目录；废弃网页功能的类型、名称和链接在源码/文档中搜索无残留。
- 本次修复期间关闭 CreateCipx，原有未提交的调试配置、cipx 包及校验文件与开始修复时字节一致；Plugin.cs 原有注释整理仍留在工作区，未混入提交。

验证边界：实际执行环境为 macOS ARM64。没有运行真实关机、重启、睡眠或锁屏，没有播放真实音频，也未在 Windows/Linux 启动完整宿主桌面；音频中断和平台 UI 接入已按宿主接口核对并编译，仍需在目标桌面作运行验收。

执行命令：

```sh
dotnet run --project tests/SystemTools.CrossPlatform.RegressionTests
dotnet run --project tests/SystemTools.CrossPlatform.HostContractTests -p:CreateCipx=false
dotnet build SystemTools-Cross-platform.slnx -c Release -p:CreateCipx=false
```

提交按中文约定式拆分，可从 Git 历史独立回退；分批计划见同目录 2026-11-13-fix-plan.md。
