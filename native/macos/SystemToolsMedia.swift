import AppKit
import AVFoundation
import Foundation

func captureCapability(_ kind: AVMediaType, purpose: String) -> [String: Any] {
    guard let description = Bundle.main.object(forInfoDictionaryKey: purpose) as? String,
          !description.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
        return ["state": "MissingDependency", "reason": "当前宿主未声明此设备用途，请更新完整 ClassIsland 应用。"]
    }
    guard AVCaptureDevice.authorizationStatus(for: kind) == .authorized else {
        return ["state": "PermissionRequired", "reason": "请通过此页申请权限，或在系统设置中允许 ClassIsland 访问设备。"]
    }
    if kind == .video && AVCaptureDevice.devices(for: .video).isEmpty {
        return ["state": "MissingDevice", "reason": "没有可用摄像头。"]
    }
    return ["state": "Available", "reason": "设备权限已就绪。"]
}

func requestCaptureAccess(_ kind: AVMediaType, purpose: String) throws -> [String: Any] {
    let capability = captureCapability(kind, purpose: purpose)
    if capability["state"] as? String == "MissingDependency" { return capability }
    if Thread.isMainThread { throw NativeFailure(state: "Unsupported", message: "设备授权必须异步进行。") }
    let completed = DispatchSemaphore(value: 0)
    AVCaptureDevice.requestAccess(for: kind) { _ in completed.signal() }
    guard completed.wait(timeout: .now() + 60) == .success else {
        throw NativeFailure(state: "PermissionRequired", message: "授权尚未完成，请在系统提示中选择后重新检查。")
    }
    return captureCapability(kind, purpose: purpose)
}

final class CameraJob: NSObject, AVCapturePhotoCaptureDelegate {
    let completed = DispatchSemaphore(value: 0)
    let lock = NSLock()
    var cancelled = false
    var failure: Error?
    var destination = ""

    func cancel() {
        lock.withLock { cancelled = true }
        completed.signal()
    }

    func photoOutput(_ output: AVCapturePhotoOutput, didFinishProcessingPhoto photo: AVCapturePhoto, error: Error?) {
        defer { completed.signal() }
        lock.withLock {
            if cancelled { return }
            do {
                if let error = error { throw error }
                guard let data = photo.fileDataRepresentation(), let image = NSBitmapImageRep(data: data),
                      let png = image.representation(using: .png, properties: [:]) else {
                    throw NativeFailure(state: "MissingDevice", message: "摄像头未返回有效图片。")
                }
                try png.write(to: URL(fileURLWithPath: destination), options: .atomic)
            } catch { failure = error }
        }
    }
}

let cameraJobsLock = NSLock()
var cameraJobs: [String: CameraJob] = [:]

func handleCamera(_ request: [String: Any], operation: String) throws -> [String: Any] {
    if operation == "camera.probe" { return captureCapability(.video, purpose: "NSCameraUsageDescription") }
    if operation == "camera.request" { return try requestCaptureAccess(.video, purpose: "NSCameraUsageDescription") }
    if operation == "camera.devices" {
        return ["state": "Available", "devices": AVCaptureDevice.devices(for: .video).map { ["id": $0.uniqueID, "name": $0.localizedName] }]
    }
    guard let id = request["id"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少采集会话。") }
    if operation == "camera.prepare" {
        cameraJobsLock.withLock { cameraJobs[id] = CameraJob() }
        return ["state": "Available"]
    }
    guard let job = cameraJobsLock.withLock({ cameraJobs[id] }) else { throw NativeFailure(state: "Unsupported", message: "采集会话已结束。") }
    if operation == "camera.cancel" { job.cancel(); return ["state": "Available"] }
    if operation == "camera.release" {
        cameraJobsLock.withLock { _ = cameraJobs.removeValue(forKey: id) }
        return ["state": "Available"]
    }
    guard operation == "camera.capture", !Thread.isMainThread else { throw NativeFailure(state: "Unsupported", message: "摄像头采集必须在后台执行。") }
    let capability = captureCapability(.video, purpose: "NSCameraUsageDescription")
    if capability["state"] as? String != "Available" { return capability }
    if job.lock.withLock({ job.cancelled }) { throw NativeFailure(state: "Disabled", message: "采集已取消。") }
    guard let deviceID = request["device"] as? String,
          let device = AVCaptureDevice.devices(for: .video).first(where: { $0.uniqueID == deviceID }),
          let path = request["path"] as? String else { throw NativeFailure(state: "MissingDevice", message: "所选摄像头已断开，请重新选择设备。") }
    job.destination = path
    let session = AVCaptureSession()
    session.sessionPreset = .photo
    let input = try AVCaptureDeviceInput(device: device)
    let output = AVCapturePhotoOutput()
    guard session.canAddInput(input), session.canAddOutput(output) else { throw NativeFailure(state: "MissingDevice", message: "摄像头被占用或不支持照片采集。") }
    session.addInput(input)
    session.addOutput(output)
    session.startRunning()
    defer { session.stopRunning() }
    if job.lock.withLock({ job.cancelled }) { throw NativeFailure(state: "Disabled", message: "采集已取消。") }
    output.capturePhoto(with: AVCapturePhotoSettings(), delegate: job)
    if job.completed.wait(timeout: .now() + 20) == .timedOut {
        job.cancel()
        throw NativeFailure(state: "MissingDevice", message: "摄像头采集超时，请检查设备和权限。")
    }
    try job.lock.withLock {
        if job.cancelled { throw NativeFailure(state: "Disabled", message: "采集已取消。") }
        if let error = job.failure { throw error }
    }
    return ["state": "Available"]
}
