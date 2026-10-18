// Gera Resources/AppIcon.icns. Uso: swift Tools/make-icon.swift
import AppKit

func render(_ size: CGFloat) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size),
                               bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let s = size / 1024
    // Grade de ícones do macOS: corpo de 824 pt centrado em 1024.
    let body = NSRect(x: 100 * s, y: 100 * s, width: 824 * s, height: 824 * s)
    let shape = NSBezierPath(roundedRect: body, xRadius: 185 * s, yRadius: 185 * s)

    NSGraphicsContext.saveGraphicsState()
    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.3)
    shadow.shadowBlurRadius = 20 * s
    shadow.shadowOffset = NSSize(width: 0, height: -8 * s)
    shadow.set()
    NSColor.black.setFill()
    shape.fill()
    NSGraphicsContext.restoreGraphicsState()

    NSGradient(colors: [NSColor(srgbRed: 0.10, green: 0.45, blue: 1.00, alpha: 1),
                        NSColor(srgbRed: 0.42, green: 0.22, blue: 0.95, alpha: 1)])!
        .draw(in: shape, angle: -60)

    // Cantos do visor.
    let inset = body.insetBy(dx: 190 * s, dy: 190 * s)
    let arm = 130 * s
    let corners = NSBezierPath()
    for (x, y, dx, dy) in [(inset.minX, inset.minY, 1.0, 1.0), (inset.maxX, inset.minY, -1.0, 1.0),
                           (inset.minX, inset.maxY, 1.0, -1.0), (inset.maxX, inset.maxY, -1.0, -1.0)] {
        corners.move(to: NSPoint(x: x, y: y + dy * arm))
        corners.line(to: NSPoint(x: x, y: y))
        corners.line(to: NSPoint(x: x + dx * arm, y: y))
    }
    corners.lineWidth = 44 * s
    corners.lineCapStyle = .round
    corners.lineJoinStyle = .round
    NSColor.white.setStroke()
    corners.stroke()

    // Obturador no centro.
    let r = 92 * s
    let lens = NSBezierPath(ovalIn: NSRect(x: body.midX - r, y: body.midY - r, width: 2 * r, height: 2 * r))
    NSColor.white.setFill()
    lens.fill()

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let iconset = FileManager.default.temporaryDirectory.appendingPathComponent("AppIcon.iconset")
try? FileManager.default.removeItem(at: iconset)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for base in [16, 32, 128, 256, 512] {
    try render(CGFloat(base)).write(to: iconset.appendingPathComponent("icon_\(base)x\(base).png"))
    try render(CGFloat(base * 2)).write(to: iconset.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}
let task = Process()
task.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
task.arguments = ["-c", "icns", iconset.path, "-o", root.appendingPathComponent("Resources/AppIcon.icns").path]
try task.run()
task.waitUntilExit()
try render(1024).write(to: FileManager.default.temporaryDirectory.appendingPathComponent("AppIcon-preview.png"))
print(task.terminationStatus == 0 ? "Resources/AppIcon.icns criado" : "iconutil falhou")
