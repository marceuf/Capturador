import AppKit
import CoreImage

enum Tool: Int, CaseIterable {
    case select, rectangle, oval, arrow, line, pen, text, highlight, mosaic

    var title: String {
        switch self {
        case .select: return "Selecionar"
        case .rectangle: return "Retângulo"
        case .oval: return "Círculo"
        case .arrow: return "Seta"
        case .line: return "Linha"
        case .pen: return "Caneta"
        case .text: return "Texto"
        case .highlight: return "Marca-texto"
        case .mosaic: return "Desfoque"
        }
    }

    var symbol: String {
        switch self {
        case .select: return "cursorarrow"
        case .rectangle: return "rectangle"
        case .oval: return "circle"
        case .arrow: return "arrow.up.right"
        case .line: return "line.diagonal"
        case .pen: return "pencil.tip"
        case .text: return "textformat"
        case .highlight: return "highlighter"
        case .mosaic: return "checkerboard.rectangle"
        }
    }

    /// Tecla de atalho (além de 1–9 pela ordem).
    var key: String {
        switch self {
        case .select: return "v"
        case .rectangle: return "r"
        case .oval: return "o"
        case .arrow: return "a"
        case .line: return "l"
        case .pen: return "p"
        case .text: return "t"
        case .highlight: return "h"
        case .mosaic: return "b"
        }
    }

    /// Ferramentas cujas marcações são definidas por dois pontos opostos.
    var isBoxed: Bool { self == .rectangle || self == .oval || self == .mosaic }
}

/// Espessura do traço ou, no texto, tamanho da fonte; em pontos da exibição.
enum StrokeLevel: Int, CaseIterable {
    case thin, medium, thick

    var lineWidth: CGFloat { [2, 4, 8][rawValue] }
    var fontSize: CGFloat { [14, 20, 30][rawValue] }
    var title: String { ["Fino", "Médio", "Grosso"][rawValue] }
}

/// Uma anotação. Pontos são normalizados (0...1) sobre a imagem; espessura e fonte são
/// frações da largura. Assim ela independe do tamanho de exibição e da resolução final.
struct Annotation {
    let id = UUID()
    var tool: Tool
    var color: NSColor
    var lineWidth: CGFloat
    var points: [CGPoint]
    var text: String = ""
    var fontSize: CGFloat = 0
}

/// Mostra a captura e permite criar, selecionar e editar anotações por cima.
final class CanvasView: NSView, NSTextViewDelegate {
    let baseImage: CGImage
    private let pixellated: CGImage?

    var tool: Tool = .rectangle {
        didSet {
            commitText()
            select(nil)
            window?.invalidateCursorRects(for: self)
            onStyleChange?()
        }
    }
    var color: NSColor = .systemRed {
        didSet {
            guard !syncingStyle else { return }
            textView?.textColor = color
            textView?.insertionPointColor = color
            applyToSelection { $0.color = self.color }
            onStyleChange?()
        }
    }
    var level: StrokeLevel = .medium {
        didSet {
            guard !syncingStyle else { return }
            if textView != nil {
                editingFontSize = level.fontSize
                textView?.font = Self.font(editingFontSize)
                layoutTextView()
            }
            applyToSelection { a in
                if a.tool == .text { a.fontSize = self.level.fontSize / self.bounds.width }
                else { a.lineWidth = self.level.lineWidth / self.bounds.width }
            }
            onStyleChange?()
        }
    }

    /// Chamado quando ferramenta, cor, espessura ou seleção mudam, para a barra se atualizar.
    var onStyleChange: (() -> Void)?

    private(set) var annotations: [Annotation] = []
    private(set) var selectedID: UUID?
    private var undoStack: [[Annotation]] = []
    private var redoStack: [[Annotation]] = []
    private var syncingStyle = false

    private enum Drag {
        case drawing(Annotation)
        case moving(start: CGPoint, original: Annotation)
        case resizing(handle: Int, original: Annotation)
    }
    private var drag: Drag?
    private var dragSnapshot: [Annotation]?

    private var textView: NSTextView?
    private var editingID: UUID?
    private var textTopLeft: CGPoint = .zero
    private var editingFontSize: CGFloat = 20

    init(image: CGImage) {
        baseImage = image
        pixellated = CanvasView.makePixellated(image)
        super.init(frame: .zero)
    }

    required init?(coder: NSCoder) { fatalError() }

    override var acceptsFirstResponder: Bool { true }
    override var isFlipped: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func resetCursorRects() {
        let cursor: NSCursor
        switch tool {
        case .select: cursor = .arrow
        case .text: cursor = .iBeam
        default: cursor = .crosshair
        }
        addCursorRect(bounds, cursor: cursor)
    }

    var selected: Annotation? { selectedID.flatMap { id in annotations.first { $0.id == id } } }
    var isEditingText: Bool { textView != nil }

    // MARK: Coordenadas

    private func normalize(_ p: CGPoint) -> CGPoint {
        CGPoint(x: p.x / bounds.width, y: p.y / bounds.height)
    }

    private static func denormalize(_ p: CGPoint, in size: CGSize) -> CGPoint {
        CGPoint(x: p.x * size.width, y: p.y * size.height)
    }

    private func viewPoint(_ p: CGPoint) -> CGPoint { Self.denormalize(p, in: bounds.size) }

    private static func box(_ pts: [CGPoint]) -> CGRect {
        guard pts.count >= 2 else { return .zero }
        return CGRect(x: min(pts[0].x, pts[1].x), y: min(pts[0].y, pts[1].y),
                      width: abs(pts[1].x - pts[0].x), height: abs(pts[1].y - pts[0].y))
    }

    private static func font(_ size: CGFloat) -> NSFont { .systemFont(ofSize: size, weight: .semibold) }

    private static func textAttributes(_ a: Annotation, fontSize: CGFloat) -> [NSAttributedString.Key: Any] {
        [.font: font(fontSize), .foregroundColor: a.color]
    }

    /// Retângulo ocupado por um texto, com o canto superior esquerdo em `points[0]`.
    private static func textRect(_ a: Annotation, in size: CGSize) -> CGRect {
        let topLeft = denormalize(a.points[0], in: size)
        let bounding = textSize(a.text, font: font(a.fontSize * size.width))
        return CGRect(x: topLeft.x, y: topLeft.y - bounding.height, width: bounding.width, height: bounding.height)
    }

    private static func textSize(_ text: String, font: NSFont) -> CGSize {
        // Uma linha vazia no fim ainda ocupa altura no editor; o espaço garante a mesma medida.
        let measured = text.isEmpty || text.hasSuffix("\n") ? text + " " : text
        let r = NSAttributedString(string: measured, attributes: [.font: font])
            .boundingRect(with: CGSize(width: CGFloat.greatestFiniteMagnitude, height: .greatestFiniteMagnitude),
                          options: [.usesLineFragmentOrigin, .usesFontLeading])
        return CGSize(width: ceil(r.width), height: ceil(r.height))
    }

    // MARK: Desenho

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.interpolationQuality = .high
        ctx.draw(baseImage, in: bounds)
        for a in annotations where a.id != editingID { draw(a, in: ctx, size: bounds.size) }
        if case .drawing(let a) = drag { draw(a, in: ctx, size: bounds.size) }
        if let s = selected, s.id != editingID { drawSelection(of: s, in: ctx) }
    }

    private func draw(_ a: Annotation, in ctx: CGContext, size: CGSize) {
        let pts = a.points.map { Self.denormalize($0, in: size) }
        let width = max(1, a.lineWidth * size.width)
        ctx.saveGState()
        defer { ctx.restoreGState() }
        ctx.setStrokeColor(a.color.cgColor)
        ctx.setFillColor(a.color.cgColor)
        ctx.setLineWidth(width)
        ctx.setLineCap(.round)
        ctx.setLineJoin(.round)
        let rect = Self.box(pts)

        switch a.tool {
        case .select:
            break
        case .rectangle:
            ctx.stroke(rect)
        case .oval:
            ctx.strokeEllipse(in: rect)
        case .line:
            ctx.strokeLineSegments(between: [pts[0], pts[1]])
        case .arrow:
            drawArrow(from: pts[0], to: pts[1], width: width, in: ctx)
        case .pen, .highlight:
            if a.tool == .highlight {
                ctx.setStrokeColor(a.color.withAlphaComponent(0.4).cgColor)
                ctx.setLineWidth(width * 4)
                ctx.setLineCap(.butt)
                ctx.setBlendMode(.multiply)
            }
            ctx.addLines(between: pts)
            ctx.strokePath()
        case .mosaic:
            ctx.clip(to: rect)
            if let pixellated {
                ctx.draw(pixellated, in: CGRect(origin: .zero, size: size))
            } else {
                // Sem o filtro, cobre a área: nunca deixa à mostra o que deveria esconder.
                ctx.setFillColor(NSColor.black.cgColor)
                ctx.fill(rect)
            }
        case .text:
            let r = Self.textRect(a, in: size)
            NSGraphicsContext.saveGraphicsState()
            NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
            NSAttributedString(string: a.text, attributes: Self.textAttributes(a, fontSize: a.fontSize * size.width))
                .draw(with: r, options: [.usesLineFragmentOrigin, .usesFontLeading])
            NSGraphicsContext.restoreGraphicsState()
        }
    }

    private func drawArrow(from start: CGPoint, to end: CGPoint, width: CGFloat, in ctx: CGContext) {
        let angle = atan2(end.y - start.y, end.x - start.x)
        let head = max(12, width * 4)
        let base = CGPoint(x: end.x - cos(angle) * head * 0.8, y: end.y - sin(angle) * head * 0.8)
        ctx.strokeLineSegments(between: [start, base])
        let left = CGPoint(x: end.x - head * cos(angle - .pi / 7), y: end.y - head * sin(angle - .pi / 7))
        let right = CGPoint(x: end.x - head * cos(angle + .pi / 7), y: end.y - head * sin(angle + .pi / 7))
        ctx.move(to: end)
        ctx.addLine(to: left)
        ctx.addLine(to: right)
        ctx.closePath()
        ctx.fillPath()
    }

    private func drawSelection(of a: Annotation, in ctx: CGContext) {
        ctx.saveGState()
        defer { ctx.restoreGState() }
        let accent = NSColor.controlAccentColor
        ctx.setStrokeColor(accent.cgColor)
        ctx.setLineWidth(1)
        ctx.setLineDash(phase: 0, lengths: [4, 3])
        ctx.stroke(outline(of: a).insetBy(dx: -4, dy: -4))
        ctx.setLineDash(phase: 0, lengths: [])
        for h in handles(of: a) {
            let r = CGRect(x: h.x - 4.5, y: h.y - 4.5, width: 9, height: 9)
            ctx.setFillColor(NSColor.white.cgColor)
            ctx.fillEllipse(in: r)
            ctx.setStrokeColor(accent.cgColor)
            ctx.setLineWidth(1.5)
            ctx.strokeEllipse(in: r)
        }
    }

    // MARK: Seleção e alças

    /// Retângulo envolvente da marcação, em coordenadas da view.
    private func outline(of a: Annotation) -> CGRect {
        if a.tool == .text { return Self.textRect(a, in: bounds.size) }
        let pts = a.points.map(viewPoint)
        let xs = pts.map(\.x), ys = pts.map(\.y)
        let r = CGRect(x: xs.min()!, y: ys.min()!, width: xs.max()! - xs.min()!, height: ys.max()! - ys.min()!)
        return r.insetBy(dx: -a.lineWidth * bounds.width / 2, dy: -a.lineWidth * bounds.width / 2)
    }

    private func handles(of a: Annotation) -> [CGPoint] {
        let pts = a.points.map(viewPoint)
        if a.tool.isBoxed {
            let r = Self.box(pts)
            return [CGPoint(x: r.minX, y: r.minY), CGPoint(x: r.maxX, y: r.minY),
                    CGPoint(x: r.minX, y: r.maxY), CGPoint(x: r.maxX, y: r.maxY)]
        }
        if a.tool == .line || a.tool == .arrow { return [pts[0], pts[1]] }
        return []
    }

    private func handleIndex(at p: CGPoint, of a: Annotation) -> Int? {
        handles(of: a).firstIndex { hypot($0.x - p.x, $0.y - p.y) <= 7 }
    }

    /// Marcação mais à frente sob o ponto.
    private func hit(_ p: CGPoint) -> Annotation? {
        annotations.reversed().first { a in
            guard a.id != editingID else { return false }
            let tolerance = max(5, a.lineWidth * bounds.width / 2 + 4)
            let pts = a.points.map(viewPoint)
            switch a.tool {
            case .rectangle, .oval, .mosaic:
                return Self.box(pts).insetBy(dx: -tolerance, dy: -tolerance).contains(p)
            case .line, .arrow:
                return Self.distance(p, pts[0], pts[1]) <= tolerance
            case .pen, .highlight:
                let t = a.tool == .highlight ? tolerance * 3 : tolerance
                if pts.count == 1 { return hypot(pts[0].x - p.x, pts[0].y - p.y) <= t }
                return zip(pts, pts.dropFirst()).contains { Self.distance(p, $0, $1) <= t }
            case .text:
                return Self.textRect(a, in: bounds.size).insetBy(dx: -4, dy: -4).contains(p)
            case .select:
                return false
            }
        }
    }

    private static func distance(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> CGFloat {
        let dx = b.x - a.x, dy = b.y - a.y
        let len2 = dx * dx + dy * dy
        guard len2 > 0 else { return hypot(p.x - a.x, p.y - a.y) }
        let t = max(0, min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2))
        return hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy))
    }

    private func select(_ id: UUID?) {
        guard selectedID != id else { return }
        selectedID = id
        if let a = selected {
            // A barra passa a mostrar o estilo da marcação, sem reaplicá-lo.
            syncingStyle = true
            color = a.color
            level = nearestLevel(for: a)
            syncingStyle = false
        }
        needsDisplay = true
        onStyleChange?()
    }

    /// Tira a seleção; devolve false se não havia nada selecionado.
    @discardableResult
    func deselect() -> Bool {
        guard selectedID != nil else { return false }
        select(nil)
        return true
    }

    private func nearestLevel(for a: Annotation) -> StrokeLevel {
        let value = (a.tool == .text ? a.fontSize : a.lineWidth) * bounds.width
        return StrokeLevel.allCases.min {
            abs((a.tool == .text ? $0.fontSize : $0.lineWidth) - value)
                < abs((a.tool == .text ? $1.fontSize : $1.lineWidth) - value)
        } ?? .medium
    }

    private func applyToSelection(_ change: (inout Annotation) -> Void) {
        guard let id = selectedID, let i = annotations.firstIndex(where: { $0.id == id }) else { return }
        registerUndo()
        change(&annotations[i])
        needsDisplay = true
    }

    /// Apaga a marcação selecionada; devolve false se não havia seleção.
    @discardableResult
    func deleteSelection() -> Bool {
        guard let id = selectedID else { return false }
        registerUndo()
        annotations.removeAll { $0.id == id }
        select(nil)
        needsDisplay = true
        return true
    }

    // MARK: Mouse

    override func mouseDown(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        window?.makeKey()
        if textView != nil {
            commitText()
            if tool == .text { return }
        }
        window?.makeFirstResponder(self)

        if event.clickCount == 2, let a = hit(p), a.tool == .text {
            beginEditing(a)
            return
        }
        if let s = selected, let handle = handleIndex(at: p, of: s) {
            drag = .resizing(handle: handle, original: s)
            dragSnapshot = annotations
            return
        }
        switch tool {
        case .text:
            if let a = hit(p), a.tool == .text { beginEditing(a) } else { beginNewText(at: p) }
        case .select:
            if let a = hit(p) {
                select(a.id)
                drag = .moving(start: p, original: a)
                dragSnapshot = annotations
            } else {
                select(nil)
                window?.performDrag(with: event)
            }
        default:
            select(nil)
            let n = normalize(p)
            let width = (tool == .mosaic ? 0 : level.lineWidth) / bounds.width
            drag = .drawing(Annotation(tool: tool, color: color, lineWidth: width, points: [n, n]))
        }
    }

    override func mouseDragged(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        switch drag {
        case .drawing(var a):
            if a.tool == .pen || a.tool == .highlight { a.points.append(normalize(p)) } else { a.points[1] = normalize(p) }
            drag = .drawing(a)
        case .moving(let start, let original):
            let d = CGPoint(x: (p.x - start.x) / bounds.width, y: (p.y - start.y) / bounds.height)
            replace(original.id) { $0.points = original.points.map { CGPoint(x: $0.x + d.x, y: $0.y + d.y) } }
        case .resizing(let handle, let original):
            let n = normalize(p)
            replace(original.id) { a in
                if a.tool.isBoxed {
                    let r = Self.box(original.points)
                    // A alça oposta fica parada.
                    let opposite = [CGPoint(x: r.maxX, y: r.maxY), CGPoint(x: r.minX, y: r.maxY),
                                    CGPoint(x: r.maxX, y: r.minY), CGPoint(x: r.minX, y: r.minY)][handle]
                    a.points = [opposite, n]
                } else {
                    a.points[handle] = n
                }
            }
        case nil:
            return
        }
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        defer { drag = nil; dragSnapshot = nil; needsDisplay = true }
        switch drag {
        case .drawing(let a):
            let a0 = viewPoint(a.points.first!), a1 = viewPoint(a.points.last!)
            guard a.tool == .pen || a.tool == .highlight || hypot(a1.x - a0.x, a1.y - a0.y) > 3 else { return }
            registerUndo()
            annotations.append(a)
        case .moving, .resizing:
            if let before = dragSnapshot, before.map(\.points) != annotations.map(\.points) {
                undoStack.append(before)
                redoStack.removeAll()
            }
        case nil:
            break
        }
    }

    private func replace(_ id: UUID, _ change: (inout Annotation) -> Void) {
        guard let i = annotations.firstIndex(where: { $0.id == id }) else { return }
        change(&annotations[i])
    }

    // MARK: Texto

    private func beginNewText(at p: CGPoint) {
        select(nil)
        editingID = nil
        editingFontSize = level.fontSize
        startTextEditor(text: "", topLeft: p, color: color)
    }

    private func beginEditing(_ a: Annotation) {
        select(nil)
        syncingStyle = true
        color = a.color
        level = nearestLevel(for: a)
        syncingStyle = false
        onStyleChange?()
        editingID = a.id
        editingFontSize = a.fontSize * bounds.width
        startTextEditor(text: a.text, topLeft: viewPoint(a.points[0]), color: a.color)
        needsDisplay = true
    }

    private func startTextEditor(text: String, topLeft: CGPoint, color: NSColor) {
        let tv = TextEditorView(frame: .zero)
        tv.isRichText = false
        tv.allowsUndo = true
        tv.drawsBackground = true
        tv.backgroundColor = NSColor.white.withAlphaComponent(0.12)
        tv.font = Self.font(editingFontSize)
        tv.textColor = color
        tv.insertionPointColor = color
        tv.textContainerInset = .zero
        tv.textContainer?.lineFragmentPadding = 0
        tv.textContainer?.widthTracksTextView = false
        tv.textContainer?.containerSize = CGSize(width: CGFloat.greatestFiniteMagnitude, height: .greatestFiniteMagnitude)
        tv.isHorizontallyResizable = true
        tv.isVerticallyResizable = true
        tv.wantsLayer = true
        tv.layer?.borderColor = NSColor.controlAccentColor.cgColor
        tv.layer?.borderWidth = 1
        tv.string = text
        tv.delegate = self
        tv.onCommit = { [weak self] in self?.commitText() }
        tv.onCancel = { [weak self] in _ = self?.cancelText() }

        // A caixa começa sempre inteira dentro da imagem, mesmo em capturas baixas.
        let size = Self.textSize(text, font: Self.font(editingFontSize))
        let minWidth = editingFontSize
        textTopLeft = CGPoint(x: max(0, min(topLeft.x, bounds.width - max(size.width, minWidth))),
                              y: min(bounds.height, max(topLeft.y, size.height)))
        textView = tv
        addSubview(tv)
        layoutTextView()
        window?.makeFirstResponder(tv)
        tv.setSelectedRange(NSRange(location: (text as NSString).length, length: 0))
    }

    private func layoutTextView() {
        guard let tv = textView else { return }
        let size = Self.textSize(tv.string, font: Self.font(editingFontSize))
        let width = max(size.width, editingFontSize) + 2
        tv.frame = CGRect(x: textTopLeft.x, y: textTopLeft.y - size.height, width: width, height: size.height)
    }

    func textDidChange(_ notification: Notification) { layoutTextView() }

    /// Confirma o texto em edição (se houver) como anotação.
    func commitText() {
        guard let tv = textView else { return }
        let text = tv.string.trimmingCharacters(in: .whitespacesAndNewlines)
        tv.removeFromSuperview()
        textView = nil
        let fontSize = editingFontSize / bounds.width
        let anchor = normalize(textTopLeft)
        if let id = editingID, let i = annotations.firstIndex(where: { $0.id == id }) {
            let old = annotations[i]
            if text.isEmpty {
                registerUndo()
                annotations.remove(at: i)
            } else if old.text != text || old.color != color || old.fontSize != fontSize || old.points[0] != anchor {
                registerUndo()
                annotations[i].text = text
                annotations[i].color = color
                annotations[i].fontSize = fontSize
                annotations[i].points = [anchor]
            }
        } else if !text.isEmpty {
            registerUndo()
            annotations.append(Annotation(tool: .text, color: color, lineWidth: 0, points: [anchor],
                                          text: text, fontSize: fontSize))
        }
        editingID = nil
        window?.makeFirstResponder(self)
        needsDisplay = true
    }

    /// Cancela a edição de texto; devolve false se não havia edição.
    func cancelText() -> Bool {
        guard let tv = textView else { return false }
        tv.removeFromSuperview()
        textView = nil
        editingID = nil
        window?.makeFirstResponder(self)
        needsDisplay = true
        return true
    }

    // MARK: Desfazer

    private func registerUndo() {
        undoStack.append(annotations)
        redoStack.removeAll()
    }

    func undo() {
        if cancelText() { return }
        guard let previous = undoStack.popLast() else { NSSound.beep(); return }
        redoStack.append(annotations)
        annotations = previous
        if selected == nil { select(nil) }
        needsDisplay = true
    }

    func redo() {
        guard let next = redoStack.popLast() else { NSSound.beep(); return }
        undoStack.append(annotations)
        annotations = next
        if selected == nil { select(nil) }
        needsDisplay = true
    }

    // MARK: Exportação

    /// Renderiza imagem + anotações na resolução original da captura, sem seleção nem alças.
    /// Devolve nil se não conseguir; nunca troca pela captura sem as anotações, que poderia
    /// revelar o que o Desfoque escondeu.
    func renderedImage() -> CGImage? {
        commitText()
        let w = baseImage.width, h = baseImage.height
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: space,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        let size = CGSize(width: w, height: h)
        ctx.interpolationQuality = .high
        ctx.draw(baseImage, in: CGRect(origin: .zero, size: size))
        for a in annotations { draw(a, in: ctx, size: size) }
        return ctx.makeImage()
    }

    /// Borra e depois pixeliza em blocos grandes. Só pixelizar deixa cada bloco com a média exata
    /// do trecho embaixo, o que permite reconstruir texto comparando com fontes conhecidas; o borrão
    /// mistura os vizinhos antes e desfaz essa correspondência.
    private static func makePixellated(_ image: CGImage) -> CGImage? {
        let input = CIImage(cgImage: image)
        let block = max(24, Double(max(image.width, image.height)) / 40)
        guard let blur = CIFilter(name: "CIGaussianBlur"), let pixellate = CIFilter(name: "CIPixellate") else { return nil }
        blur.setValue(input.clampedToExtent(), forKey: kCIInputImageKey)
        blur.setValue(block / 2, forKey: kCIInputRadiusKey)
        pixellate.setValue(blur.outputImage, forKey: kCIInputImageKey)
        pixellate.setValue(block, forKey: kCIInputScaleKey)
        pixellate.setValue(CIVector(x: 0, y: 0), forKey: kCIInputCenterKey)
        guard let output = pixellate.outputImage?.cropped(to: input.extent) else { return nil }
        return CIContext().createCGImage(output, from: input.extent)
    }
}

/// Campo de texto sobre a imagem: Enter confirma, ⇧Enter quebra linha, Esc cancela.
private final class TextEditorView: NSTextView {
    var onCommit: (() -> Void)?
    var onCancel: (() -> Void)?

    override func doCommand(by selector: Selector) {
        switch selector {
        case #selector(insertNewline(_:)):
            if NSApp.currentEvent?.modifierFlags.contains(.shift) == true {
                insertText("\n", replacementRange: selectedRange())
            } else {
                onCommit?()
            }
        case #selector(cancelOperation(_:)):
            onCancel?()
        case #selector(insertTab(_:)):
            onCommit?()
        default:
            super.doCommand(by: selector)
        }
    }
}
