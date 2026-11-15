import AppKit
import Foundation

let root = URL(fileURLWithPath: CommandLine.arguments[1])
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
final class FixtureView: NSView {
    var mode = "red"
    override func draw(_ dirtyRect: NSRect) {
        let color: NSColor = mode == "black" ? .black : mode == "white" || mode == "text" ? .white : .red
        color.setFill(); bounds.fill()
        if mode == "text" {
            ("SystemTools 12345" as NSString).draw(at: NSPoint(x: 20, y: 80), withAttributes: [.font: NSFont.systemFont(ofSize: 38), .foregroundColor: NSColor.black])
        }
    }
}
let window = NSWindow(contentRect: CGRect(x: 280, y: 300, width: 500, height: 200), styleMask: [.borderless], backing: .buffered, defer: false)
window.isReleasedWhenClosed = false
let view = FixtureView(frame: window.contentView!.bounds)
view.autoresizingMask = [.width, .height]
window.contentView = view
window.orderFrontRegardless()
let frame = window.frame
let reply: [String: Any] = ["x": frame.minX, "y": (NSScreen.screens.first?.frame.maxY ?? 0) - frame.maxY, "width": frame.width, "height": frame.height]
try JSONSerialization.data(withJSONObject: reply).write(to: root.appendingPathComponent("fixture.json"))
let timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { _ in
    if let mode = try? String(contentsOf: root.appendingPathComponent("fixture-mode"), encoding: .utf8), mode != view.mode {
        if mode == "close" { app.terminate(nil); return }
        view.mode = mode; view.needsDisplay = true
    }
}
app.run()
