import AppKit
import UniformTypeIdentifiers

/// Copiar, salvar e codificar imagens finalizadas.
enum ImageOutput {
    static func copy(_ image: CGImage) {
        let pb = NSPasteboard.general
        pb.clearContents()
        let rep = NSBitmapImageRep(cgImage: image)
        if let png = rep.representation(using: .png, properties: [:]) {
            pb.setData(png, forType: .png)
        }
        if let tiff = rep.tiffRepresentation {
            pb.setData(tiff, forType: .tiff)
        }
    }

    static func data(_ image: CGImage, format: ImageFormat) -> Data? {
        let rep = NSBitmapImageRep(cgImage: image)
        switch format {
        case .png: return rep.representation(using: .png, properties: [:])
        case .jpg: return rep.representation(using: .jpeg, properties: [.compressionFactor: 0.9])
        }
    }

    /// Salva na pasta configurada; devolve o URL gravado.
    @discardableResult
    static func quickSave(_ image: CGImage) throws -> URL {
        let prefs = Preferences.shared
        let url = prefs.nextFileURL(extension: prefs.format.rawValue)
        guard let data = data(image, format: prefs.format) else {
            throw CocoaError(.fileWriteUnknown)
        }
        try data.write(to: url, options: .atomic)
        if prefs.copyAfterSave { copy(image) }
        return url
    }

    /// Mostra um painel "Salvar como" e grava no destino escolhido; devolve true se gravou.
    @discardableResult
    static func saveAs(_ image: CGImage) -> Bool {
        let prefs = Preferences.shared
        let panel = NSSavePanel()
        panel.directoryURL = prefs.saveFolder
        panel.nameFieldStringValue = prefs.nextFileURL(extension: prefs.format.rawValue).lastPathComponent
        panel.allowedContentTypes = [.png, .jpeg]
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return false }
        let format: ImageFormat = url.pathExtension.lowercased().hasPrefix("jp") ? .jpg : .png
        do {
            guard let data = data(image, format: format) else { throw CocoaError(.fileWriteUnknown) }
            try data.write(to: url, options: .atomic)
            return true
        } catch {
            NSAlert(error: error).runModal()
            return false
        }
    }

    static func nsImage(_ image: CGImage, pointSize: CGSize) -> NSImage {
        NSImage(cgImage: image, size: pointSize)
    }
}

enum Toast {
    /// Mensagem curta e não bloqueante perto do topo da tela.
    static func show(_ message: String) {
        guard let screen = ScreenCapturer.screenUnderMouse() else { return }
        let label = NSTextField(labelWithString: message)
        label.font = .systemFont(ofSize: 13, weight: .medium)
        label.textColor = .white
        label.sizeToFit()
        let size = CGSize(width: label.frame.width + 32, height: 36)
        let frame = CGRect(x: screen.visibleFrame.midX - size.width / 2,
                           y: screen.visibleFrame.maxY - size.height - 24,
                           width: size.width, height: size.height)
        let panel = NSPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.level = .statusBar
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.ignoresMouseEvents = true
        let bg = NSView(frame: CGRect(origin: .zero, size: size))
        bg.wantsLayer = true
        bg.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.8).cgColor
        bg.layer?.cornerRadius = 10
        label.frame.origin = CGPoint(x: 16, y: (size.height - label.frame.height) / 2)
        bg.addSubview(label)
        panel.contentView = bg
        panel.orderFrontRegardless()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { panel.orderOut(nil) }
    }
}
