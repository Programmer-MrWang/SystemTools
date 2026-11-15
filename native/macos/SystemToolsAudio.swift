import AVFoundation
import Foundation

final class MicrophoneJob {
    let lock = NSLock()
    var chunks: [Data] = []
    var failure: String?
    var stopped = false
    var engine: AVAudioEngine?

    func start() throws {
        if stopped { throw NativeFailure(state: "Disabled", message: "语音采集已取消。") }
        let audio = AVAudioEngine()
        let input = audio.inputNode
        let source = input.outputFormat(forBus: 0)
        guard source.sampleRate > 0, source.channelCount > 0,
              let target = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16000, channels: 1, interleaved: true),
              let converter = AVAudioConverter(from: source, to: target) else {
            throw NativeFailure(state: "MissingDevice", message: "默认麦克风不可用，请检查设备和系统声音设置。")
        }
        input.installTap(onBus: 0, bufferSize: 1024, format: source) { [weak self] buffer, _ in
            guard let owner = self else { return }
            let capacity = AVAudioFrameCount(ceil(Double(buffer.frameLength) * 16000 / source.sampleRate)) + 32
            guard let output = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
            var supplied = false
            var error: NSError?
            let result = converter.convert(to: output, error: &error) { _, status in
                if supplied { status.pointee = .noDataNow; return nil }
                supplied = true
                status.pointee = .haveData
                return buffer
            }
            owner.lock.withLock {
                if owner.stopped || owner.failure != nil { return }
                if result == .error || error != nil {
                    owner.failure = "麦克风数据转换失败，请重新选择设备后重试。"
                    return
                }
                guard output.frameLength > 0, let samples = output.int16ChannelData else { return }
                if owner.chunks.count >= 128 {
                    owner.failure = "语音处理未能跟上采集，已停止本次会话。"
                    return
                }
                owner.chunks.append(Data(bytes: samples[0], count: Int(output.frameLength) * 2))
            }
        }
        do { audio.prepare(); try audio.start(); engine = audio }
        catch { input.removeTap(onBus: 0); audio.stop(); throw error }
    }

    func stop() {
        lock.withLock { stopped = true; chunks.removeAll() }
        if let audio = engine {
            audio.inputNode.removeTap(onBus: 0)
            audio.stop()
            engine = nil
        }
    }

    func poll() throws -> String {
        try lock.withLock {
            if let message = failure { throw NativeFailure(state: "MissingDevice", message: message) }
            if stopped { throw NativeFailure(state: "Disabled", message: "语音采集已结束。") }
            let data = chunks.reduce(into: Data()) { $0.append($1) }
            chunks.removeAll(keepingCapacity: true)
            return data.base64EncodedString()
        }
    }
}

var microphoneJobs: [String: MicrophoneJob] = [:]
let microphoneQueue = DispatchQueue(label: "SystemTools.MicrophoneLifecycle")

func handleMicrophone(_ request: [String: Any], operation: String) throws -> [String: Any] {
    if operation == "microphone.probe" { return captureCapability(.audio, purpose: "NSMicrophoneUsageDescription") }
    if operation == "microphone.request" { return try requestCaptureAccess(.audio, purpose: "NSMicrophoneUsageDescription") }
    guard let id = request["id"] as? String else { throw NativeFailure(state: "Unsupported", message: "缺少语音会话。") }
    if operation == "microphone.prepare" {
        microphoneQueue.sync { microphoneJobs[id] = MicrophoneJob() }
        return ["state": "Available"]
    }
    guard let job = microphoneQueue.sync(execute: { microphoneJobs[id] }) else { throw NativeFailure(state: "Disabled", message: "语音会话已结束。") }
    if operation == "microphone.stop" { microphoneQueue.sync { job.stop() }; return ["state": "Available"] }
    if operation == "microphone.release" {
        microphoneQueue.sync { job.stop(); _ = microphoneJobs.removeValue(forKey: id) }
        return ["state": "Available"]
    }
    if operation == "microphone.start" {
        let state = captureCapability(.audio, purpose: "NSMicrophoneUsageDescription")
        if state["state"] as? String != "Available" { return state }
        try microphoneQueue.sync { try job.start() }
        return ["state": "Available"]
    }
    if operation == "microphone.poll" { return ["state": "Available", "pcm": try job.poll()] }
    throw NativeFailure(state: "Unsupported", message: "此语音操作不可用。")
}
