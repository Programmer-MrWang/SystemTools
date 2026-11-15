import AppKit
import Darwin
import Foundation

struct NativeFailure: Error { let state: String; let message: String }

func mainThread<T>(_ operation: () throws -> T) rethrows -> T {
    if Thread.isMainThread { return try operation() }
    return try DispatchQueue.main.sync(execute: operation)
}

final class LocalChannel {
    let descriptor: Int32
    private let writes = NSLock()
    private let state = NSLock()
    private var closed = false

    init(path: String) throws {
        descriptor = Darwin.socket(AF_UNIX, SOCK_STREAM, 0)
        guard descriptor >= 0 else { throw CocoaError(.fileReadUnknown) }
        var noSignal: Int32 = 1
        setsockopt(descriptor, SOL_SOCKET, SO_NOSIGPIPE, &noSignal, socklen_t(MemoryLayout<Int32>.size))
        var address = sockaddr_un()
        address.sun_family = sa_family_t(AF_UNIX)
        let bytes = Array(path.utf8) + [0]
        guard bytes.count <= MemoryLayout.size(ofValue: address.sun_path) else {
            Darwin.close(descriptor); throw CocoaError(.fileReadInvalidFileName)
        }
        withUnsafeMutableBytes(of: &address.sun_path) { $0.copyBytes(from: bytes) }
        let connected = withUnsafePointer(to: &address) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.connect(descriptor, $0, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        var uid: uid_t = 0
        var gid: gid_t = 0
        guard connected == 0, getpeereid(descriptor, &uid, &gid) == 0, uid == geteuid() else {
            Darwin.close(descriptor); throw CocoaError(.fileReadNoPermission)
        }
    }

    func readExactly(_ count: Int) throws -> Data {
        var result = Data(count: count)
        try result.withUnsafeMutableBytes { storage in
            var position = 0
            while position < count {
                let amount = Darwin.read(descriptor, storage.baseAddress!.advanced(by: position), count - position)
                if amount < 0 && errno == EINTR { continue }
                guard amount > 0 else { throw CocoaError(.fileReadUnknown) }
                position += amount
            }
        }
        return result
    }

    func receive() throws -> [String: Any] {
        let header = try readExactly(4)
        let size = header.reduce(0) { ($0 << 8) | Int($1) }
        guard size > 0, size <= 65536,
              let request = try JSONSerialization.jsonObject(with: readExactly(size)) as? [String: Any] else {
            throw CocoaError(.fileReadCorruptFile)
        }
        return request
    }

    func send(_ value: [String: Any]) throws {
        let bytes = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
        guard bytes.count <= 24 * 1024 * 1024 else { throw CocoaError(.fileWriteOutOfSpace) }
        var length = UInt32(bytes.count).bigEndian
        var frame = withUnsafeBytes(of: &length) { Data($0) }
        frame.append(bytes)
        try writes.withLock {
            guard !state.withLock({ closed }) else { throw CocoaError(.fileWriteUnknown) }
            try frame.withUnsafeBytes { storage in
                var position = 0
                while position < frame.count {
                    let count = Darwin.write(descriptor, storage.baseAddress!.advanced(by: position), frame.count - position)
                    if count < 0 && errno == EINTR { continue }
                    guard count > 0 else { throw CocoaError(.fileWriteUnknown) }
                    position += count
                }
            }
        }
    }

    func stop() {
        state.withLock {
            if !closed { closed = true; Darwin.shutdown(descriptor, SHUT_RDWR) }
        }
    }
    deinit { stop(); Darwin.close(descriptor) }
}

func privateFile(_ path: String, directory: Bool) -> Bool {
    var entry = stat()
    guard lstat(path, &entry) == 0, entry.st_uid == geteuid() else { return false }
    let kind = entry.st_mode & mode_t(S_IFMT)
    return kind == mode_t(directory ? S_IFDIR : S_IFREG) &&
        entry.st_mode & 0o777 == mode_t(directory ? 0o700 : 0o600)
}

final class CompanionApp: NSObject, NSApplicationDelegate {
    var channel: LocalChannel?
    var item: NSStatusItem?
    var window: NSWindow?
    let lock = NSLock()
    var lastMessage = ProcessInfo.processInfo.systemUptime
    var directory: URL?
    var stopping = false
    let active = DispatchGroup()
    let capacity = DispatchSemaphore(value: 16)
    var watchdog: DispatchSourceTimer?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        menu.addItem(withTitle: "SystemTools 采集助手", action: nil, keyEquivalent: "")
        menu.addItem(withTitle: "停止采集并退出", action: #selector(stopFromMenu), keyEquivalent: "q").target = self
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item?.button?.image = NSImage(systemSymbolName: "camera.on.rectangle", accessibilityDescription: "SystemTools 采集助手")
        item?.button?.toolTip = "SystemTools 采集助手：设备只在插件请求时打开；可在此停止并退出。"
        item?.menu = menu
        guard let index = CommandLine.arguments.firstIndex(of: "--session"), index + 1 < CommandLine.arguments.count else {
            showStatus("请在 SystemTools 的相机、语音与媒体设置中连接采集助手。设备仅在您使用相应功能时打开。")
            return
        }
        let session = CommandLine.arguments[index + 1]
        DispatchQueue.global(qos: .userInitiated).async {
            do { try self.connect(session) }
            catch { self.shutdown() }
        }
    }

    func connect(_ path: String) throws {
        let file = URL(fileURLWithPath: path)
        let parent = file.deletingLastPathComponent()
        guard file.lastPathComponent == "session.json", privateFile(path, directory: false),
              privateFile(parent.path, directory: true),
              (try FileManager.default.attributesOfItem(atPath: path)[.size] as? Int ?? 0) < 16384,
              let session = try JSONSerialization.jsonObject(with: Data(contentsOf: file)) as? [String: Any],
              session["protocol"] as? Int == 1, let socket = session["socket"] as? String,
              socket == parent.appendingPathComponent("ipc").path,
              let token = session["token"] as? String, token.count == 64,
              token.allSatisfy({ $0.isHexDigit }), let host = session["hostPid"] as? Int32, host > 0, kill(host, 0) == 0 else {
            throw CocoaError(.fileReadNoPermission)
        }
        directory = parent
        let connection = try LocalChannel(path: socket)
        channel = connection
        try connection.send(["protocol": 1, "token": token, "bundleId": Bundle.main.bundleIdentifier ?? "",
                             "pid": getpid(), "parentPid": getppid()])
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.global())
        timer.schedule(deadline: .now() + 3, repeating: 3)
        timer.setEventHandler { [weak self] in
            guard let owner = self else { return }
            if owner.lock.withLock({ ProcessInfo.processInfo.systemUptime - owner.lastMessage > 15 }) { owner.shutdown() }
        }
        watchdog = timer
        timer.resume()
        defer { shutdown() }
        while true {
            let request = try connection.receive()
            guard let requestID = request["requestId"] as? Int64, requestID > 0,
                  let operation = request["operation"] as? String else { throw CocoaError(.fileReadCorruptFile) }
            lock.withLock { lastMessage = ProcessInfo.processInfo.systemUptime }
            guard capacity.wait(timeout: .now()) == .success else { throw CocoaError(.fileReadTooLarge) }
            active.enter()
            let perform = {
                defer { self.capacity.signal(); self.active.leave() }
                var reply: [String: Any]
                do { reply = try self.execute(request, operation: operation) }
                catch let error as NativeFailure { reply = ["state": error.state, "reason": error.message] }
                catch { reply = ["state": "MissingDevice", "reason": "采集未完成，请检查设备和权限后重试。"] }
                reply["requestId"] = requestID
                do { try connection.send(reply) } catch { connection.stop() }
            }
            // Preserve wire order for session creation and cleanup, including a cancellation arriving during prepare.
            if [".prepare", ".cancel", ".stop", ".release"].contains(where: { operation.hasSuffix($0) }) { perform() }
            else { DispatchQueue.global(qos: .userInitiated).async(execute: perform) }
        }
    }

    func execute(_ request: [String: Any], operation: String) throws -> [String: Any] {
        guard !lock.withLock({ stopping }) else { throw NativeFailure(state: "Disabled", message: "采集助手已停止。") }
        if operation == "session.ping" { return ["state": "Available"] }
        if operation == "session.status" {
            return ["state": "Available", "cameraJobs": cameraJobsLock.withLock { cameraJobs.count },
                    "microphoneJobs": microphoneQueue.sync { microphoneJobs.count },
                    "bundleId": Bundle.main.bundleIdentifier ?? "", "pid": getpid(), "parentPid": getppid()]
        }
        let allowed = ["camera.probe", "camera.request", "camera.devices", "camera.prepare", "camera.capture", "camera.cancel", "camera.release",
                       "microphone.probe", "microphone.request", "microphone.prepare", "microphone.start", "microphone.poll", "microphone.stop", "microphone.release"]
        if operation == "system.execute" {
            guard !lock.withLock({ stopping }) else { throw NativeFailure(state: "Disabled", message: "助手已停止。") }
            return try executeSystem(request)
        }
        guard allowed.contains(operation) else { throw NativeFailure(state: "Unsupported", message: "采集操作不受支持。") }
        if !operation.hasSuffix(".probe") && !operation.hasSuffix(".request") && operation != "camera.devices" {
            guard let id = request["id"] as? String, UUID(uuidString: id) != nil else {
                throw NativeFailure(state: "Unsupported", message: "无效的设备会话。")
            }
        }
        if operation.hasSuffix(".request") {
            DispatchQueue.main.async { self.showStatus("请在系统提示中选择是否允许 SystemTools 采集助手访问设备。授权可在系统设置的隐私与安全性中撤销。") }
        }
        if operation == "camera.capture" {
            guard let id = request["id"] as? String, let directory = directory else { throw CocoaError(.fileWriteUnknown) }
            let output = directory.appendingPathComponent(id + ".png")
            defer { try? FileManager.default.removeItem(at: output) }
            var capture = request
            capture["path"] = output.path
            let result = try handleCamera(capture, operation: operation)
            if result["state"] as? String != "Available" { return result }
            let data = try Data(contentsOf: output)
            guard data.count <= 16 * 1024 * 1024 else { throw CocoaError(.fileReadTooLarge) }
            return ["state": "Available", "png": data.base64EncodedString()]
        }
        if operation.hasPrefix("camera.") { return try handleCamera(request, operation: operation) }
        return try handleMicrophone(request, operation: operation)
    }

    func showStatus(_ text: String) {
        if window == nil {
            let view = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 440, height: 140), styleMask: [.titled, .closable], backing: .buffered, defer: false)
            view.title = "SystemTools 采集助手"
            view.isReleasedWhenClosed = false
            let label = NSTextField(wrappingLabelWithString: text)
            label.frame = NSRect(x: 24, y: 24, width: 392, height: 92)
            label.font = .systemFont(ofSize: 14)
            view.contentView?.addSubview(label)
            view.center()
            window = view
        } else if let label = window?.contentView?.subviews.first as? NSTextField { label.stringValue = text }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc func stopFromMenu() { DispatchQueue.global().async { self.shutdown() } }

    func shutdown() {
        let first = lock.withLock { () -> Bool in
            if stopping { return false }
            stopping = true
            return true
        }
        if !first { return }
        watchdog?.cancel()
        channel?.stop()
        cameraJobsLock.withLock { for job in cameraJobs.values { job.cancel() } }
        microphoneQueue.sync {
            for job in microphoneJobs.values { job.stop() }
            microphoneJobs.removeAll()
        }
        _ = active.wait(timeout: .now() + 5)
        if let directory = directory {
            for url in (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? [] {
                if url.pathExtension == "png" { try? FileManager.default.removeItem(at: url) }
            }
            try? FileManager.default.removeItem(atPath: directory.appendingPathComponent("ipc").path)
            try? FileManager.default.removeItem(at: directory)
        }
        DispatchQueue.main.async { NSApp.terminate(nil) }
    }

    func applicationWillTerminate(_ notification: Notification) {
        channel?.stop()
        cameraJobsLock.withLock { for job in cameraJobs.values { job.cancel() } }
        microphoneQueue.sync { for job in microphoneJobs.values { job.stop() } }
    }
}

let application = NSApplication.shared
let delegate = CompanionApp()
application.delegate = delegate
application.setActivationPolicy(.accessory)
application.run()


func executeSystem(_ request: [String: Any]) throws -> [String: Any] {
    let action = request["action"] as? String ?? ""
    let source: String
    switch action {
    case "shutdown": source = "tell application \"System Events\" to shut down"
    case "restart": source = "tell application \"System Events\" to restart"
    case "sleep": source = "tell application \"System Events\" to sleep"
    case "lock": source = "tell application \"System Events\" to keystroke \"q\" using {command down, control down}"
    case "theme.read": source = "tell application \"System Events\" to tell appearance preferences to get dark mode"
    case "theme.set":
        guard let value = request["value"] as? String, value == "dark" || value == "light" else {
            throw NativeFailure(state: "Unsupported", message: "主题值无效。")
        }
        source = "tell application \"System Events\" to tell appearance preferences to set dark mode to " + (value == "dark" ? "true" : "false")
    default: throw NativeFailure(state: "Unsupported", message: "系统操作不受支持。")
    }
    return try mainThread {
        var error: NSDictionary?
        let reply = NSAppleScript(source: source)!.executeAndReturnError(&error)
        guard error == nil else { throw NativeFailure(state: "PermissionRequired", message: "系统未允许助手自动化 System Events；请在隐私与安全性中检查自动化权限。") }
        return ["state": "Available", "value": action == "theme.read" ? (reply.booleanValue ? "dark" : "light") : ""]
    }
}
