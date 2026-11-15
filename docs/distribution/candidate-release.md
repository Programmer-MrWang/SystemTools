# M7 候选版与发行检查

目标版本 3.0.0.0，渠道 rc，正式身份 SystemTools，目标宿主 Avalonia/Misha API 2.2。开发维护分支/基线继续保留。没有创建公开 Release、市场条目或正式版本标签。

构建顺序为 `python3 -B eng/m1.py build --clean`、`python3 -B eng/m7.py check`、`python3 -B eng/m7.py stage`。三端 CI 各自产生原生阶段、实时漏洞查询和包许可证/来源资料；Mac 阶段对捆绑库及助手做 ad-hoc 签名。公共程序集、源码提交、指纹、SDK、能力表和发行策略必须一致，否则严格合包失败。

`aggregate --input <win阶段> <linux阶段> <mac阶段> --output <新包.cipx>` 仅接 M7 原生阶段，随后检查身份/版本/API、依赖审计绑定、许可证文件哈希、功能台账 82 项、重复/宿主程序集隔离，以及实际 PE x64、ELF x64 和 Mach-O ARM64。依赖表含完整解析图，包含宿主所有的引用，不代表将它们全部打包。

`audit --package <候选>` 可重新审计；`gate --package <候选> --lifecycle <验收.json> --channel rc` 生成机器报告。stable 还要求本包三端生命周期实测、Developer ID、公证和本人明确发布批准；证据包哈希不符即拒绝。`--publication-approved` 仅用于本人已明确批准后的门禁核对，不执行发布。任何正式签名/公证产生的新包哈希都需重新绑定验收证据。

本轮允许交付候选包与发行工具。Windows/Linux 实体设备、本人 Windows Hello、中文/外部 AI、Mac 多屏/硬件亮度和正式签名升级仍按对应清单追踪；不能从原生 CI 推断这些成功。安装、配置迁移、回退与卸载见 install-upgrade-rollback.md，Mac 凭据和公证见 mac-signing.md。
