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

for text in ["Hello world", "这是中文课程表", "数学 Mathematics 123"] {
    let bitmap=CGContext(data:nil,width:500,height:60,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.noneSkipLast.rawValue)!
    bitmap.setFillColor(CGColor(gray:1,alpha:1));bitmap.fill(CGRect(x:0,y:0,width:500,height:60))
    NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(cgContext:bitmap,flipped:false)
    (text as NSString).draw(at:CGPoint(x:5,y:15),withAttributes:[.font:NSFont.systemFont(ofSize:24),.foregroundColor:NSColor.black])
    NSGraphicsContext.restoreGraphicsState()
    let image=bitmap.makeImage()!;let png=NSBitmapImageRep(cgImage:image).representation(using:.png,properties:[:])!
    let done=DispatchSemaphore(value:0);var reply:[String:Any]=[:]
    DispatchQueue.global().async {
        defer{done.signal()}
        reply=(try? dispatch(["operation":"ocr.recognize","png":png.base64EncodedString()])) ?? [:]
    }
    check(done.wait(timeout:.now()+20) == .success,"中英文 OCR 有界完成")
    let boxes=reply["boxes"] as? [[String:Any]] ?? []
    check(boxes.contains{($0["confidence"] as? Float ?? 0)>=0.6},"自动语言识别保留避让置信度门槛："+text)
}
