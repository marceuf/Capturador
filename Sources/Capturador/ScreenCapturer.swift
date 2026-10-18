import AppKit
import ScreenCaptureKit

/// Imagem congelada de uma tela, usada como fundo da seleção.
struct ScreenSnapshot {
    let screen: NSScreen
    let display: SCDisplay
    let image: CGImage
    /// Retângulos das janelas visíveis, em coordenadas locais da tela (origem embaixo à esquerda), da frente para trás.
    let windowRects: [CGRect]
}

enum CaptureError: LocalizedError {
    case noPermission
    case noDisplay

    var errorDescription: String? {
        switch self {
        case .noPermission:
            return "O Capturador precisa da permissão de Gravação de Tela. Ative-a em Ajustes do Sistema › Privacidade e Segurança › Gravação de Tela e Áudio do Sistema, depois reabra o app."
        case .noDisplay:
            return "Não foi possível identificar a tela atual."
        }
    }
}

enum ScreenCapturer {
    static func hasPermission() -> Bool { CGPreflightScreenCaptureAccess() }

    static func requestPermission() { _ = CGRequestScreenCaptureAccess() }

    static func openPermissionSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID? {
        screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID
    }

    static func screenUnderMouse() -> NSScreen? {
        let mouse = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
    }

    /// Captura só a tela sob o cursor.
    static func snapshotUnderMouse() async throws -> ScreenSnapshot {
        guard let screen = screenUnderMouse() else { throw CaptureError.noDisplay }
        guard let snapshot = try await snapshots(of: [screen]).first else { throw CaptureError.noDisplay }
        return snapshot
    }

    /// Captura todas as telas conectadas, em paralelo, cada uma na sua resolução nativa.
    static func snapshotAllScreens() async throws -> [ScreenSnapshot] {
        let result = try await snapshots(of: NSScreen.screens)
        guard !result.isEmpty else { throw CaptureError.noDisplay }
        return result
    }

    private static func snapshots(of screens: [NSScreen]) async throws -> [ScreenSnapshot] {
        guard hasPermission() else { throw CaptureError.noPermission }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        // Uma única leitura da lista de janelas mantém todas as telas coerentes entre si.
        let windows = globalWindowRects()

        let targets: [(index: Int, screen: NSScreen, display: SCDisplay)] = screens.enumerated().compactMap { i, screen in
            guard let id = displayID(of: screen),
                  let display = content.displays.first(where: { $0.displayID == id }) else { return nil }
            return (i, screen, display)
        }
        let images = try await withThrowingTaskGroup(of: (Int, CGImage).self) { group in
            for target in targets {
                let size = target.screen.frame.size
                let scale = target.screen.backingScaleFactor
                let display = target.display
                let index = target.index
                group.addTask {
                    let config = SCStreamConfiguration()
                    config.width = Int(size.width * scale)
                    config.height = Int(size.height * scale)
                    config.showsCursor = false
                    config.captureResolution = .best
                    let filter = SCContentFilter(display: display, excludingWindows: [])
                    return (index, try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config))
                }
            }
            var out: [Int: CGImage] = [:]
            for try await (index, image) in group { out[index] = image }
            return out
        }
        return targets.compactMap { target in
            guard let image = images[target.index] else { return nil }
            return ScreenSnapshot(screen: target.screen, display: target.display, image: image,
                                  windowRects: localRects(windows, on: target.screen))
        }
    }

    /// Janelas normais visíveis (camada 0), da frente para trás, em coordenadas globais do Cocoa.
    private static func globalWindowRects() -> [CGRect] {
        guard let info = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
                as? [[String: Any]] else { return [] }
        let ownPID = ProcessInfo.processInfo.processIdentifier
        // O CGWindowList usa origem no canto superior esquerdo da tela principal (a da barra de menus).
        let primaryHeight = NSScreen.screens.first?.frame.height ?? 0
        return info.compactMap { window in
            guard (window[kCGWindowLayer as String] as? Int) == 0,
                  (window[kCGWindowOwnerPID as String] as? Int32) != ownPID,
                  let boundsDict = window[kCGWindowBounds as String] as? NSDictionary,
                  let bounds = CGRect(dictionaryRepresentation: boundsDict),
                  bounds.width > 40, bounds.height > 40 else { return nil }
            return CGRect(x: bounds.minX, y: primaryHeight - bounds.maxY, width: bounds.width, height: bounds.height)
        }
    }

    /// Recorta as janelas para uma tela e converte para coordenadas locais dela.
    private static func localRects(_ global: [CGRect], on screen: NSScreen) -> [CGRect] {
        global.compactMap { rect in
            let clipped = rect.intersection(screen.frame)
            guard !clipped.isNull, !clipped.isEmpty else { return nil }
            return clipped.offsetBy(dx: -screen.frame.minX, dy: -screen.frame.minY)
        }
    }

    /// Recorta a imagem congelada usando um retângulo em pontos locais da tela.
    static func crop(_ snapshot: ScreenSnapshot, to rect: CGRect) -> CGImage? {
        let scale = CGFloat(snapshot.image.width) / snapshot.screen.frame.width
        let height = snapshot.screen.frame.height
        let pixelRect = CGRect(x: rect.minX * scale,
                               y: (height - rect.maxY) * scale,
                               width: rect.width * scale,
                               height: rect.height * scale).integral
        return snapshot.image.cropping(to: pixelRect)
    }
}
