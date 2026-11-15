# NPOI 源码构建

本插件使用固定提交 ab1aa0f7d9aaea06159eeb0311b7b74e752d819a 的 Apache-2.0 源码，重新构建为 SystemTools.NPOI 2.8.1-source.1。没有下载或接受上游 2.8 系列官方二进制包的 OSMF 条款。源码许可证和嵌入的第三方许可证随自建包保留；不能把这一自建包称为官方 NPOI 二进制。

先运行 `python3 -B eng/build_npoi.py`，再执行正常的 dotnet restore/build；`eng/m1.py build` 已自动执行此前置步骤。源码归档地址、提交和 SHA-256 位于 eng/npoi-source.json；归档不符、补丁不再适用、SDK 不符或缓存来源不符都会中止。脚本只更新 System.Security.Cryptography.Xml 到 10.0.15，输出记录构建脚本 SHA-256、SDK 和目标框架。

应用同时固定 Pkcs/Xml 10.0.15。图片解码改用宿主已有 SkiaSharp，并在分配像素前检查尺寸。旧 NPOI 2.7.2 和间接 ImageSharp 已退出新插件依赖图；不得复用含旧 DLL 的输出目录，首次切换请执行 `python3 -B eng/m1.py build --clean`。

`python3 -B eng/audit_dependencies.py` 保存实际解析版本、包内容哈希和实时漏洞查询结果；查询失败或发现漏洞均阻止发行。扫描只是已公开公告的依赖检查，不代表不存在未知漏洞。候选包的许可证、来源和实际文件另由 M7 包审计生成。
