import AppKit
import ScreenCaptureKit

/// Gravação de uma área da tela para .mov com ScreenCaptureKit.
final class ScreenRecorder: NSObject, SCStreamDelegate, SCStreamOutput, SCRecordingOutputDelegate {
    private var stream: SCStream?
    private var recording: SCRecordingOutput?
    private var outputURL: URL?
    private let frameQueue = DispatchQueue(label: "capturador.frames")
    var onStateChange: ((Bool) -> Void)?

    var isRecording: Bool { stream != nil }

    /// - Parameter rect: área em pontos locais da tela (origem embaixo à esquerda), como devolvida pela seleção.
    func start(snapshot: ScreenSnapshot, rect: CGRect) async throws {
        let screen = snapshot.screen
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        let ownApp = content.applications.filter { $0.processID == ProcessInfo.processInfo.processIdentifier }
        let filter = SCContentFilter(display: snapshot.display, excludingApplications: ownApp, exceptingWindows: [])

        let scale = screen.backingScaleFactor
        let config = SCStreamConfiguration()
        // sourceRect usa origem no canto superior esquerdo da tela.
        config.sourceRect = CGRect(x: rect.minX, y: screen.frame.height - rect.maxY, width: rect.width, height: rect.height)
        config.width = Int(rect.width * scale) & ~1
        config.height = Int(rect.height * scale) & ~1
        config.minimumFrameInterval = CMTime(value: 1, timescale: 30)
        config.showsCursor = true
        config.queueDepth = 6

        let url = Preferences.shared.nextFileURL(extension: "mov", prefix: "Gravação")
        let recConfig = SCRecordingOutputConfiguration()
        recConfig.outputURL = url
        recConfig.outputFileType = .mov
        recConfig.videoCodecType = .h264

        let stream = SCStream(filter: filter, configuration: config, delegate: self)
        let recording = SCRecordingOutput(configuration: recConfig, delegate: self)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: frameQueue)
        try stream.addRecordingOutput(recording)
        try await stream.startCapture()

        self.stream = stream
        self.recording = recording
        self.outputURL = url
        await MainActor.run { onStateChange?(true) }
    }

    func stop() async {
        guard let stream else { return }
        try? await stream.stopCapture()
        self.stream = nil
        self.recording = nil
        await MainActor.run { onStateChange?(false) }
    }

    // Os quadros vão direto para o arquivo via SCRecordingOutput; esta saída só mantém o stream ativo.
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {}

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        NSLog("Capturador: gravação interrompida: \(error)")
        Task { await stop() }
    }

    func recordingOutputDidFinishRecording(_ recordingOutput: SCRecordingOutput) {
        guard let url = outputURL else { return }
        DispatchQueue.main.async {
            NSWorkspace.shared.activateFileViewerSelecting([url])
            Toast.show("Gravação salva: \(url.lastPathComponent)")
        }
    }

    func recordingOutput(_ recordingOutput: SCRecordingOutput, didFailWithError error: Error) {
        DispatchQueue.main.async { NSAlert(error: error).runModal() }
    }
}
