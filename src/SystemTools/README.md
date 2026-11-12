# SystemTools M1 加载预览

仅用于独立测试宿主数据目录。此包沿用正式 SystemTools 身份，不能覆盖日常插件或与原版并装。

只包含「SystemTools M1 诊断」设置页和「记录平台诊断」行动。完整 Windows 行为仍保留在原项目，M2 接入；其他平台功能后续逐项实现。不会加载或写回 Main.json、自动化、组件、会话或认证配置。诊断仅在本插件配置目录写 M1.Diagnostics.json / M1.HostSmoke.json。

支持目标：Windows x64、Linux x64 原生 X11、macOS Apple Silicon ARM64。仅 macOS 已进行本轮实机验收，其余平台请按 docs/validation/m1.md 验证。
