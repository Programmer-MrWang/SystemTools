import AppKit

var checks = 0
func check(_ condition: Bool, _ name: String) {
    precondition(condition, name)
    checks += 1
    print("PASS \(checks): \(name)")
}

check(wallpaperFillComponents(nil) == [0, 0, 0], "缺省壁纸填充色不读取灰阶 RGB 分量")
for color in [NSColor.black, NSColor.white, NSColor(deviceWhite: 0.5, alpha: 1)] {
    let values = wallpaperFillComponents(color)
    check(values.count == 3 && values.allSatisfy { $0.isFinite && (0...1).contains($0) }, "灰阶填充色转换为有限 RGB")
}
let values = wallpaperFillComponents(NSColor(srgbRed: 0.25, green: 0.5, blue: 0.75, alpha: 1))
check(zip(values, [0.25, 0.5, 0.75]).allSatisfy { abs($0 - $1) < 0.001 }, "RGB 填充色保留分量")
let pattern = NSColor(patternImage: NSImage(size: NSSize(width: 1, height: 1)))
check(wallpaperFillComponents(pattern) == [0, 0, 0], "不可转换的图案色使用数值回退")
if !CommandLine.arguments.contains("--headless") {
    _ = NSApplication.shared
    let rows = try wallpaperRows()
    check(!rows.isEmpty && rows.allSatisfy { ($0["fill"] as? [Double])?.count == 3 }, "真实桌面壁纸快照只读查询不崩溃")
}
do {
    try wallpaperWriteConfirmed([])
    preconditionFailure("主线程壁纸等待被接受")
} catch let error as NativeFailure {
    check(error.state == "Unsupported", "壁纸确认拒绝阻塞 AppKit 主线程且不执行写入")
}
do {
    try windowExecute("最大化")
    preconditionFailure("主线程窗口等待被接受")
} catch let error as NativeFailure {
    check(error.state == "Unsupported", "窗口确认拒绝阻塞 AppKit 主线程且不调整窗口")
}
print("Native M4 checks: \(checks)")

let cameraID = UUID().uuidString
_ = try handleCamera(["id": cameraID], operation: "camera.prepare")
_ = try handleCamera(["id": cameraID], operation: "camera.cancel")
check(cameraJobsLock.withLock { cameraJobs[cameraID]?.cancelled == true }, "相机任务取消状态保存且未打开设备")
_ = try handleCamera(["id": cameraID], operation: "camera.release")
check(cameraJobsLock.withLock { cameraJobs[cameraID] == nil }, "相机任务释放后移除")
let microphoneID = UUID().uuidString
_ = try handleMicrophone(["id": microphoneID], operation: "microphone.prepare")
_ = try handleMicrophone(["id": microphoneID], operation: "microphone.stop")
let microphone = microphoneQueue.sync { microphoneJobs[microphoneID]! }
do {
    try microphoneQueue.sync { try microphone.start() }
    preconditionFailure("已取消麦克风任务重新开始")
} catch let error as NativeFailure {
    check(error.state == "Disabled" && microphone.engine == nil, "已停止麦克风任务不创建音频引擎")
}
_ = try handleMicrophone(["id": microphoneID], operation: "microphone.release")
check(microphoneQueue.sync { microphoneJobs[microphoneID] == nil }, "麦克风任务释放后移除")
let authenticationID = UUID().uuidString
_ = try handleAuthentication(["id": authenticationID], operation: "authentication.prepare")
let authentication = authenticationJobsLock.withLock { authenticationJobs[authenticationID]! }
authentication.verified = true
_ = try handleAuthentication(["id": authenticationID], operation: "authentication.cancel")
check(authentication.lock.withLock { authentication.cancelled && !authentication.verified }, "取消系统认证清除成功状态并使上下文失效")
_ = try handleAuthentication(["id": authenticationID], operation: "authentication.release")
check(authenticationJobsLock.withLock { authenticationJobs[authenticationID] == nil }, "认证任务释放后移除")
print("Native M5 lifecycle checks complete: \(checks)")
