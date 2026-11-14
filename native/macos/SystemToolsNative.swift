import AppKit
import ScreenCaptureKit
import Foundation
import IOKit
import DiskArbitration

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

func usbAncestor(_ entry: io_registry_entry_t) -> UInt64? {
    var current = entry
    IOObjectRetain(current)
    while current != 0 {
        if IOObjectConformsTo(current, "IOUSBHostDevice") != 0 || IOObjectConformsTo(current, "IOUSBDevice") != 0 {
            var identity: UInt64 = 0
            let status = IORegistryEntryGetRegistryEntryID(current, &identity)
            IOObjectRelease(current)
            return status == KERN_SUCCESS ? identity : nil
        }
        var parent: io_registry_entry_t = 0
        let status = IORegistryEntryGetParentEntry(current, kIOServicePlane, &parent)
        IOObjectRelease(current)
        if status != KERN_SUCCESS { return nil }
        current = parent
    }
    return nil
}

func usbSnapshot() throws -> [[String: Any]] {
    var iterator: io_iterator_t = 0
    guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOUSBHostDevice"), &iterator) == KERN_SUCCESS else {
        throw NativeFailure(state: "MissingDependency", message: "系统 USB 服务暂不可用。")
    }
    defer { IOObjectRelease(iterator) }
    var devices: [UInt64: [String: Any]] = [:]
    var entry = IOIteratorNext(iterator)
    while entry != 0 {
        var identity: UInt64 = 0
        if IORegistryEntryGetRegistryEntryID(entry, &identity) == KERN_SUCCESS {
            devices[identity] = ["id": String(identity), "storage": false, "mounts": [String]()]
        }
        IOObjectRelease(entry); entry = IOIteratorNext(iterator)
    }
    guard let session = DASessionCreate(kCFAllocatorDefault) else { return Array(devices.values) }
    let volumes = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: [.volumeIsBrowsableKey], options: [.skipHiddenVolumes]) ?? []
    for volume in volumes {
        guard let disk = DADiskCreateFromVolumePath(kCFAllocatorDefault, session, volume as CFURL) else { continue }
        let media = DADiskCopyIOMedia(disk)
        if media == 0 { continue }
        defer { IOObjectRelease(media) }
        guard let identity = usbAncestor(media), var device = devices[identity] else { continue }
        var mounts = device["mounts"] as? [String] ?? []
        if FileManager.default.isReadableFile(atPath: volume.path) { mounts.append(volume.path) }
        device["mounts"] = mounts
        device["storage"] = true
        devices[identity] = device
    }
    return Array(devices.values)
}

func dispatch(_ request: [String: Any]) throws -> [String: Any] {
    guard let operation = request["operation"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少操作。") }
    switch operation {
    case "usb.probe":
        _ = try usbSnapshot(); return ["state": "Available"]
    case "usb.snapshot": return ["state": "Available", "devices": try usbSnapshot()]
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
