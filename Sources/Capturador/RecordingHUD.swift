import AppKit

/// Contagem regressiva antes de gravar e controles durante a gravação.
/// As janelas são do próprio app, que o filtro da gravação exclui; por isso não aparecem no vídeo.
/// São painéis não ativadores: respondem a clique e teclado sem tirar o foco do app em uso.
enum RecordingHUD {
    private static var countdownPanel: CountdownPanel?
    private static var framePanel: NSPanel?
    private static var controls: ControlsPanel?

    static var isCountingDown: Bool { countdownPanel != nil }

    /// Mostra 3, 2, 1 sobre a área (coordenadas globais). `completion(false)` se cancelado.
    static func countdown(over rect: CGRect, seconds: Int = 3, completion: @escaping (Bool) -> Void) {
        cancelCountdown()
        let panel = CountdownPanel(rect: rect, seconds: seconds) { started in
            countdownPanel = nil
            completion(started)
        }
        countdownPanel = panel
        panel.start()
    }

    static func cancelCountdown() {
        countdownPanel?.finish(started: false)
    }

    /// Moldura vermelha em volta da área e o controle com tempo e botão Parar.
    static func showRecording(around rect: CGRect, onStop: @escaping () -> Void) {
        hide()
        let frame = NSPanel(contentRect: rect.insetBy(dx: -3, dy: -3), styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        configure(frame)
        frame.ignoresMouseEvents = true
        frame.contentView = RecordingFrameView()
        frame.orderFrontRegardless()
        framePanel = frame

        let panel = ControlsPanel(onStop: onStop)
        panel.place(around: rect)
        panel.orderFrontRegardless()
        controls = panel
    }

    static func hide() {
        framePanel?.close()
        framePanel = nil
        controls?.stopTimer()
        controls?.close()
        controls = nil
    }

    fileprivate static func configure(_ panel: NSPanel) {
        panel.isFloatingPanel = true
        panel.level = .statusBar
        panel.hidesOnDeactivate = false
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    }
}

// MARK: Contagem

private final class CountdownPanel: NSPanel {
    private var remaining: Int
    private let label = NSTextField(labelWithString: "")
    private var timer: Timer?
    private var onFinish: ((Bool) -> Void)?

    init(rect: CGRect, seconds: Int, onFinish: @escaping (Bool) -> Void) {
        remaining = seconds
        self.onFinish = onFinish
        super.init(contentRect: rect, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        RecordingHUD.configure(self)

        let root = NSView(frame: CGRect(origin: .zero, size: rect.size))
        root.wantsLayer = true
        root.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.25).cgColor

        let diameter = min(140, max(64, min(rect.width, rect.height) * 0.6))
        let badge = NSView(frame: CGRect(x: (rect.width - diameter) / 2, y: (rect.height - diameter) / 2,
                                         width: diameter, height: diameter))
        badge.wantsLayer = true
        badge.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.7).cgColor
        badge.layer?.cornerRadius = diameter / 2
        badge.layer?.borderWidth = 3
        badge.layer?.borderColor = NSColor.systemRed.cgColor
        label.font = .monospacedDigitSystemFont(ofSize: diameter * 0.5, weight: .bold)
        label.textColor = .white
        label.alignment = .center
        label.frame = CGRect(x: 0, y: (diameter - diameter * 0.62) / 2, width: diameter, height: diameter * 0.62)
        badge.addSubview(label)
        root.addSubview(badge)

        let hint = NSTextField(labelWithString: "Esc cancela")
        hint.font = .systemFont(ofSize: 12, weight: .medium)
        hint.textColor = .white
        hint.sizeToFit()
        hint.frame.origin = CGPoint(x: (rect.width - hint.frame.width) / 2, y: max(4, badge.frame.minY - hint.frame.height - 8))
        root.addSubview(hint)
        contentView = root
    }

    override var canBecomeKey: Bool { true }

    func start() {
        label.stringValue = "\(remaining)"
        orderFrontRegardless()
        makeKey()
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.tick() }
    }

    private func tick() {
        remaining -= 1
        if remaining <= 0 {
            finish(started: true)
        } else {
            label.stringValue = "\(remaining)"
        }
    }

    func finish(started: Bool) {
        timer?.invalidate()
        timer = nil
        orderOut(nil)
        let callback = onFinish
        onFinish = nil
        callback?(started)
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { finish(started: false) } else { super.keyDown(with: event) }
    }

    override func cancelOperation(_ sender: Any?) { finish(started: false) }
}

// MARK: Moldura

private final class RecordingFrameView: NSView {
    override func draw(_ dirtyRect: NSRect) {
        NSColor.systemRed.setStroke()
        let path = NSBezierPath(rect: bounds.insetBy(dx: 1.5, dy: 1.5))
        path.lineWidth = 3
        path.setLineDash([10, 6], count: 2, phase: 0)
        path.stroke()
    }
}

// MARK: Controles

private final class ControlsPanel: NSPanel {
    private let onStop: () -> Void
    private let time = NSTextField(labelWithString: "00:00")
    private let dot = NSView()
    private var timer: Timer?
    private let started = Date()

    init(onStop: @escaping () -> Void) {
        self.onStop = onStop
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        RecordingHUD.configure(self)
        hasShadow = true
        isMovableByWindowBackground = true

        dot.wantsLayer = true
        dot.layer?.backgroundColor = NSColor.systemRed.cgColor
        dot.layer?.cornerRadius = 5
        dot.translatesAutoresizingMaskIntoConstraints = false
        dot.widthAnchor.constraint(equalToConstant: 10).isActive = true
        dot.heightAnchor.constraint(equalToConstant: 10).isActive = true

        time.font = .monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
        time.textColor = .white

        let stop = StopButton(target: self, action: #selector(stopClicked))
        let stack = NSStackView(views: [dot, time, stop])
        stack.spacing = 10
        stack.alignment = .centerY
        stack.edgeInsets = NSEdgeInsets(top: 5, left: 14, bottom: 5, right: 5)
        stack.translatesAutoresizingMaskIntoConstraints = false

        let background = NSVisualEffectView()
        background.material = .hudWindow
        background.blendingMode = .behindWindow
        background.state = .active
        background.appearance = NSAppearance(named: .darkAqua)
        background.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: background.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: background.trailingAnchor),
            stack.topAnchor.constraint(equalTo: background.topAnchor),
            stack.bottomAnchor.constraint(equalTo: background.bottomAnchor)
        ])
        let size = stack.fittingSize
        background.frame = CGRect(origin: .zero, size: size)
        background.wantsLayer = true
        background.layer?.cornerRadius = size.height / 2
        background.layer?.masksToBounds = true
        contentView = background
        setContentSize(size)

        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in self?.tick() }
    }

    /// Centralizado abaixo da área; acima se não couber; dentro dela, no topo, em último caso.
    func place(around rect: CGRect) {
        let screen = NSScreen.screens.first { $0.frame.intersects(rect) } ?? NSScreen.main
        let visible = screen?.visibleFrame ?? rect.insetBy(dx: -200, dy: -200)
        let size = frame.size
        let x = min(max(rect.midX - size.width / 2, visible.minX + 8), visible.maxX - size.width - 8)
        var y = rect.minY - 12 - size.height
        if y < visible.minY + 8 {
            y = rect.maxY + 12
            if y + size.height > visible.maxY - 8 { y = min(rect.maxY, visible.maxY) - size.height - 12 }
        }
        setFrameOrigin(CGPoint(x: x, y: y))
    }

    private func tick() {
        let elapsed = Int(Date().timeIntervalSince(started))
        time.stringValue = String(format: "%02d:%02d", elapsed / 60, elapsed % 60)
        dot.alphaValue = dot.alphaValue > 0.5 ? 0.25 : 1
    }

    func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    @objc private func stopClicked() { onStop() }
}

private final class StopButton: NSButton {
    convenience init(target: AnyObject, action: Selector) {
        self.init(frame: .zero)
        self.target = target
        self.action = action
        let config = NSImage.SymbolConfiguration(pointSize: 11, weight: .bold)
        image = NSImage(systemSymbolName: "stop.fill", accessibilityDescription: "Parar")?.withSymbolConfiguration(config)
        title = "Parar"
        imagePosition = .imageLeading
        // Ícone e texto centralizados juntos, em vez do ícone preso à borda.
        imageHugsTitle = true
        isBordered = false
        contentTintColor = .white
        font = .systemFont(ofSize: 13, weight: .semibold)
        attributedTitle = NSAttributedString(string: "Parar", attributes: [
            .foregroundColor: NSColor.white, .font: NSFont.systemFont(ofSize: 13, weight: .semibold)
        ])
        wantsLayer = true
        layer?.backgroundColor = NSColor.systemRed.cgColor
        layer?.cornerRadius = 13
        toolTip = "Parar gravação"
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: 80).isActive = true
        heightAnchor.constraint(equalToConstant: 26).isActive = true
    }

    // Para com um clique, mesmo com outro app em primeiro plano.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
