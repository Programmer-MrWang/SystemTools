import AppKit

final class FixtureDelegate: NSObject, NSApplicationDelegate {
    let state: URL
    let command: URL
    let window = NSWindow(contentRect: NSRect(x: 180, y: 180, width: 600, height: 320),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
    var timer: Timer?
    var baselineMinimized: Bool?
    init(_ path: String) { state = URL(fileURLWithPath: path); command = URL(fileURLWithPath: path + ".command") }
    func applicationDidFinishLaunching(_ notification: Notification) {
        window.title = "SystemTools M4 外部窗口合成目标"
        window.isReleasedWhenClosed = false
        window.contentView = NSTextField(labelWithString: "此独立窗口仅用于 M4 原生操作验收。")
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        timer = Timer.scheduledTimer(withTimeInterval: 0.05, repeats: true) { [self] _ in
            if FileManager.default.fileExists(atPath: command.path) {
                let action = (try? String(contentsOf: command, encoding: .utf8)) ?? "activate"
                try? FileManager.default.removeItem(at: command)
                if action == "minimize-baseline" {
                    window.performMiniaturize(nil)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [self] in baselineMinimized = window.isMiniaturized }
                }
                else { window.deminiaturize(nil); window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
            }
            let frame = window.frame
            let value: [String: Any] = ["pid": getpid(), "x": frame.origin.x, "y": frame.origin.y,
                "width": frame.width, "height": frame.height, "minimized": window.isMiniaturized,
                "visible": window.isVisible]
            var snapshot = value
            if let baseline = baselineMinimized { snapshot["baselineMinimized"] = baseline }
            if let data = try? JSONSerialization.data(withJSONObject: snapshot, options: [.sortedKeys]) { try? data.write(to: state, options: .atomic) }
        }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

guard CommandLine.arguments.count == 2 else { exit(2) }
let app = NSApplication.shared
let delegate = FixtureDelegate(CommandLine.arguments[1])
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
