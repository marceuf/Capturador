import AppKit

/// Barra de ferramentas em forma de cápsula, numa janela própria abaixo da imagem.
/// Segue a imagem até ser arrastada; depois fica onde o usuário a deixou.
final class ToolCapsule: NSPanel {
    private weak var editor: EditorWindow?
    private var toolButtons: [Tool: CapsuleButton] = [:]
    private var levelButtons: [StrokeLevel: CapsuleButton] = [:]
    private let colorButton = ColorDotButton()
    private var moreButton: CapsuleButton!

    static let presetColors: [NSColor] = [
        .systemRed, .systemOrange, .systemYellow, .systemGreen,
        .systemBlue, .systemPurple, .black, .white
    ]

    init(editor: EditorWindow) {
        self.editor = editor
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .floating
        hidesOnDeactivate = false
        becomesKeyOnlyIfNeeded = true
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isReleasedWhenClosed = false
        collectionBehavior = [.fullScreenAuxiliary]
        build()
    }

    // O teclado fica sempre com a janela da imagem.
    override var canBecomeKey: Bool { false }

    var isDetached = false

    private func build() {
        guard let editor else { return }
        var views: [NSView] = []
        for tool in Tool.allCases {
            let index = Tool.allCases.firstIndex(of: tool)! + 1
            let b = CapsuleButton(symbol: tool.symbol, tip: "\(tool.title) (\(index) ou \(tool.key.uppercased()))",
                                  target: self, action: #selector(toolClicked(_:)))
            b.tag = tool.rawValue
            toolButtons[tool] = b
            views.append(b)
        }
        views.append(CapsuleSeparator())

        colorButton.target = self
        colorButton.action = #selector(colorClicked(_:))
        colorButton.toolTip = "Cor"
        views.append(colorButton)
        for level in StrokeLevel.allCases {
            let b = CapsuleButton(image: Self.levelImage(level), tip: level.title, target: self, action: #selector(levelClicked(_:)))
            b.tag = level.rawValue
            levelButtons[level] = b
            views.append(b)
        }
        views.append(CapsuleSeparator())

        views.append(CapsuleButton(symbol: "arrow.uturn.backward", tip: "Desfazer (⌘Z)", target: editor, action: #selector(EditorWindow.undoAction)))
        views.append(CapsuleButton(symbol: "doc.on.doc", tip: "Copiar (⌘C)", target: editor, action: #selector(EditorWindow.copyAction)))
        views.append(CapsuleButton(symbol: "square.and.arrow.down", tip: "Salvar (⌘S)", target: editor, action: #selector(EditorWindow.saveAction)))
        let discard = CapsuleButton(symbol: "trash", tip: "Descartar (⌘⌫)", target: editor, action: #selector(EditorWindow.discardAction))
        discard.tint = .systemRed
        views.append(discard)
        moreButton = CapsuleButton(symbol: "ellipsis.circle", tip: "Mais opções", target: self, action: #selector(moreClicked(_:)))
        views.append(moreButton)

        let stack = NSStackView(views: views)
        stack.orientation = .horizontal
        stack.spacing = 2
        stack.edgeInsets = NSEdgeInsets(top: 5, left: 10, bottom: 5, right: 10)
        stack.translatesAutoresizingMaskIntoConstraints = false

        let background = CapsuleBackground()
        background.material = .hudWindow
        background.blendingMode = .behindWindow
        background.state = .active
        background.onDragStart = { [weak self] in self?.detach() }
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
        background.layer?.borderWidth = 0.5
        background.layer?.borderColor = NSColor.white.withAlphaComponent(0.15).cgColor
        contentView = background
        setContentSize(size)
        refresh()
    }

    /// Para de seguir a imagem quando o usuário arrasta a cápsula.
    private func detach() {
        guard !isDetached else { return }
        isDetached = true
        parent?.removeChildWindow(self)
    }

    /// Atualiza botões a partir do estado do canvas.
    func refresh() {
        guard let canvas = editor?.canvas else { return }
        for (tool, b) in toolButtons { b.isActive = tool == canvas.tool }
        for (level, b) in levelButtons { b.isActive = level == canvas.level }
        colorButton.color = canvas.color
    }

    func setHighlighted(_ on: Bool) {
        animator().alphaValue = on ? 1 : 0.55
    }

    // MARK: Ações

    @objc private func toolClicked(_ sender: NSButton) {
        guard let tool = Tool(rawValue: sender.tag) else { return }
        editor?.canvas.tool = tool
    }

    @objc private func levelClicked(_ sender: NSButton) {
        guard let level = StrokeLevel(rawValue: sender.tag) else { return }
        editor?.canvas.level = level
    }

    @objc private func colorClicked(_ sender: NSButton) {
        let current = editor?.canvas.color
        let menu = NSMenu()
        for (i, color) in Self.presetColors.enumerated() {
            let item = NSMenuItem(title: Self.colorNames[i], action: #selector(presetPicked(_:)), keyEquivalent: "")
            item.target = self
            item.tag = i
            item.image = Self.swatch(color)
            item.state = color == current ? .on : .off
            menu.addItem(item)
        }
        menu.addItem(.separator())
        let other = NSMenuItem(title: "Outra cor…", action: #selector(otherColor), keyEquivalent: "")
        other.target = self
        menu.addItem(other)
        menu.popUp(positioning: nil, at: CGPoint(x: 0, y: sender.bounds.maxY + 4), in: sender)
    }

    @objc private func presetPicked(_ sender: NSMenuItem) {
        editor?.canvas.color = Self.presetColors[sender.tag]
    }

    @objc private func otherColor() { showSystemColorPanel() }

    private static let colorNames = ["Vermelho", "Laranja", "Amarelo", "Verde", "Azul", "Roxo", "Preto", "Branco"]

    private static func swatch(_ color: NSColor) -> NSImage {
        NSImage(size: CGSize(width: 14, height: 14), flipped: false) { rect in
            color.setFill()
            NSBezierPath(ovalIn: rect.insetBy(dx: 1, dy: 1)).fill()
            NSColor.gray.withAlphaComponent(0.6).setStroke()
            NSBezierPath(ovalIn: rect.insetBy(dx: 1.5, dy: 1.5)).stroke()
            return true
        }
    }

    /// Cápsula que recebe as mudanças do painel de cores; o AppKit não informa o alvo atual.
    private static weak var colorPanelOwner: ToolCapsule?

    private func showSystemColorPanel() {
        let panel = NSColorPanel.shared
        panel.level = NSWindow.Level(rawValue: NSWindow.Level.floating.rawValue + 1)
        panel.showsAlpha = false
        panel.color = editor?.canvas.color ?? .systemRed
        panel.setTarget(self)
        panel.setAction(#selector(systemColorChanged(_:)))
        Self.colorPanelOwner = self
        panel.orderFront(nil)
    }

    @objc private func systemColorChanged(_ sender: NSColorPanel) {
        editor?.canvas.color = sender.color
    }

    /// O painel de cores é compartilhado e sobrevive ao editor: solta o alvo ao fechar.
    override func close() {
        if Self.colorPanelOwner === self {
            NSColorPanel.shared.setTarget(nil)
            NSColorPanel.shared.setAction(nil)
        }
        super.close()
    }

    @objc private func moreClicked(_ sender: NSButton) {
        guard let editor else { return }
        let menu = NSMenu()
        func add(_ title: String, _ symbol: String, _ action: Selector, _ key: String = "", _ mods: NSEvent.ModifierFlags = []) {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
            item.keyEquivalentModifierMask = mods
            item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
            item.target = editor
            menu.addItem(item)
        }
        add("Salvar como…", "square.and.arrow.down.on.square", #selector(EditorWindow.saveAsAction), "s", [.command, .shift])
        add("Fixar na tela", "pin", #selector(EditorWindow.pinAction))
        add("Compartilhar…", "square.and.arrow.up", #selector(EditorWindow.shareAction))
        add("Extrair texto (OCR)", "text.viewfinder", #selector(EditorWindow.ocrAction))
        menu.popUp(positioning: nil, at: CGPoint(x: 0, y: sender.bounds.maxY + 4), in: sender)
    }

    var shareAnchor: NSView { moreButton }

    // MARK: Ícones

    private static func levelImage(_ level: StrokeLevel) -> NSImage {
        let d = [4.0, 7.0, 11.0][level.rawValue]
        let image = NSImage(size: CGSize(width: 16, height: 16), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(ovalIn: CGRect(x: rect.midX - d / 2, y: rect.midY - d / 2, width: d, height: d)).fill()
            return true
        }
        image.isTemplate = true
        return image
    }
}

/// Fundo da cápsula; arrastar por ele move a janela.
private final class CapsuleBackground: NSVisualEffectView {
    var onDragStart: (() -> Void)?

    override func mouseDown(with event: NSEvent) {
        onDragStart?()
        window?.performDrag(with: event)
    }
}

final class CapsuleButton: NSButton {
    var isActive = false { didSet { updateLook() } }
    var tint: NSColor? { didSet { updateLook() } }

    convenience init(symbol: String, tip: String, target: AnyObject?, action: Selector) {
        let config = NSImage.SymbolConfiguration(pointSize: 14, weight: .medium)
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: tip)?.withSymbolConfiguration(config)
        self.init(image: image ?? NSImage(), tip: tip, target: target, action: action)
    }

    convenience init(image: NSImage, tip: String, target: AnyObject?, action: Selector) {
        self.init(frame: CGRect(x: 0, y: 0, width: 30, height: 28))
        self.image = image
        self.target = target
        self.action = action
        toolTip = tip
        setAccessibilityLabel(tip)
        isBordered = false
        imagePosition = .imageOnly
        wantsLayer = true
        layer?.cornerRadius = 7
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: 30).isActive = true
        heightAnchor.constraint(equalToConstant: 28).isActive = true
        updateLook()
    }

    // Um clique na cápsula age direto, mesmo com o app em segundo plano.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    private func updateLook() {
        layer?.backgroundColor = isActive ? NSColor.controlAccentColor.cgColor : NSColor.clear.cgColor
        contentTintColor = isActive ? .white : (tint ?? .labelColor)
    }
}

private final class CapsuleSeparator: NSView {
    init() {
        super.init(frame: .zero)
        wantsLayer = true
        layer?.backgroundColor = NSColor.white.withAlphaComponent(0.18).cgColor
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: 1).isActive = true
        heightAnchor.constraint(equalToConstant: 18).isActive = true
    }

    required init?(coder: NSCoder) { fatalError() }
}

/// Bolinha com a cor atual.
private final class ColorDotButton: NSButton {
    var color: NSColor = .systemRed { didSet { needsDisplay = true } }

    init() {
        super.init(frame: CGRect(x: 0, y: 0, width: 30, height: 28))
        isBordered = false
        title = ""
        setAccessibilityLabel("Cor")
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: 30).isActive = true
        heightAnchor.constraint(equalToConstant: 28).isActive = true
    }

    required init?(coder: NSCoder) { fatalError() }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        let r = CGRect(x: bounds.midX - 9, y: bounds.midY - 9, width: 18, height: 18)
        color.setFill()
        NSBezierPath(ovalIn: r).fill()
        NSColor.white.withAlphaComponent(0.85).setStroke()
        let ring = NSBezierPath(ovalIn: r.insetBy(dx: 0.75, dy: 0.75))
        ring.lineWidth = 1.5
        ring.stroke()
    }
}
