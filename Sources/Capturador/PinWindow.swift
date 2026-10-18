import AppKit

/// Captura fixada: janela flutuante sempre visível, arrastável, com zoom pela roda do mouse.
final class PinWindow: NSPanel {
    private static var open: Set<PinWindow> = []
    private let image: CGImage
    private let baseSize: CGSize
    private var zoom: CGFloat = 1

    static func show(image: CGImage, pointSize: CGSize) {
        let pin = PinWindow(image: image, pointSize: pointSize)
        open.insert(pin)
        pin.orderFrontRegardless()
        pin.makeKey()
    }

    private init(image: CGImage, pointSize: CGSize) {
        self.image = image
        self.baseSize = pointSize
        let origin = NSEvent.mouseLocation
        super.init(contentRect: CGRect(x: origin.x - pointSize.width / 2, y: origin.y - pointSize.height / 2,
                                       width: pointSize.width, height: pointSize.height),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        level = .floating
        isMovableByWindowBackground = true
        hasShadow = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let view = PinView(frame: CGRect(origin: .zero, size: pointSize))
        view.image = image
        view.toolTip = "Arraste para mover · roda do mouse = zoom · duplo clique ou Esc = fechar · ⌘C = copiar"
        contentView = view
    }

    override var canBecomeKey: Bool { true }

    override func scrollWheel(with event: NSEvent) {
        zoom = min(4, max(0.2, zoom * (1 + event.scrollingDeltaY * 0.01)))
        let size = CGSize(width: baseSize.width * zoom, height: baseSize.height * zoom)
        let center = CGPoint(x: frame.midX, y: frame.midY)
        setFrame(CGRect(x: center.x - size.width / 2, y: center.y - size.height / 2,
                        width: size.width, height: size.height), display: true)
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { close() } else { super.keyDown(with: event) }
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if event.modifierFlags.contains(.command), event.charactersIgnoringModifiers == "c" {
            ImageOutput.copy(image)
            Toast.show("Copiado")
            return true
        }
        return super.performKeyEquivalent(with: event)
    }

    override func close() {
        super.close()
        PinWindow.open.remove(self)
    }
}

private final class PinView: NSView {
    var image: CGImage?

    override func draw(_ dirtyRect: NSRect) {
        guard let image, let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.interpolationQuality = .high
        ctx.draw(image, in: bounds)
        NSColor.systemBlue.withAlphaComponent(0.6).setStroke()
        NSBezierPath(rect: bounds.insetBy(dx: 0.5, dy: 0.5)).stroke()
    }

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 { window?.close() } else { window?.performDrag(with: event) }
    }
}
