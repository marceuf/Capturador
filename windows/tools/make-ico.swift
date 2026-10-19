// Gera windows/src/Capturador/Assets/Capturador.ico a partir de Resources/AppIcon.icns.
// Uso, na raiz do repositório: swift windows/tools/make-ico.swift
import AppKit

guard let icon = NSImage(contentsOfFile: "Resources/AppIcon.icns") else { fatalError("AppIcon.icns não encontrado") }
// O ícone do macOS tem margem de 100/1024 em volta do corpo; no Windows o corpo ocupa quase tudo.
let crop = NSRect(x: 84, y: 84, width: 856, height: 856)

func png(_ size: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    icon.draw(in: NSRect(x: 0, y: 0, width: size, height: size),
              from: NSRect(x: crop.minX * icon.size.width / 1024, y: crop.minY * icon.size.height / 1024,
                           width: crop.width * icon.size.width / 1024, height: crop.height * icon.size.height / 1024),
              operation: .copy, fraction: 1)
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let sizes = [16, 20, 24, 32, 40, 48, 64, 256]
let images = sizes.map(png)
var ico = Data()
func le16(_ v: Int) { ico.append(contentsOf: [UInt8(v & 0xff), UInt8(v >> 8 & 0xff)]) }
func le32(_ v: Int) { le16(v & 0xffff); le16(v >> 16 & 0xffff) }
le16(0); le16(1); le16(sizes.count)
var offset = 6 + 16 * sizes.count
for (size, data) in zip(sizes, images) {
    ico.append(UInt8(size % 256)); ico.append(UInt8(size % 256))
    ico.append(0); ico.append(0)
    le16(1); le16(32); le32(data.count); le32(offset)
    offset += data.count
}
images.forEach { ico.append($0) }
let out = URL(fileURLWithPath: "windows/src/Capturador/Assets/Capturador.ico")
try! ico.write(to: out)
print("\(out.path) criado")
