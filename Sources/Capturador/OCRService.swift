import AppKit
import Vision

/// Reconhecimento de texto local com o framework Vision (sem internet).
enum OCRService {
    /// Síncrono: `perform` só retorna depois de processar o pedido, então o resultado é lido logo
    /// em seguida. Uma falha do Vision vira um único erro lançado.
    static func recognize(_ image: CGImage, tableMode: Bool) throws -> String {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.automaticallyDetectsLanguage = true
        request.recognitionLanguages = ["pt-BR", "en-US", "es-ES"]
        try VNImageRequestHandler(cgImage: image).perform([request])
        return layout(request.results ?? [], tableMode: tableMode)
    }

    /// Agrupa os trechos em linhas (de cima para baixo). No modo tabela, separa colunas com TAB,
    /// o que cola corretamente em planilhas.
    private static func layout(_ observations: [VNRecognizedTextObservation], tableMode: Bool) -> String {
        let items = observations.compactMap { obs -> (box: CGRect, text: String)? in
            guard let text = obs.topCandidates(1).first?.string else { return nil }
            return (obs.boundingBox, text)
        }
        guard !items.isEmpty else { return "" }
        let sorted = items.sorted { $0.box.midY > $1.box.midY }
        var rows: [[(box: CGRect, text: String)]] = []
        for item in sorted {
            if let last = rows.last?.first, abs(last.box.midY - item.box.midY) < min(last.box.height, item.box.height) * 0.5 {
                rows[rows.count - 1].append(item)
            } else {
                rows.append([item])
            }
        }
        let separator = tableMode ? "\t" : " "
        return rows.map { row in
            row.sorted { $0.box.minX < $1.box.minX }.map(\.text).joined(separator: separator)
        }.joined(separator: "\n")
    }

    static func recognizeAndShow(_ image: CGImage) {
        OCRWindow.show(image: image)
    }
}

/// Janela com o texto reconhecido, editável, com cópia e modo tabela.
final class OCRWindow: NSWindow {
    private static var open: Set<OCRWindow> = []
    private let image: CGImage
    private let textView = NSTextView()
    private let tableToggle = NSButton(checkboxWithTitle: "Otimizar para tabelas", target: nil, action: nil)
    private let status = NSTextField(labelWithString: "Reconhecendo…")

    static func show(image: CGImage) {
        let w = OCRWindow(image: image)
        open.insert(w)
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
        w.run(copyWhenDone: true)
    }

    private init(image: CGImage) {
        self.image = image
        super.init(contentRect: CGRect(x: 0, y: 0, width: 520, height: 380),
                   styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        title = "Texto extraído (OCR)"
        isReleasedWhenClosed = false
        level = .floating
        center()

        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        textView.isRichText = false
        textView.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
        textView.autoresizingMask = [.width]
        textView.isVerticallyResizable = true
        textView.textContainer?.widthTracksTextView = true
        scroll.documentView = textView

        tableToggle.target = self
        tableToggle.action = #selector(rerun)
        let copy = NSButton(title: "Copiar (⌘↩)", target: self, action: #selector(copyText))
        copy.keyEquivalent = "\r"
        copy.keyEquivalentModifierMask = .command
        copy.bezelStyle = .rounded

        let bottom = NSStackView(views: [tableToggle, status, NSView(), copy])
        let stack = NSStackView(views: [scroll, bottom])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 12, bottom: 12, right: 12)
        scroll.translatesAutoresizingMaskIntoConstraints = false
        bottom.translatesAutoresizingMaskIntoConstraints = false
        stack.addConstraints([
            scroll.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -24),
            bottom.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -24)
        ])
        contentView = stack
    }

    @objc private func rerun() { run(copyWhenDone: false) }

    private func run(copyWhenDone: Bool) {
        status.stringValue = "Reconhecendo…"
        let table = tableToggle.state == .on
        let image = self.image
        Task { @MainActor in
            do {
                let text = try await Task.detached { try OCRService.recognize(image, tableMode: table) }.value
                textView.string = text
                if text.isEmpty {
                    status.stringValue = "Nenhum texto encontrado."
                } else if copyWhenDone {
                    copyText()
                } else {
                    status.stringValue = "\(text.count) caracteres"
                }
            } catch {
                status.stringValue = "Falha no reconhecimento: \(error.localizedDescription)"
            }
        }
    }

    @objc private func copyText() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(textView.string, forType: .string)
        status.stringValue = "Copiado para a área de transferência"
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { close() } else { super.keyDown(with: event) }
    }

    override func close() {
        super.close()
        OCRWindow.open.remove(self)
    }
}
