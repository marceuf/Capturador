import AppKit

/// Coordena uma sobreposição por tela. A seleção acontece na tela onde o arraste começou;
/// o destaque de janela segue o cursor entre as telas.
final class SelectionOverlay {
    private static var active: SelectionOverlay?

    private var windows: [OverlayWindow] = []
    private var completion: ((ScreenSnapshot, CGRect)?) -> Void

    /// - Parameter hint: texto mostrado no topo de cada tela (por exemplo, "Selecione a área para OCR").
    static func select(on snapshots: [ScreenSnapshot], hint: String,
                       completion: @escaping ((ScreenSnapshot, CGRect)?) -> Void) {
        guard active == nil else { return }
        let overlay = SelectionOverlay(completion: completion)
        active = overlay
        overlay.windows = snapshots.map { OverlayWindow(snapshot: $0, hint: hint, coordinator: overlay) }
        NSApp.activate(ignoringOtherApps: true)
        for window in overlay.windows { window.orderFrontRegardless() }
        overlay.focusWindowUnderMouse()
        NSCursor.crosshair.push()
    }

    private init(completion: @escaping ((ScreenSnapshot, CGRect)?) -> Void) {
        self.completion = completion
    }

    fileprivate func mouseMoved() {
        windows.forEach { $0.selectionView.refreshHover() }
        focusWindowUnderMouse()
    }

    /// Mantém como janela principal a sobreposição sob o cursor, para que Esc/Enter cheguem a ela.
    private func focusWindowUnderMouse() {
        let mouse = NSEvent.mouseLocation
        guard let target = windows.first(where: { NSMouseInRect(mouse, $0.frame, false) }) ?? windows.first,
              !target.isKeyWindow else { return }
        target.makeKeyAndOrderFront(nil)
        target.makeFirstResponder(target.selectionView)
    }

    /// Enter: captura a tela inteira sob o cursor.
    fileprivate func selectScreenUnderMouse() {
        let mouse = NSEvent.mouseLocation
        guard let window = windows.first(where: { NSMouseInRect(mouse, $0.frame, false) }) ?? windows.first else { return }
        finish((window.snapshot, CGRect(origin: .zero, size: window.frame.size)))
    }

    fileprivate func finish(_ result: (ScreenSnapshot, CGRect)?) {
        guard SelectionOverlay.active === self else { return }
        SelectionOverlay.active = nil
        NSCursor.pop()
        for window in windows {
            window.orderOut(nil)
            window.close()
        }
        windows.removeAll()
        completion(result)
    }
}

private final class OverlayWindow: NSWindow {
    let snapshot: ScreenSnapshot
    let selectionView: SelectionView

    init(snapshot: ScreenSnapshot, hint: String, coordinator: SelectionOverlay) {
        self.snapshot = snapshot
        self.selectionView = SelectionView(snapshot: snapshot, hint: hint, coordinator: coordinator)
        super.init(contentRect: snapshot.screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        level = .screenSaver
        isOpaque = true
        hasShadow = false
        isReleasedWhenClosed = false
        acceptsMouseMovedEvents = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        setFrame(snapshot.screen.frame, display: false)
        contentView = selectionView
    }

    override var canBecomeKey: Bool { true }
}

private final class SelectionView: NSView {
    private unowned let coordinator: SelectionOverlay
    private let snapshot: ScreenSnapshot
    private let hint: String
    private var dragStart: CGPoint?
    private var selection: CGRect?
    private var hoveredWindow: CGRect?

    init(snapshot: ScreenSnapshot, hint: String, coordinator: SelectionOverlay) {
        self.snapshot = snapshot
        self.hint = hint
        self.coordinator = coordinator
        super.init(frame: CGRect(origin: .zero, size: snapshot.screen.frame.size))
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseMoved, .activeAlways, .inVisibleRect],
                                       owner: self, userInfo: nil))
        hoveredWindow = window(at: localMouse())
    }

    required init?(coder: NSCoder) { fatalError() }

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    /// Posição do cursor nesta tela, ou nil se ele está em outra.
    private func localMouse() -> CGPoint? {
        let m = NSEvent.mouseLocation
        guard NSMouseInRect(m, snapshot.screen.frame, false) else { return nil }
        return CGPoint(x: m.x - snapshot.screen.frame.minX, y: m.y - snapshot.screen.frame.minY)
    }

    private func window(at point: CGPoint?) -> CGRect? {
        guard let point else { return nil }
        return snapshot.windowRects.first { $0.contains(point) }
    }

    func refreshHover() {
        guard dragStart == nil else { return }
        let hovered = window(at: localMouse())
        if hovered != hoveredWindow {
            hoveredWindow = hovered
            needsDisplay = true
        }
    }

    override func mouseMoved(with event: NSEvent) { coordinator.mouseMoved() }

    override func mouseDown(with event: NSEvent) {
        dragStart = convert(event.locationInWindow, from: nil)
        selection = nil
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = dragStart else { return }
        // A seleção fica limitada à tela em que o arraste começou.
        var p = convert(event.locationInWindow, from: nil)
        p.x = min(max(p.x, 0), bounds.maxX)
        p.y = min(max(p.y, 0), bounds.maxY)
        selection = CGRect(x: min(start.x, p.x), y: min(start.y, p.y),
                           width: abs(p.x - start.x), height: abs(p.y - start.y))
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        defer { dragStart = nil }
        if let sel = selection, sel.width >= 4, sel.height >= 4 {
            coordinator.finish((snapshot, sel.integral))
        } else if let win = hoveredWindow {
            // Clique simples captura a janela sob o cursor.
            coordinator.finish((snapshot, win.integral))
        } else {
            coordinator.finish((snapshot, bounds))
        }
    }

    override func rightMouseDown(with event: NSEvent) { coordinator.finish(nil) }

    override func keyDown(with event: NSEvent) {
        switch Int(event.keyCode) {
        case 53: coordinator.finish(nil)                 // Esc
        case 36, 76: coordinator.selectScreenUnderMouse() // Enter: tela inteira
        default: super.keyDown(with: event)
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.draw(snapshot.image, in: bounds)

        let highlight = selection ?? (dragStart == nil ? hoveredWindow : nil)
        let path = CGMutablePath()
        path.addRect(bounds)
        if let highlight { path.addRect(highlight) }
        ctx.addPath(path)
        ctx.setFillColor(NSColor.black.withAlphaComponent(0.45).cgColor)
        ctx.fillPath(using: .evenOdd)

        if let highlight {
            ctx.setStrokeColor(NSColor.systemBlue.cgColor)
            ctx.setLineWidth(2)
            ctx.stroke(highlight.insetBy(dx: -1, dy: -1))
            drawLabel("\(Int(highlight.width)) × \(Int(highlight.height))",
                      at: CGPoint(x: highlight.minX, y: highlight.maxY + 6))
        }
        drawLabel(hint + "  ·  arraste para selecionar, clique numa janela, Enter = tela inteira, Esc = cancelar",
                  at: CGPoint(x: 16, y: bounds.maxY - 34))
    }

    private func drawLabel(_ text: String, at point: CGPoint) {
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 12, weight: .medium),
            .foregroundColor: NSColor.white
        ]
        let str = NSAttributedString(string: text, attributes: attrs)
        let size = str.size()
        var origin = point
        origin.x = min(max(origin.x, 4), bounds.maxX - size.width - 14)
        origin.y = min(origin.y, bounds.maxY - size.height - 10)
        let bg = CGRect(x: origin.x, y: origin.y, width: size.width + 10, height: size.height + 4)
        NSColor.black.withAlphaComponent(0.7).setFill()
        NSBezierPath(roundedRect: bg, xRadius: 4, yRadius: 4).fill()
        str.draw(at: CGPoint(x: origin.x + 5, y: origin.y + 2))
    }
}
