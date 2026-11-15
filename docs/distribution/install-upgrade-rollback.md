# 安装、迁移、回退与卸载

以下 root 必须是包含 PackageType 的 ClassIsland 包根目录，操作对象是插件目录和配置目录。先退出所有宿主及 SystemTools Companion；操作拒绝链接路径和正在运行的宿主。首先备份自己的资料，再对工具显示的绝对路径和包哈希进行核对。

1. `python3 -B eng/release_lifecycle.py plan --root <包根> --package <候选.cipx>` 只读预览身份、提交、包哈希、旧插件和文件指纹。
2. `install --root <包根> --package <候选.cipx>` 创建仅当前用户可访问的恢复目录，校验原包与配置备份后原子替换。存在旧跨平台插件时先迁移，只有明确加入 `--disable-legacy` 才会写旧包 .disabled。待安装队列非空时拒绝，避免宿主启动覆盖候选。
3. 升级前快照位于插件配置的 .upgrade-snapshots/版本；同版本重启不覆盖，损坏快照拒绝继续写入。未知 JSON 字段、密文和附件按原字节备份；无法解密的旧系统密文不会被转换成新凭据。
4. 首次启动、重启和功能验收后退出宿主，执行 `seal --root <包根> --journal <journal.json>` 记录明确验收后的配置状态。再运行 `rollback --root <包根> --journal <journal.json>` 可恢复旧插件、旧插件启停标记及升级前完整配置；回退会丢弃 seal 时已经确认的新配置状态，执行前应保留其副本。seal 之后再有新数据或备份损坏则拒绝覆盖。安装/回退两次目录移动之间的中断可用同一 rollback 恢复，不需删除日志重试。
5. `disable` / `enable` / `uninstall` 都需 --root，只写宿主既有 .disabled / .uninstall 标记。卸载在下次宿主启动执行，保留配置与恢复目录；清理备份是独立的本人决定。

旧跨平台身份迁移沿用 eng/SystemTools.Migrate。建立 JSON 计划，包含 TargetRoot 与 Files，每项包含 Source、Target、Choice（默认 KeepExisting，需显式 ImportLegacy）。先运行 `dotnet run --project eng/SystemTools.Migrate -c Release -- preview <计划.json>`，检查新生成的 .reviewed.json 中选择、冲突、候选和源/目标哈希。再用 `apply <计划.json.reviewed.json>` 应用；源或目标在预览后变化则拒绝。不能用原计划直接 apply，也不能覆盖已有已审阅计划。resume / rollback 接迁移 journal.json，均要求宿主退出。

恢复目录含原配置与密文，不能随公开问题报告上传。回退工具不会自动修改宿主源码、安装应用或操作系统授权记录；版本回退后的相机/麦克风授权稳定性仍取决于正式签名身份验证。
