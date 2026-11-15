# Mac 签名与公证

工具只处理 SystemTools CIPX 内的 Vosk、插件原生 dylib 和独立 Companion.app；不修改 ClassIsland 源码或安装应用。所有写操作要求一个不存在的新输出包，输入包不覆盖。

- `python3 -B eng/mac_release.py dry-run --package <包>` 只检查来源并显示门禁；`preflight` 遇到缺凭据返回失败。本机仅有 Apple Development，不满足 Developer ID 分发条件。
- `sign-candidate --package <包> --output <新候选.cipx>` 只做 ad-hoc 签名，标记 ad-hoc-not-notarized。`verify --package <候选> --candidate` 核对全部自有 Mac 签名及 Bundle ID。ad-hoc 验证不能证明公证或公开分发资格。
- 配置 Developer ID Application 证书和私钥后，使用 `sign --package <包> --output <新签名.cipx> --identity 'Developer ID Application: …' --team-id <十位团队ID>`。工具逐个签名库，再对助手启用 hardened runtime 和相机/采音 entitlement，不使用 --deep 代替内部签名。
- 在本人本机 Keychain 中配置 notarytool profile 后，运行 `notarize --package <签名包> --output <新公证.cipx> --identity 'Developer ID Application: …' --team-id <团队ID> --keychain-profile <配置名>`。密码、私钥和 API key 不进入仓库、聊天或工具参数。工具使用 ZIP 提交，必须取得 Accepted，再为助手 staple，并验证签名、票据和 Gatekeeper 后重新封包。
- `verify --package <公证包> --team-id <团队ID> --notarized` 要求真实 Developer ID、匹配团队、hardened runtime、stapler 和 Gatekeeper 同时通过。裸 dylib 不支持 stapling，其原始签名字节由同一 notarization 提交覆盖；离线安装行为仍需真实分发环境验证。

当前交付范围是候选包与可执行发行工具；正式签名、公证、证书更新后的授权稳定性和 Gatekeeper 首次分发场景没有真实结果。工具契约测试使用模拟签名输出，不能当作 Apple 接受证据。真实 ad-hoc 测试会记录具体输入包哈希，M6 夹具仅证明工具路径，不冒充 M7 最终候选。

参考：Apple 的 Notarizing macOS software before distribution、Customizing the notarization workflow 及 Hardened Runtime 文档。
