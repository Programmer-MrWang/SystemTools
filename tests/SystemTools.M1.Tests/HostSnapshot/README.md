# 宿主加载器快照

PluginLoadContext.cs 原样复制自 ClassIsland 提交 3afce2714b6ec50d4bd2a3baac95a71d96ce3b9b 的 ClassIsland/PluginLoadContext.cs，遵循原仓库 GPL-3.0 许可；本仓库同为 GPL-3.0。仅替换外部 PluginService 注册表为测试桩，不修改解析器。实际宿主启动由 eng/m1.py smoke 另行验证。

Windows/Linux 上解析分支需在对应 OS 执行本测试；在 Mac 上通过这些检查不能声称其他 OS 分支通过。宿主版本更新时须先核对快照差异再更新基线。
