import Foundation
import LocalAuthentication

final class AuthenticationJob {
    let context = LAContext()
    let lock = NSLock()
    let completed = DispatchSemaphore(value: 0)
    var cancelled = false
    var verified = false
    func cancel() {
        lock.withLock { cancelled = true; verified = false }
        context.invalidate()
        completed.signal()
    }
}

let authenticationJobsLock = NSLock()
var authenticationJobs: [String: AuthenticationJob] = [:]

func handleAuthentication(_ request: [String: Any], operation: String) throws -> [String: Any] {
    if operation == "authentication.probe" {
        var error: NSError?
        return LAContext().canEvaluatePolicy(.deviceOwnerAuthentication, error: &error)
            ? ["state": "Available"] : ["state": "MissingDevice", "reason": "系统认证当前不可用，请先配置系统登录认证。"]
    }
    guard let id = request["id"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少认证会话。") }
    if operation == "authentication.prepare" {
        authenticationJobsLock.withLock { authenticationJobs[id] = AuthenticationJob() }
        return ["state": "Available"]
    }
    guard let job = authenticationJobsLock.withLock({ authenticationJobs[id] }) else { throw NativeFailure(state: "Disabled", message: "认证会话已结束。") }
    if operation == "authentication.cancel" { job.cancel(); return ["state": "Available"] }
    if operation == "authentication.release" {
        job.cancel()
        authenticationJobsLock.withLock { _ = authenticationJobs.removeValue(forKey: id) }
        return ["state": "Available"]
    }
    guard operation == "authentication.verify", !Thread.isMainThread,
          let reason = request["reason"] as? String, !reason.isEmpty else {
        throw NativeFailure(state: "Unsupported", message: "请从认证窗口异步发起系统认证。")
    }
    if job.lock.withLock({ job.cancelled }) { return ["state": "Available", "verified": false] }
    job.context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason) { success, _ in
        job.lock.withLock { job.verified = success && !job.cancelled }
        job.completed.signal()
    }
    if job.lock.withLock({ job.cancelled }) { job.context.invalidate() }
    if job.completed.wait(timeout: .now() + 120) == .timedOut { job.cancel() }
    return ["state": "Available", "verified": job.lock.withLock { job.verified && !job.cancelled }]
}
