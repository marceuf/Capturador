import AppKit

/// Captura temporária: a imagem flutua sem moldura e a cápsula de ferramentas fica abaixo.
/// Nada é gravado em disco até o usuário salvar; Esc, ⌘W ou Descartar fecham sem salvar.
/// É um painel não ativador: recebe o teclado mesmo quando o macOS não deixa o app
/// (acessório, chamado por atalho global) tomar o foco do app que estava na frente.
final class EditorWindow: NSPanel {
    private static var open: Set<EditorWindow> = []

    let canvas: CanvasView
    private let pointSize: CGSize
    private var capsule: ToolCapsule!
    private var closing = false

    /// Margem transparente em volta da imagem, onde a sombra é desenhada.
    private static let shadowPad: CGFloat = 28

    /// Área da imagem na tela, sem a margem da sombra.
    private var imageFrame: CGRect { frame.insetBy(dx: Self.shadowPad, dy: Self.shadowPad) }

    /// - Parameter rect: área capturada em coordenadas globais; a imagem aparece exatamente sobre ela.
    static func show(image: CGImage, pointSize: CGSize, near rect: CGRect? = nil) {
        let editor = EditorWindow(image: image, pointSize: pointSize, near: rect)
        open.insert(editor)
        NSApp.activate(ignoringOtherApps: true)
        editor.makeKeyAndOrderFront(nil)
        editor.makeFirstResponder(editor.canvas)
        editor.showCapsule()
    }

    private init(image: CGImage, pointSize: CGSize, near rect: CGRect?) {
        self.canvas = CanvasView(image: image)
        self.pointSize = pointSize

        let screen = rect.flatMap { r in NSScreen.screens.first { $0.frame.intersects(r) } }
            ?? ScreenCapturer.screenUnderMouse() ?? NSScreen.main
        let visible = screen?.visibleFrame ?? CGRect(origin: rect?.origin ?? .zero, size: pointSize)
        let capsuleRoom: CGFloat = 70
        let fit = min(1, visible.width * 0.85 / pointSize.width, (visible.height * 0.85 - capsuleRoom) / pointSize.height)
        let size = CGSize(width: max(round(pointSize.width * fit), 1), height: max(round(pointSize.height * fit), 1))
        var frame: CGRect
        if let rect, fit == 1 {
            frame = CGRect(origin: rect.origin, size: size)
        } else {
            frame = CGRect(x: visible.midX - size.width / 2, y: visible.midY - size.height / 2 + capsuleRoom / 2,
                           width: size.width, height: size.height)
        }
        frame.origin.x = min(max(frame.minX, visible.minX), visible.maxX - frame.width)
        frame.origin.y = min(max(frame.minY, visible.minY), visible.maxY - frame.height)

        let pad = Self.shadowPad
        super.init(contentRect: frame.insetBy(dx: -pad, dy: -pad), styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        hidesOnDeactivate = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        level = .floating
        isReleasedWhenClosed = false
        collectionBehavior = [.fullScreenAuxiliary]
        setAccessibilityTitle("Captura temporária — \(Int(pointSize.width)) × \(Int(pointSize.height))")

        // Sombra própria, mais forte e macia que a da janela, para destacar a captura do fundo.
        let root = NSView(frame: CGRect(origin: .zero, size: CGSize(width: size.width + 2 * pad, height: size.height + 2 * pad)))
        let imageRect = CGRect(x: pad, y: pad, width: size.width, height: size.height)
        root.wantsLayer = true
        let shadowHost = NSView(frame: imageRect)
        shadowHost.wantsLayer = true
        // A sombra vai pela propriedade da view; o AppKit sobrescreve a configurada só na camada.
        let shadow = NSShadow()
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.75)
        shadow.shadowBlurRadius = 22
        shadow.shadowOffset = CGSize(width: 0, height: -6)
        shadowHost.shadow = shadow
        shadowHost.layer?.cornerRadius = 7
        shadowHost.layer?.backgroundColor = NSColor.black.cgColor
        // Contorno escuro externo: destaca a captura também sobre fundos claros.
        shadowHost.layer?.borderWidth = 1
        shadowHost.layer?.borderColor = NSColor.black.withAlphaComponent(0.35).cgColor
        shadowHost.autoresizingMask = [.width, .height]
        root.addSubview(shadowHost)

        let container = NSView(frame: imageRect)
        container.wantsLayer = true
        container.layer?.cornerRadius = 7
        container.layer?.masksToBounds = true
        container.layer?.borderWidth = 1.5
        container.layer?.borderColor = NSColor.white.withAlphaComponent(0.45).cgColor
        container.autoresizingMask = [.width, .height]
        canvas.frame = container.bounds
        canvas.autoresizingMask = [.width, .height]
        container.addSubview(canvas)
        root.addSubview(container)
        contentView = root

        capsule = ToolCapsule(editor: self)
        canvas.onStyleChange = { [weak self] in self?.capsule.refresh() }

        let center = NotificationCenter.default
        center.addObserver(self, selector: #selector(activeChanged), name: NSWindow.didBecomeMainNotification, object: self)
        center.addObserver(self, selector: #selector(activeChanged), name: NSWindow.didResignMainNotification, object: self)
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    private func showCapsule() {
        let cs = capsule.frame.size
        let visible = (screen ?? NSScreen.main)?.visibleFrame ?? frame
        let gap: CGFloat = 10
        let image = imageFrame
        let x = min(max(image.midX - cs.width / 2, visible.minX + 8), visible.maxX - cs.width - 8)
        var y = image.minY - gap - cs.height
        if y < visible.minY + 8 {
            y = image.maxY + gap
            if y + cs.height > visible.maxY - 8 { y = visible.minY + 8 }
        }
        capsule.setFrameOrigin(CGPoint(x: x, y: y))
        addChildWindow(capsule, ordered: .above)
        capsule.orderFront(nil)
    }

    @objc private func activeChanged() {
        capsule.setHighlighted(isMainWindow)
    }

    override func close() {
        canvas.commitText()
        NotificationCenter.default.removeObserver(self)
        removeChildWindow(capsule)
        capsule.close()
        super.close()
        EditorWindow.open.remove(self)
    }

    // MARK: Ações

    @objc func undoAction() { canvas.undo() }
    @objc func redoAction() { canvas.redo() }

    /// Copia e encerra a captura: a imagem voa para o canto inferior da tela e se desfaz.
    /// Imagem final com as anotações. Se não puder ser gerada, avisa e não entrega nada:
    /// a captura sem as anotações poderia expor o que foi escondido.
    private func rendered() -> CGImage? {
        if let image = canvas.renderedImage() { return image }
        let alert = NSAlert()
        alert.messageText = "Não foi possível gerar a imagem"
        alert.informativeText = "A captura continua aberta. Tente de novo ou reduza a área capturada."
        alert.runModal()
        return nil
    }

    @objc func copyAction() {
        guard !closing, let image = rendered() else { return }
        ImageOutput.copy(image)
        closing = true
        if let screen = screen ?? NSScreen.main { FlyAway.run(image: image, from: imageFrame, on: screen) }
        close()
        Toast.show("Copiado — ⌘V para colar")
    }

    @objc func saveAction() {
        guard let image = rendered() else { return }
        do {
            let url = try ImageOutput.quickSave(image)
            Toast.show("Salvo em \(url.deletingLastPathComponent().lastPathComponent)/\(url.lastPathComponent)")
            close()
        } catch {
            NSAlert(error: error).runModal()
        }
    }

    @objc func saveAsAction() {
        guard let image = rendered() else { return }
        if ImageOutput.saveAs(image) { close() }
    }

    @objc func discardAction() { close() }

    @objc func pinAction() {
        guard let image = rendered() else { return }
        PinWindow.show(image: image, pointSize: pointSize)
    }

    @objc func shareAction() {
        guard let rendered = rendered() else { return }
        let image = ImageOutput.nsImage(rendered, pointSize: pointSize)
        let anchor = capsule.shareAnchor
        NSSharingServicePicker(items: [image]).show(relativeTo: anchor.bounds, of: anchor, preferredEdge: .minY)
    }

    @objc func ocrAction() {
        guard let image = rendered() else { return }
        OCRService.recognizeAndShow(image)
    }

    // MARK: Teclado

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        // Durante a digitação, ⌘C/⌘V/⌘Z pertencem ao texto.
        if canvas.isEditingText { return super.performKeyEquivalent(with: event) }
        let flags = event.modifierFlags.intersection([.command, .shift, .option, .control])
        let key = event.charactersIgnoringModifiers?.lowercased()
        switch (flags, key) {
        case (.command, "c"): copyAction()
        case (.command, "s"): saveAction()
        case ([.command, .shift], "s"): saveAsAction()
        case (.command, "z"): undoAction()
        case ([.command, .shift], "z"): redoAction()
        case (.command, "w"): discardAction()
        case (.command, _) where event.keyCode == 51: discardAction()   // ⌘⌫
        default: return super.performKeyEquivalent(with: event)
        }
        return true
    }

    override func keyDown(with event: NSEvent) {
        let flags = event.modifierFlags.intersection([.command, .shift, .option, .control])
        switch Int(event.keyCode) {
        case 53:                                         // Esc
            if !canvas.cancelText() && !canvas.deselect() { discardAction() }
        case 36, 76:                                     // Enter
            copyAction()
        case 51, 117:                                    // ⌫ / ⌦
            if !canvas.deleteSelection() { NSSound.beep() }
        default:
            guard flags.isEmpty, let c = event.charactersIgnoringModifiers?.lowercased() else {
                super.keyDown(with: event)
                return
            }
            if let n = Int(c), (1...Tool.allCases.count).contains(n) {
                canvas.tool = Tool.allCases[n - 1]
            } else if let tool = Tool.allCases.first(where: { $0.key == c }) {
                canvas.tool = tool
            } else {
                super.keyDown(with: event)
            }
        }
    }
}

/// Animação de saída ao copiar: a captura encolhe em direção ao canto inferior direito e some.
private enum FlyAway {
    private static var windows: Set<NSWindow> = []

    static func run(image: CGImage, from rect: CGRect, on screen: NSScreen) {
        let window = NSWindow(contentRect: rect, styleMask: [.borderless], backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = true
        window.level = .floating
        window.ignoresMouseEvents = true
        window.isReleasedWhenClosed = false
        let view = NSImageView(frame: CGRect(origin: .zero, size: rect.size))
        view.image = NSImage(cgImage: image, size: rect.size)
        view.imageScaling = .scaleAxesIndependently
        view.autoresizingMask = [.width, .height]
        view.wantsLayer = true
        view.layer?.cornerRadius = 7
        view.layer?.masksToBounds = true
        window.contentView = view
        window.orderFrontRegardless()
        windows.insert(window)

        let visible = screen.visibleFrame
        let width = min(rect.width, 36)
        let height = max(width * rect.height / max(rect.width, 1), 4)
        let target = CGRect(x: visible.maxX - width - 18, y: visible.minY + 18, width: width, height: height)
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.45
            context.timingFunction = CAMediaTimingFunction(name: .easeIn)
            window.animator().setFrame(target, display: true)
            window.animator().alphaValue = 0
        }, completionHandler: {
            window.orderOut(nil)
            windows.remove(window)
        })
    }
}
