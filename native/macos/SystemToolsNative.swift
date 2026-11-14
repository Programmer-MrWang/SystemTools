import AppKit
import ScreenCaptureKit
import Foundation

struct NativeFailure: Error {
    let state: String
    let message: String
}

func mainThread<T>(_ operation: () throws -> T) rethrows -> T {
    if Thread.isMainThread { return try operation() }
    return try DispatchQueue.main.sync(execute: operation)
}

func screenCapability() -> [String: Any] {
    guard #available(macOS 14.0, *) else {
        return ["state": "Unsupported", "reason": "截图需要 macOS 14 或更新版本。"]
    }
    return CGPreflightScreenCaptureAccess()
        ? ["state": "Available"]
        : ["state": "PermissionRequired", "reason": "请在系统设置中允许 ClassIsland 录制屏幕，然后重启应用。"]
}

@available(macOS 14.0, *)
func captureScreen(_ path: String) throws {
    if Thread.isMainThread { throw NativeFailure(state: "Unsupported", message: "屏幕采集必须在后台执行。") }
    let completed = DispatchSemaphore(value: 0)
    let lock = NSLock()
    var failure: Error?
    var abandoned = false
    let task = Task {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
            guard let display = content.displays.first(where: { $0.displayID == CGMainDisplayID() }) else {
                throw NativeFailure(state: "MissingDevice", message: "没有可采集的主显示器。")
            }
            let configuration = SCStreamConfiguration()
            configuration.width = display.width
            configuration.height = display.height
            configuration.showsCursor = false
            let filter = SCContentFilter(display: display, excludingWindows: [])
            let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)
            try Task.checkCancellation()
            guard let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else {
                throw NativeFailure(state: "Unsupported", message: "无法编码屏幕图像。")
            }
            try lock.withLock {
                if abandoned { throw CancellationError() }
                try png.write(to: URL(fileURLWithPath: path), options: .atomic)
            }
        } catch { lock.withLock { failure = error } }
        completed.signal()
    }
    if completed.wait(timeout: .now() + 15) == .timedOut {
        lock.withLock { abandoned = true }
        task.cancel()
        throw NativeFailure(state: "MissingDevice", message: "屏幕采集超时，请确认屏幕已解锁并重试。")
    }
    let error = lock.withLock { failure }
    if let error = error { throw error }
}

func dispatch(_ request: [String: Any]) throws -> [String: Any] {
    guard let operation = request["operation"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少操作。") }
    switch operation {
    case "screen.probe": return screenCapability()
    case "screen.request":
        _ = mainThread { CGRequestScreenCaptureAccess() }
        return screenCapability()
    case "screen.capture":
        guard #available(macOS 14.0, *), CGPreflightScreenCaptureAccess() else {
            throw NativeFailure(state: "PermissionRequired", message: "请允许屏幕录制权限后重试。")
        }
        guard let path = request["path"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少输出文件。") }
        try captureScreen(path)
        return ["state": "Available"]
    default: throw NativeFailure(state: "Unsupported", message: "当前系统不支持此操作。")
    }
}

@_cdecl("systemtools_native_call")
public func nativeCall(_ pointer: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    var response: [String: Any]
    do {
        guard let pointer = pointer,
              let data = String(cString: pointer).data(using: .utf8),
              let request = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw NativeFailure(state: "Unsupported", message: "无法读取请求。")
        }
        response = try dispatch(request)
    } catch let error as NativeFailure {
        response = ["state": error.state, "reason": error.message]
    } catch {
        response = ["state": "MissingDevice", "reason": "系统操作未完成，请检查权限、设备和目标后重试。"]
    }
    guard let data = try? JSONSerialization.data(withJSONObject: response, options: [.sortedKeys]),
          let text = String(data: data, encoding: .utf8) else { return nil }
    return strdup(text)
}

@_cdecl("systemtools_native_free")
public func nativeFree(_ pointer: UnsafeMutableRawPointer?) { free(pointer) }
