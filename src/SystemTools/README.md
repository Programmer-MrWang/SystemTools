# SystemTools 跨平台开发预览

仅用于独立测试宿主数据目录。此包沿用正式 SystemTools 身份，不能覆盖日常插件或与原版并装。

Windows 已接入原版设置页、主题、行动、触发器、组件、规则、AI 与可选扩展。插件沿用 SystemTools 身份和旧配置键；沉浸式时钟按产品决定移除，原版未注册的 ClickSimulation 不新增入口。缺少扩展时保留配置并提供失败反馈；启用摄像头、认证和语音仍需要对应权限、组件与模型。

Linux x64 原生 X11 与 macOS Apple Silicon ARM64 当前提供平台加载和诊断，尚未接入 Windows 业务。Linux 按用户要求仅完成构建与选择边界检查，未做真机验收。Windows M2 的开发检查与尚待硬件验证项目见 docs/roadmap/2026-11-14-m2-progress.md；此包仍为开发预览。
