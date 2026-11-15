import AppKit
import Foundation

var checks = 0
func check(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fatalError(message) }
    checks += 1; print("PASS \(checks): \(message)")
}
let _ = NSApplication.shared
let probe = try dispatch(["operation": "material.probe"])
check((probe["state"] as? String) == "Available", "系统材质探测不请求权限")
let window = NSWindow(contentRect: CGRect(x: 100, y: 100, width: 320, height: 200), styleMask: [.borderless], backing: .buffered, defer: false)
window.isReleasedWhenClosed = false
let original = window.contentView!
let button = NSButton(title: "命中测试", target: nil, action: nil)
button.frame = CGRect(x: 20, y: 20, width: 100, height: 40)
original.addSubview(button)
let pointer = UInt64(UInt(bitPattern: Unmanaged.passUnretained(window).toOpaque()))
for style in ["SystemGlass", "SystemMaterial", "Solid"] {
    let id = UUID().uuidString
    let result = try dispatch(["operation": "material.apply", "handle": pointer, "material": style, "id": id])
    check(result["state"] as? String == "Available", "材质可安装：" + style)
    check(window.contentView !== original && original.window === window, "原始内容仍位于同一窗口")
    check(button.window === window && original.hitTest(CGPoint(x: 40, y: 30)) != nil, "材质不截断内容命中")
    _ = try dispatch(["operation": "material.release", "id": id])
    check(window.contentView === original && materialLeases.isEmpty, "释放材质恢复原始内容并清空租约")
}
window.close()
