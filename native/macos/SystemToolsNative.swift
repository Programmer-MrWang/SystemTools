import AppKit
import Vision
import ImageIO
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

func axValue(_ element: AXUIElement, _ attribute: String) throws -> CFTypeRef {
    var value: CFTypeRef?
    let status = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
    guard status == .success, let value = value else {
        throw NativeFailure(state: "Unsupported", message: "目标窗口没有提供此操作所需的辅助功能属性。")
    }
    return value
}

var windowRestoreFrames: [String: (AXUIElement, CGPoint, CGSize)] = [:]

func windowGeometry(_ window: AXUIElement) throws -> (CGPoint, CGSize) {
    var point = CGPoint.zero
    var size = CGSize.zero
    let positionValue = try axValue(window, kAXPositionAttribute)
    let sizeValue = try axValue(window, kAXSizeAttribute)
    guard CFGetTypeID(positionValue) == AXValueGetTypeID(), CFGetTypeID(sizeValue) == AXValueGetTypeID(),
          AXValueGetValue(unsafeBitCast(positionValue, to: AXValue.self), .cgPoint, &point),
          AXValueGetValue(unsafeBitCast(sizeValue, to: AXValue.self), .cgSize, &size) else {
        throw NativeFailure(state: "Unsupported", message: "目标窗口不支持读取位置和尺寸。")
    }
    return (point, size)
}

func applyWindowGeometry(_ window: AXUIElement, _ point: CGPoint, _ size: CGSize) throws {
    var position = point
    var dimensions = size
    guard let p = AXValueCreate(.cgPoint, &position), let z = AXValueCreate(.cgSize, &dimensions),
          AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, p) == .success,
          AXUIElementSetAttributeValue(window, kAXSizeAttribute as CFString, z) == .success,
          AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, p) == .success else {
        throw NativeFailure(state: "Unsupported", message: "目标应用拒绝调整窗口位置或尺寸。")
    }
}

func windowExecute(_ action: String) throws {
    guard !Thread.isMainThread else { throw NativeFailure(state: "Unsupported", message: "窗口状态确认必须在后台执行。") }
    var geometry: (AXUIElement, CGPoint, CGSize)?
    var minimized: (AXUIElement, Bool)?
    var localMinimized: (NSWindow, Bool)?
    var restoredIdentity: String?
    try mainThread {
        guard AXIsProcessTrusted(), let application = NSWorkspace.shared.frontmostApplication else {
            throw NativeFailure(state: "PermissionRequired", message: "请授予辅助功能权限并激活目标窗口。")
        }
        let app = AXUIElementCreateApplication(application.processIdentifier)
        // 最小化后部分应用暂时没有 focused window；只回退到系统明确标识的 main window。
        let focused = try (try? axValue(app, kAXFocusedWindowAttribute)) ?? axValue(app, kAXMainWindowAttribute)
        guard CFGetTypeID(focused) == AXUIElementGetTypeID() else { throw NativeFailure(state: "MissingDevice", message: "没有可操作的活动窗口。") }
        let window = unsafeBitCast(focused, to: AXUIElement.self)
        let identity = "\(application.processIdentifier):\(CFHash(window))"
        switch action {
        case "最小化", "向下还原":
            let value = action == "最小化"
            if application.processIdentifier == getpid(), let local = NSApp.keyWindow {
                guard local.styleMask.contains(.miniaturizable) else { throw NativeFailure(state: "Unsupported", message: "目标窗口不允许最小化。") }
                if value { local.miniaturize(nil) } else { local.deminiaturize(nil) }
                localMinimized = (local, value)
            } else {
                let result: AXError
                if value {
                    let button = try axValue(window, kAXMinimizeButtonAttribute)
                    guard CFGetTypeID(button) == AXUIElementGetTypeID() else { throw NativeFailure(state: "Unsupported", message: "目标窗口没有提供最小化按钮。") }
                    result = AXUIElementPerformAction(unsafeBitCast(button, to: AXUIElement.self), kAXPressAction as CFString)
                } else {
                    result = AXUIElementSetAttributeValue(window, kAXMinimizedAttribute as CFString, kCFBooleanFalse!)
                }
                guard result == .success else {
                    throw NativeFailure(state: "Unsupported", message: "目标应用拒绝更改最小化状态。")
                }
                minimized = (window, value)
            }
            if !value, let old = windowRestoreFrames[identity], CFEqual(old.0, window) {
                try applyWindowGeometry(window, old.1, old.2)
                geometry = (window, old.1, old.2)
                restoredIdentity = identity
            }
        case "关闭窗口":
            let button = try axValue(window, kAXCloseButtonAttribute)
            guard CFGetTypeID(button) == AXUIElementGetTypeID(), AXUIElementPerformAction(unsafeBitCast(button, to: AXUIElement.self), kAXPressAction as CFString) == .success else {
                throw NativeFailure(state: "Unsupported", message: "目标应用拒绝关闭窗口。")
            }
            windowRestoreFrames.removeValue(forKey: identity)
        case "最大化":
            let (point, size) = try windowGeometry(window)
            let mainHeight = NSScreen.screens.first?.frame.height ?? 0
            let screen = NSScreen.screens.first(where: { $0.frame.contains(CGPoint(x: point.x + size.width / 2, y: mainHeight - point.y - size.height / 2)) }) ?? NSScreen.main
            guard let frame = screen?.visibleFrame else { throw NativeFailure(state: "MissingDevice", message: "没有可用的目标显示器。") }
            let target = CGPoint(x: frame.minX, y: mainHeight - frame.maxY)
            if abs(size.width - frame.width) > 4 || abs(size.height - frame.height) > 4 {
                if windowRestoreFrames.count >= 128 { throw NativeFailure(state: "Unsupported", message: "窗口恢复记录已满，请先恢复已有窗口或重启应用。") }
                if windowRestoreFrames[identity] == nil { windowRestoreFrames[identity] = (window, point, size) }
                try applyWindowGeometry(window, target, frame.size)
                geometry = (window, target, frame.size)
            }
        default: throw NativeFailure(state: "Unsupported", message: "不支持此窗口操作。")
        }
    }
    // AX 写入后由目标应用更新窗口；将确认移出主线程，使自身窗口也能处理布局事件。
    let deadline = ProcessInfo.processInfo.systemUptime + 2
    repeat {
        let confirmed = try mainThread { () throws -> Bool in
            if let (window, value) = localMinimized, window.isMiniaturized != value { return false }
            if let (window, value) = minimized, (try axValue(window, kAXMinimizedAttribute) as? Bool) != value { return false }
            if let (window, point, size) = geometry {
                let (actualPoint, actualSize) = try windowGeometry(window)
                if abs(actualPoint.x - point.x) > 4 || abs(actualPoint.y - point.y) > 4 ||
                   abs(actualSize.width - size.width) > 4 || abs(actualSize.height - size.height) > 4 { return false }
            }
            if let identity = restoredIdentity { windowRestoreFrames.removeValue(forKey: identity) }
            return true
        }
        if confirmed { return }
        Thread.sleep(forTimeInterval: 0.025)
    } while ProcessInfo.processInfo.systemUptime < deadline
    throw NativeFailure(state: "Unsupported", message: "目标应用未确认请求的窗口状态，请检查尺寸限制和窗口权限。")
}

func wallpaperFillComponents(_ color: NSColor?) -> [Double] {
    // 缺省色和无法转换的颜色直接使用数值，避免对灰阶 NSColor 读取 RGB 分量而抛出 Objective-C 异常。
    guard let rgb = color?.usingColorSpace(.sRGB) else { return [0, 0, 0] }
    return [Double(rgb.redComponent), Double(rgb.greenComponent), Double(rgb.blueComponent)]
}

func wallpaperRows() throws -> [[String: Any]] {
    return try mainThread {
        try NSScreen.screens.map { screen in
            guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber,
                  let url = NSWorkspace.shared.desktopImageURL(for: screen) else { throw NativeFailure(state: "MissingDevice", message: "无法读取显示器壁纸。") }
            let options = NSWorkspace.shared.desktopImageOptions(for: screen) ?? [:]
            return ["id": number.uint32Value, "path": url.path,
                    "scale": (options[.imageScaling] as? NSNumber)?.intValue ?? Int(NSImageScaling.scaleProportionallyUpOrDown.rawValue),
                    "clip": (options[.allowClipping] as? NSNumber)?.boolValue ?? true,
                    "fill": wallpaperFillComponents(options[.fillColor] as? NSColor)]
        }.sorted { ($0["id"] as! UInt32) < ($1["id"] as! UInt32) }
    }
}

func wallpaperSnapshot(_ rows: [[String: Any]]) throws -> String {
    let data = try JSONSerialization.data(withJSONObject: rows, options: [.sortedKeys])
    return String(data: data, encoding: .utf8)!
}

func wallpaperSnapshot() throws -> String { try wallpaperSnapshot(wallpaperRows()) }

func wallpaperWrite(_ rows: [[String: Any]]) throws {
    try mainThread {
        let screens = NSScreen.screens
        guard screens.count == rows.count else { throw NativeFailure(state: "MissingDevice", message: "显示器已变化，已保留当前壁纸。") }
        for row in rows {
            guard let identity = row["id"] as? UInt32,
                  let screen = screens.first(where: { ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == identity }),
                  let path = row["path"] as? String else { throw NativeFailure(state: "MissingDevice", message: "原显示器已断开。") }
            let fill = row["fill"] as? [Double] ?? [0, 0, 0]
            let options: [NSWorkspace.DesktopImageOptionKey: Any] = [
                .imageScaling: row["scale"] as? Int ?? Int(NSImageScaling.scaleProportionallyUpOrDown.rawValue),
                .allowClipping: row["clip"] as? Bool ?? true,
                .fillColor: NSColor(srgbRed: fill[0], green: fill[1], blue: fill[2], alpha: 1)]
            try NSWorkspace.shared.setDesktopImageURL(URL(fileURLWithPath: path), for: screen, options: options)
        }
    }
}

func wallpaperWriteConfirmed(_ rows: [[String: Any]]) throws {
    guard !Thread.isMainThread else { throw NativeFailure(state: "Unsupported", message: "壁纸写入确认必须在后台执行。") }
    let expected = try wallpaperSnapshot(rows)
    try wallpaperWrite(rows)
    let deadline = ProcessInfo.processInfo.systemUptime + 3
    // NSWorkspace 写入返回时桌面服务可能尚未更新；只在后台等待实际读回，不能阻塞 AppKit 主线程。
    repeat {
        if try wallpaperSnapshot() == expected { return }
        Thread.sleep(forTimeInterval: 0.05)
    } while ProcessInfo.processInfo.systemUptime < deadline
    throw NativeFailure(state: "Unsupported", message: "系统没有确认新的壁纸图片或布局。")
}

func wallpaperRollback(_ original: [[String: Any]], _ written: [[String: Any]]) throws {
    let current = try wallpaperRows()
    var changed = false
    let rows = try current.map { row -> [String: Any] in
        guard let applied = written.first(where: { ($0["id"] as? UInt32) == (row["id"] as? UInt32) }),
              let saved = original.first(where: { ($0["id"] as? UInt32) == (row["id"] as? UInt32) }),
              try wallpaperSnapshot([row]) == wallpaperSnapshot([applied]) else { return row }
        changed = true
        return saved
    }
    if changed { try wallpaperWriteConfirmed(rows) }
}

func handleWallpaper(_ request: [String: Any], operation: String) throws -> [String: Any] {
    if operation == "wallpaper.probe" { return ["state": "Available"] }
    if operation == "wallpaper.read" {
        let rows = try wallpaperRows()
        return ["state": "Available", "snapshot": try wallpaperSnapshot(rows), "path": rows.first?["path"] as? String ?? ""]
    }
    if operation == "wallpaper.restore" {
        guard request["expected"] as? String == (try wallpaperSnapshot()) else { throw NativeFailure(state: "Unsupported", message: "壁纸已被用户或其他应用修改，已保留当前壁纸。") }
        guard let original = request["original"] as? String,
              let rows = try JSONSerialization.jsonObject(with: Data(original.utf8)) as? [[String: Any]] else { throw NativeFailure(state: "Unsupported", message: "壁纸恢复记录无效。") }
        try wallpaperWriteConfirmed(rows)
        return ["state": "Available"]
    }
    guard let path = request["path"] as? String, FileManager.default.isReadableFile(atPath: path),
          let fit = request["fitStyle"] as? Int else { throw NativeFailure(state: "Unsupported", message: "请选择可读取的壁纸图片。") }
    guard (1...4).contains(fit) else { throw NativeFailure(state: "Unsupported", message: "Mac 暂不支持平铺或跨屏拼接，请选择居中、拉伸、填充或适应。") }
    let original = try wallpaperRows()
    let rows = original.map { value -> [String: Any] in
        var row = value
        row["path"] = path
        row["scale"] = Int((fit == 1 ? NSImageScaling.scaleNone : fit == 2 ? .scaleAxesIndependently : .scaleProportionallyUpOrDown).rawValue)
        row["clip"] = fit == 3
        return row
    }
    do {
        try wallpaperWriteConfirmed(rows)
    } catch { try? wallpaperRollback(original, rows); throw error }
    return ["state": "Available"]
}

func dispatch(_ request: [String: Any]) throws -> [String: Any] {
    guard let operation = request["operation"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少操作。") }
    if operation.hasPrefix("display.") { return try handleDisplay(request, operation: operation) }
    if operation.hasPrefix("material.") { return try mainThread { try handleMaterial(request, operation: operation) } }
    if operation == "ocr.probe" { return ["state": "Available"] }
    if operation == "ocr.recognize" { return try recognizeBackgroundText(request) }
    if operation == "background.probe" { return screenCapability() }
    if operation == "background.region" {
        return try mainThread {
            let window = try materialWindow(request)
            let frame = window.contentRect(forFrameRect: window.frame)
            let top = NSScreen.screens.first?.frame.maxY ?? 0
            return ["state": "Available", "x": frame.minX + (request["x"] as? Double ?? 0),
                    "y": top - frame.maxY + (request["y"] as? Double ?? 0)]
        }
    }
    if operation == "background.capture" {
        guard #available(macOS 14.0, *), CGPreflightScreenCaptureAccess() else {
            throw NativeFailure(state: "PermissionRequired", message: "背景捕获需要 macOS 14 和屏幕录制权限。")
        }
        return try captureBackground(request)
    }
    if operation.hasPrefix("camera.") || operation.hasPrefix("microphone.") {
        return ["state": "Unsupported", "reason": "相机和麦克风由 SystemTools 采集助手提供，请通过插件媒体设置连接。"]
    }
    if operation == "companion.launch" {
        guard !Thread.isMainThread, let path = request["application"] as? String,
              let session = request["session"] as? String else {
            throw NativeFailure(state: "Unsupported", message: "采集助手必须在后台通过应用启动服务打开。")
        }
        let finished = DispatchSemaphore(value: 0)
        let lock = NSLock()
        var reply: [String: Any] = ["state": "MissingDependency", "reason": "无法打开 SystemTools 采集助手，请重新安装插件。"]
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.arguments = ["--session", session]
        configuration.activates = false
        configuration.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(at: URL(fileURLWithPath: path), configuration: configuration) { app, error in
            if let app = app, error == nil { lock.withLock { reply = ["state": "Available", "pid": app.processIdentifier] } }
            finished.signal()
        }
        guard finished.wait(timeout: .now() + 10) == .success else {
            throw NativeFailure(state: "MissingDependency", message: "采集助手启动超时，请重新检查。")
        }
        return lock.withLock { reply }
    }
    if operation.hasPrefix("authentication.") { return try handleAuthentication(request, operation: operation) }
    if operation.hasPrefix("wallpaper.") { return try handleWallpaper(request, operation: operation) }
    if operation.hasPrefix("input.") { return try handleInput(request, operation: operation) }
    if operation.hasPrefix("hotkey.") { return try handleHotkey(request, operation: operation) }
    switch operation {
    case "window.clear":
        mainThread { windowRestoreFrames.removeAll() }; return ["state": "Available"]
    case "window.execute":
        try windowExecute(request["action"] as? String ?? ""); return ["state": "Available"]
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

// ScreenCaptureKit excludes the entire host application, including hidden and newly opened plugin windows.
@available(macOS 14.0, *)
func captureBackground(_ request: [String: Any]) throws -> [String: Any] {
    guard !Thread.isMainThread, let x = request["x"] as? Double, let y = request["y"] as? Double,
          let width = request["width"] as? Double, let height = request["height"] as? Double,
          [x, y, width, height].allSatisfy({ $0.isFinite }), width > 0, height > 0, width <= 8192, height <= 8192 else {
        throw NativeFailure(state: "Unsupported", message: "背景采集区域无效或不在后台线程。")
    }
    let region = CGRect(x: x, y: y, width: width, height: height)
    let completed = DispatchSemaphore(value: 0)
    let lock = NSLock()
    var result: [String: Any]?
    var failure: Error?
    let task = Task {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            guard let display = content.displays.first(where: { CGDisplayBounds($0.displayID).contains(region) }) else {
                throw NativeFailure(state: "MissingDevice", message: "区域跨越显示器或显示器已移除，请移动窗口后重试。")
            }
            let bounds = CGDisplayBounds(display.displayID)
            let scale = Double(CGDisplayPixelsWide(display.displayID)) / bounds.width
            let configuration = SCStreamConfiguration()
            configuration.sourceRect = CGRect(x: x - bounds.minX, y: y - bounds.minY, width: width, height: height)
            configuration.width = Int(ceil(width * scale))
            configuration.height = Int(ceil(height * scale))
            guard configuration.width * configuration.height <= 16_000_000 else {
                throw NativeFailure(state: "Unsupported", message: "背景区域像素过大，请缩小窗口。")
            }
            configuration.showsCursor = false
            let filter = SCContentFilter(display: display, excludingApplications: content.applications.filter { $0.processID == getpid() }, exceptingWindows: [])
            let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)
            try Task.checkCancellation()
            guard let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else {
                throw NativeFailure(state: "MissingDevice", message: "无法编码背景。")
            }
            lock.withLock { result = ["state": "Available", "png": png.base64EncodedString(), "width": image.width,
                                      "height": image.height, "excludedProcessId": getpid()] }
        } catch { lock.withLock { failure = error } }
        completed.signal()
    }
    guard completed.wait(timeout: .now() + 15) == .success else {
        task.cancel()
        throw NativeFailure(state: "MissingDevice", message: "背景捕获超时，画面未保存。")
    }
    if let error = lock.withLock({ failure }) { throw error }
    guard let reply = lock.withLock({ result }) else { throw NativeFailure(state: "MissingDevice", message: "背景没有返回画面。") }
    return reply
}

func materialWindow(_ request: [String: Any]) throws -> NSWindow {
    guard let number = request["handle"] as? UInt64, number != 0, let pointer = UnsafeRawPointer(bitPattern: UInt(number)) else {
        throw NativeFailure(state: "Unsupported", message: "缺少原生窗口句柄。")
    }
    let object = Unmanaged<AnyObject>.fromOpaque(pointer).takeUnretainedValue()
    if let window = object as? NSWindow { return window }
    if let view = object as? NSView, let window = view.window { return window }
    throw NativeFailure(state: "Unsupported", message: "句柄不是原生窗口或视图。")
}

func recognizeBackgroundText(_ request: [String: Any]) throws -> [String: Any] {
    guard !Thread.isMainThread, let value = request["png"] as? String, value.count <= 48_000_000,
          let data = Data(base64Encoded: value), let source = CGImageSourceCreateWithData(data as CFData, nil),
          let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [String: Any],
          let width = properties[kCGImagePropertyPixelWidth as String] as? Int,
          let height = properties[kCGImagePropertyPixelHeight as String] as? Int,
          width > 0, height > 0, width <= 16384, height <= 16384, width * height <= 16_000_000,
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        throw NativeFailure(state: "Unsupported", message: "OCR 图像无效、过大或不在后台线程。")
    }
    let recognition = VNRecognizeTextRequest()
    recognition.recognitionLevel = .accurate
    recognition.recognitionLanguages = ["zh-Hans", "zh-Hant", "en-US"]
    recognition.usesLanguageCorrection = true
    try VNImageRequestHandler(cgImage: image, options: [:]).perform([recognition])
    let boxes = (recognition.results ?? []).prefix(500).compactMap { observation -> [String: Any]? in
        guard let candidate = observation.topCandidates(1).first else { return nil }
        let box = observation.boundingBox
        return ["x": box.minX, "y": 1 - box.maxY, "width": box.width, "height": box.height, "confidence": candidate.confidence]
    }
    return ["state": "Available", "boxes": boxes]
}

final class MaterialLease {
    let window: NSWindow
    let original: NSView
    let background: NSColor
    let opaque: Bool
    let requested: String
    var observer: NSObjectProtocol?
    var wrapper: NSView?
    var applied = "Solid"
    var reason = ""
    init(window: NSWindow, requested: String) throws {
        guard let original = window.contentView else { throw NativeFailure(state: "Unsupported", message: "原生内容视图尚未就绪。") }
        self.window = window; self.original = original; self.requested = requested
        background = window.backgroundColor; opaque = window.isOpaque
        refresh()
        observer = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification, object: nil, queue: .main) { [weak self] _ in self?.refresh() }
    }
    func refresh() {
        original.removeFromSuperview()
        window.contentView = original
        let frame = original.frame
        let solid = requested == "Solid" || NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency || NSWorkspace.shared.accessibilityDisplayShouldIncreaseContrast
        let surface: NSView
        if !solid && requested == "SystemGlass" {
            if #available(macOS 26.0, *) {
                let glass = NSGlassEffectView(frame: frame)
                glass.style = .regular; glass.cornerRadius = 16
                glass.contentView = original
                surface = glass; applied = "SystemGlass"; reason = "macOS 原生玻璃"
            } else {
                let effect = NSVisualEffectView(frame: frame)
                effect.material = .hudWindow; effect.blendingMode = .behindWindow; effect.state = .followsWindowActiveState
                effect.addSubview(original); surface = effect
                applied = "SystemMaterial"; reason = "当前系统使用原生磨砂材质"
            }
        } else if !solid {
            let effect = NSVisualEffectView(frame: frame)
            effect.material = .hudWindow; effect.blendingMode = .behindWindow; effect.state = .followsWindowActiveState
            effect.addSubview(original); surface = effect
            applied = "SystemMaterial"; reason = "macOS 原生磨砂材质"
        } else {
            surface = NSView(frame: frame); surface.wantsLayer = true
            surface.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
            surface.addSubview(original); applied = "Solid"; reason = "实色表面（尊重系统减少透明度与增强对比度设置）"
        }
        window.isOpaque = solid; window.backgroundColor = solid ? .windowBackgroundColor : .clear
        original.frame = CGRect(origin: .zero, size: frame.size)
        original.autoresizingMask = [.width, .height]
        surface.autoresizingMask = [.width, .height]
        wrapper = surface; window.contentView = surface
    }
    func restore() {
        if let observer = observer { NSWorkspace.shared.notificationCenter.removeObserver(observer) }
        observer = nil
        if window.contentView === wrapper {
            original.removeFromSuperview(); window.contentView = original
            window.backgroundColor = background; window.isOpaque = opaque
        }
        wrapper = nil
    }
}
var materialLeases: [String: MaterialLease] = [:]
func handleMaterial(_ request: [String: Any], operation: String) throws -> [String: Any] {
    if operation == "material.probe" { return ["state": "Available"] }
    guard let id = request["id"] as? String, !id.isEmpty else { throw NativeFailure(state: "Unsupported", message: "缺少材质租约。") }
    if operation == "material.release" {
        materialLeases.removeValue(forKey: id)?.restore(); return ["state": "Available"]
    }
    guard operation == "material.apply", let material = request["material"] as? String,
          ["Solid", "SystemMaterial", "SystemGlass"].contains(material), materialLeases[id] == nil else {
        throw NativeFailure(state: "Unsupported", message: "材质类型或租约无效。")
    }
    let window = try materialWindow(request)
    guard !materialLeases.values.contains(where: { $0.window === window }) else { throw NativeFailure(state: "Unsupported", message: "此窗口已有材质租约。") }
    let lease = try MaterialLease(window: window, requested: material)
    materialLeases[id] = lease
    return ["state": "Available", "applied": lease.applied, "reason": lease.reason]
}

func displayRows() throws -> [[String: Any]] {
    var ids = [CGDirectDisplayID](repeating: 0, count: 32)
    var count: UInt32 = 0
    guard CGGetOnlineDisplayList(32, &ids, &count) == .success, count > 0 else {
        throw NativeFailure(state: "MissingDevice", message: "没有可读取的显示器。")
    }
    return ids.prefix(Int(count)).sorted().map { id in
        let bounds = CGDisplayBounds(id)
        return ["id": String(id), "name": "显示器 " + String(id), "builtIn": CGDisplayIsBuiltin(id) != 0,
                "x": bounds.minX, "y": bounds.minY, "width": bounds.width, "height": bounds.height,
                "mirror": String(CGDisplayMirrorsDisplay(id))]
    }
}
func displaySnapshot(_ rows: [[String: Any]]) throws -> String {
    return String(data: try JSONSerialization.data(withJSONObject: rows, options: [.sortedKeys]), encoding: .utf8)!
}
func configureDisplayRows(_ rows: [[String: Any]]) throws {
    var configuration: CGDisplayConfigRef?
    guard CGBeginDisplayConfiguration(&configuration) == .success, let config = configuration else {
        throw NativeFailure(state: "PermissionRequired", message: "系统拒绝修改显示器配置。")
    }
    var committed = false
    defer { if !committed { CGCancelDisplayConfiguration(config) } }
    for row in rows {
        guard let id = UInt32(row["id"] as? String ?? ""), let mirror = UInt32(row["mirror"] as? String ?? ""),
              let x = row["x"] as? Double, let y = row["y"] as? Double,
              CGConfigureDisplayMirrorOfDisplay(config, id, mirror) == .success else {
            throw NativeFailure(state: "Unsupported", message: "显示器不支持所请求的镜像关系。")
        }
        if mirror == 0 && CGConfigureDisplayOrigin(config, id, Int32(x), Int32(y)) != .success {
            throw NativeFailure(state: "Unsupported", message: "显示器不支持所请求的位置。")
        }
    }
    guard CGCompleteDisplayConfiguration(config, .forSession) == .success else {
        throw NativeFailure(state: "Unsupported", message: "系统未应用显示器配置。")
    }
    committed = true
}
func handleDisplay(_ request: [String: Any], operation: String) throws -> [String: Any] {
    let rows = try displayRows()
    let snapshot = try displaySnapshot(rows)
    if operation == "display.read" { return ["state": "Available", "displays": rows, "snapshot": snapshot] }
    if operation == "display.probe" {
        return rows.count >= 2 ? ["state": "Available"] : ["state": "MissingDevice", "reason": "请连接第二台显示器后重新检查。"]
    }
    if operation == "display.restore" {
        guard request["expected"] as? String == snapshot, let original = request["original"] as? String,
              let data = original.data(using: .utf8), let saved = try JSONSerialization.jsonObject(with: data) as? [[String: Any]],
              Set(saved.compactMap { $0["id"] as? String }) == Set(rows.compactMap { $0["id"] as? String }) else {
            throw NativeFailure(state: "MissingDevice", message: "显示器已拔出或设置已由用户更改，已停止自动覆盖，请检查系统显示设置。")
        }
        try configureDisplayRows(saved)
        guard try displaySnapshot(displayRows()) == original else { throw NativeFailure(state: "MissingDevice", message: "系统未确认完整恢复显示配置，请检查显示设置。") }
        return ["state": "Available"]
    }
    guard operation == "display.apply", rows.count >= 2, let layout = request["layout"] as? String,
          ["Mirror", "Extend"].contains(layout) else {
        throw NativeFailure(state: "Unsupported", message: "此布局需要至少两台显示器；Mac 当前仅支持镜像与扩展。")
    }
    let main = CGMainDisplayID()
    let mainBounds = CGDisplayBounds(main)
    var right = mainBounds.maxX
    let target = rows.map { value -> [String: Any] in
        var row = value
        let id = UInt32(row["id"] as! String)!
        row["mirror"] = layout == "Mirror" && id != main ? String(main) : "0"
        if id != main && layout == "Extend" { row["x"] = right; row["y"] = mainBounds.minY; right += row["width"] as? Double ?? 0 }
        return row
    }
    do {
        try configureDisplayRows(target)
        let current = try displayRows()
        let verified = current.allSatisfy { row in
            let id = UInt32(row["id"] as! String)!
            return row["mirror"] as? String == (layout == "Mirror" && id != main ? String(main) : "0")
        }
        guard verified else { throw NativeFailure(state: "Unsupported", message: "显示器未确认请求的布局。") }
        return ["state": "Available", "original": snapshot, "snapshot": try displaySnapshot(current)]
    } catch {
        do { try configureDisplayRows(rows) } catch {
            throw NativeFailure(state: "MissingDevice", message: "显示调整与回滚均未完成，请打开系统显示设置恢复。")
        }
        throw error
    }
}
