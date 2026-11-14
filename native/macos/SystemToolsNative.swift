import AppKit
import ScreenCaptureKit
import Foundation
import IOKit
import DiskArbitration
import Carbon
import ApplicationServices

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

let logicalKeys: [String: UInt16] = [
    "A": 0, "S": 1, "D": 2, "F": 3, "H": 4, "G": 5, "Z": 6, "X": 7, "C": 8, "V": 9, "B": 11,
    "Q": 12, "W": 13, "E": 14, "R": 15, "Y": 16, "T": 17, "1": 18, "2": 19, "3": 20, "4": 21,
    "6": 22, "5": 23, "9": 25, "7": 26, "8": 28, "0": 29, "O": 31, "U": 32, "I": 34, "P": 35,
    "L": 37, "J": 38, "K": 40, "N": 45, "M": 46, "Return": 36, "Tab": 48, "Space": 49,
    "BackSpace": 51, "Escape": 53, "Meta": 55, "Shift": 56, "Alt": 58, "Control": 59,
    "F1": 122, "F2": 120, "F3": 99, "F4": 118, "F5": 96, "F6": 97, "F7": 98, "F8": 100,
    "F9": 101, "F10": 109, "F11": 103, "F12": 111, "Delete": 117, "Left": 123, "Right": 124, "Down": 125, "Up": 126
]
func keyCode(_ name: String) -> UInt16? {
    if name.count != 1 { return logicalKeys[name] }
    guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
          let raw = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return nil }
    let data = Unmanaged<CFData>.fromOpaque(raw).takeUnretainedValue()
    guard let bytes = CFDataGetBytePtr(data) else { return nil }
    let layout = UnsafeRawPointer(bytes).assumingMemoryBound(to: UCKeyboardLayout.self)
    for code in UInt16(0)...UInt16(127) {
        var dead: UInt32 = 0
        var length = 0
        var characters = [UniChar](repeating: 0, count: 8)
        if UCKeyTranslate(layout, code, UInt16(kUCKeyActionDown), 0, UInt32(LMGetKbdType()),
                          OptionBits(kUCKeyTranslateNoDeadKeysBit), &dead, 8, &length, &characters) == noErr,
           length > 0,
           String(utf16CodeUnits: characters, count: length).uppercased() == name.uppercased() { return code }
    }
    return nil
}

var hotkeyHandler: EventHandlerRef?
var hotkeyNext: UInt32 = 1
var hotkeys: [UInt32: EventHotKeyRef] = [:]
var hotkeyCounts: [UInt32: Int] = [:]
var inputFlags = CGEventFlags()

func hotkeyEvent(_ next: EventHandlerCallRef?, _ event: EventRef?, _ context: UnsafeMutableRawPointer?) -> OSStatus {
    var identity = EventHotKeyID()
    if GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil,
                         MemoryLayout<EventHotKeyID>.size, nil, &identity) == noErr {
        hotkeyCounts[identity.id, default: 0] += 1
    }
    return noErr
}

func inputCapability() -> [String: Any] {
    AXIsProcessTrusted() ? ["state": "Available"] : ["state": "PermissionRequired", "reason": "请在系统设置中允许 ClassIsland 使用辅助功能，然后重试。"]
}

func inputTarget() throws -> String {
    guard AXIsProcessTrusted() else { throw NativeFailure(state: "PermissionRequired", message: "请先授予辅助功能权限。") }
    guard let application = mainThread({ NSWorkspace.shared.frontmostApplication }) else {
        throw NativeFailure(state: "MissingDevice", message: "没有可用的输入目标。")
    }
    let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], 0) as? [[String: Any]] ?? []
    guard let window = windows.first(where: { ($0[kCGWindowOwnerPID as String] as? Int32) == application.processIdentifier && ($0[kCGWindowLayer as String] as? Int) == 0 }),
          let number = window[kCGWindowNumber as String] as? UInt32 else {
        throw NativeFailure(state: "MissingDevice", message: "无法确定输入目标窗口。")
    }
    return "\(application.processIdentifier):\(number)"
}

func handleInput(_ request: [String: Any], operation: String) throws -> [String: Any] {
    if operation == "input.probe" { return inputCapability() }
    if operation == "input.request" {
        _ = mainThread { AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary) }
        return inputCapability()
    }
    if operation == "input.target" { return ["state": "Available", "target": try inputTarget()] }
    let down = request["down"] as? Bool ?? true
    if down || operation == "input.text" {
        guard request["target"] as? String == (try inputTarget()) else {
            throw NativeFailure(state: "MissingDevice", message: "输入目标已改变，已停止后续输入。")
        }
    }
    if operation == "input.text" {
        let text = request["text"] as? String ?? ""
        for character in text {
            guard request["target"] as? String == (try inputTarget()) else { throw NativeFailure(state: "MissingDevice", message: "输入目标已改变。") }
            var units = Array(String(character).utf16)
            guard let event = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true),
                  let release = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: false) else { throw NativeFailure(state: "MissingDevice", message: "无法创建输入事件。") }
            event.keyboardSetUnicodeString(stringLength: units.count, unicodeString: &units)
            release.keyboardSetUnicodeString(stringLength: units.count, unicodeString: &units)
            event.post(tap: .cghidEventTap); release.post(tap: .cghidEventTap)
        }
    } else {
        guard let key = request["key"] as? String, let code = keyCode(key) else {
            throw NativeFailure(state: "Unsupported", message: "当前 Mac 键盘映射不支持此逻辑键，请使用文本输入或重新录制。")
        }
        let modifier: CGEventFlags? = ["Meta": .maskCommand, "Shift": .maskShift, "Alt": .maskAlternate, "Control": .maskControl][key]
        if let modifier = modifier { if down { inputFlags.insert(modifier) } else { inputFlags.remove(modifier) } }
        guard let event = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: down) else { throw NativeFailure(state: "MissingDevice", message: "无法创建按键事件。") }
        event.flags = inputFlags
        event.post(tap: .cghidEventTap)
    }
    return ["state": "Available"]
}

func handleHotkey(_ request: [String: Any], operation: String) throws -> [String: Any] {
    if operation == "hotkey.probe" { return ["state": "Available"] }
    return try mainThread {
        let identity = UInt32(request["id"] as? Int ?? 0)
        if operation == "hotkey.take" {
            let count = hotkeyCounts[identity, default: 0]; hotkeyCounts[identity] = 0
            return ["state": "Available", "count": count]
        }
        if operation == "hotkey.unregister" {
            if let reference = hotkeys.removeValue(forKey: identity) { UnregisterEventHotKey(reference) }
            hotkeyCounts.removeValue(forKey: identity)
            if hotkeys.isEmpty, let handler = hotkeyHandler { RemoveEventHandler(handler); hotkeyHandler = nil }
            return ["state": "Available"]
        }
        guard let key = request["key"] as? String, let code = keyCode(key) else { throw NativeFailure(state: "Unsupported", message: "此逻辑键不能用于当前热键。") }
        if hotkeyHandler == nil {
            var event = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            guard InstallEventHandler(GetApplicationEventTarget(), hotkeyEvent, 1, &event, nil, &hotkeyHandler) == noErr else { throw NativeFailure(state: "Unsupported", message: "无法安装系统热键监听。") }
        }
        let flags = request["modifiers"] as? Int ?? 0
        let modifiers = UInt32(((flags & 1) != 0 ? optionKey : 0) | ((flags & 2) != 0 ? controlKey : 0) | ((flags & 4) != 0 ? shiftKey : 0) | ((flags & 8) != 0 ? cmdKey : 0))
        let id = hotkeyNext; hotkeyNext += 1
        var reference: EventHotKeyRef?
        let status = RegisterEventHotKey(UInt32(code), modifiers, EventHotKeyID(signature: 0x53544d34, id: id), GetApplicationEventTarget(), 0, &reference)
        guard status == noErr, let reference = reference else { throw NativeFailure(state: "Unsupported", message: "热键被占用或系统拒绝注册，请更换组合键。") }
        hotkeys[id] = reference
        return ["state": "Available", "id": id]
    }
}

func dispatch(_ request: [String: Any]) throws -> [String: Any] {
    guard let operation = request["operation"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少操作。") }
    if operation.hasPrefix("input.") { return try handleInput(request, operation: operation) }
    if operation.hasPrefix("hotkey.") { return try handleHotkey(request, operation: operation) }
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
