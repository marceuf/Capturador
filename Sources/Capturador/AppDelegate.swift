import AppKit
import Combine

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var actionItems: [HotKeyAction: NSMenuItem] = [:]
    private let recorder = ScreenRecorder()
    private var recordingStarting = false
    private var busy = false
    private var subscriptions: Set<AnyCancellable> = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildStatusItem()
        let hotKeys = HotKeyCenter.shared
        hotKeys.bind(.capture) { [weak self] in self?.captureArea() }
        hotKeys.bind(.ocr) { [weak self] in self?.captureText() }
        hotKeys.bind(.record) { [weak self] in self?.toggleRecording() }
        // Reaplica os atalhos e atualiza o menu sempre que mudarem nos Ajustes.
        Preferences.shared.$shortcuts
            .sink { [weak self] shortcuts in
                hotKeys.apply(shortcuts)
                self?.refreshMenuTitles(shortcuts)
            }
            .store(in: &subscriptions)
        recorder.onStateChange = { [weak self] recording in
            if !recording { RecordingHUD.hide() }
            self?.refreshMenuTitles(Preferences.shared.shortcuts)
        }

        if !ScreenCapturer.hasPermission() {
            ScreenCapturer.requestPermission()
        }
    }

    // Abrir o app de novo (Finder, Spotlight) mostra os Ajustes, mesmo se o ícone da barra estiver oculto.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        SettingsWindow.show()
        return false
    }

    // MARK: Menu da barra

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "camera.viewfinder", accessibilityDescription: "Capturador")

        let menu = NSMenu()
        actionItems[.capture] = item("", "camera.viewfinder", #selector(captureArea))
        actionItems[.ocr] = item("", "text.viewfinder", #selector(captureText))
        actionItems[.record] = item("", "record.circle", #selector(toggleRecording))
        menu.addItem(actionItems[.capture]!)
        menu.addItem(item("Capturar tela inteira (sob o cursor)", "display", #selector(captureFullScreen)))
        menu.addItem(actionItems[.ocr]!)
        menu.addItem(actionItems[.record]!)
        menu.addItem(.separator())
        menu.addItem(item("Abrir imagem para editar…", "photo", #selector(importImage)))
        menu.addItem(item("Abrir pasta de capturas", "folder", #selector(openFolder)))
        menu.addItem(item("Ajustes…", "gearshape", #selector(openSettings)))
        menu.addItem(item("Sobre o Capturador", "info.circle", #selector(showAbout)))
        menu.addItem(.separator())
        menu.addItem(item("Sair", "power", #selector(quit)))
        statusItem.menu = menu
    }

    private func item(_ title: String, _ symbol: String, _ action: Selector) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
        item.target = self
        return item
    }

    // MARK: Ações

    @objc func captureArea() {
        select(hint: "Captura") { snapshot, rect in
            guard let image = ScreenCapturer.crop(snapshot, to: rect) else { return }
            let global = rect.offsetBy(dx: snapshot.screen.frame.minX, dy: snapshot.screen.frame.minY)
            EditorWindow.show(image: image, pointSize: rect.size, near: global)
        }
    }

    @objc func captureFullScreen() {
        guard !busy else { return }
        busy = true
        Task { @MainActor in
            defer { busy = false }
            do {
                let snapshot = try await ScreenCapturer.snapshotUnderMouse()
                EditorWindow.show(image: snapshot.image, pointSize: snapshot.screen.frame.size, near: snapshot.screen.frame)
            } catch {
                present(error)
            }
        }
    }

    @objc func captureText() {
        select(hint: "Selecione o texto para OCR") { snapshot, rect in
            guard let image = ScreenCapturer.crop(snapshot, to: rect) else { return }
            OCRService.recognizeAndShow(image)
        }
    }

    @objc func toggleRecording() {
        // Entre o fim da contagem e o início real do stream, ignora: uma segunda gravação
        // ficaria sem controle para parar a primeira.
        if recordingStarting { return }
        if recorder.isRecording {
            Task { await recorder.stop() }
            return
        }
        // O mesmo atalho durante a contagem desiste de gravar.
        if RecordingHUD.isCountingDown {
            RecordingHUD.cancelCountdown()
            return
        }
        select(hint: "Selecione a área para gravar") { [weak self] snapshot, rect in
            let global = rect.offsetBy(dx: snapshot.screen.frame.minX, dy: snapshot.screen.frame.minY)
            RecordingHUD.countdown(over: global) { go in
                guard go, let self else { return }
                self.recordingStarting = true
                Task {
                    do {
                        try await self.recorder.start(snapshot: snapshot, rect: rect)
                        await MainActor.run {
                            self.recordingStarting = false
                            RecordingHUD.showRecording(around: global) { [weak self] in self?.toggleRecording() }
                        }
                    } catch {
                        await MainActor.run {
                            self.recordingStarting = false
                            self.present(error)
                        }
                    }
                }
            }
        }
    }

    @objc func importImage() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image]
        NSApp.activate(ignoringOtherApps: true)
        guard panel.runModal() == .OK, let url = panel.url,
              let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return }
        let scale = NSScreen.main?.backingScaleFactor ?? 2
        EditorWindow.show(image: image, pointSize: CGSize(width: CGFloat(image.width) / scale,
                                                          height: CGFloat(image.height) / scale))
    }

    @objc func openFolder() {
        let folder = Preferences.shared.saveFolder
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        NSWorkspace.shared.open(folder)
    }

    @objc func openSettings() { SettingsWindow.show() }

    @objc func showAbout() {
        let credits = NSMutableAttributedString(
            string: "Desenvolvido por Marcéu Farias",
            attributes: [.font: NSFont.systemFont(ofSize: NSFont.smallSystemFontSize),
                         .foregroundColor: NSColor.secondaryLabelColor])
        credits.setAlignment(.center, range: NSRange(location: 0, length: credits.length))
        NSApp.activate(ignoringOtherApps: true)
        NSApp.orderFrontStandardAboutPanel(options: [
            .credits: credits,
            NSApplication.AboutPanelOptionKey(rawValue: "Copyright"): "© 2026 Marcéu Farias"
        ])
    }
    @objc func quit() { NSApp.terminate(nil) }

    // MARK: Auxiliares

    /// Congela todas as telas e deixa o usuário escolher uma área ou janela em qualquer uma delas.
    private func select(hint: String, _ body: @escaping (ScreenSnapshot, CGRect) -> Void) {
        guard !busy else { return }
        busy = true
        Task { @MainActor in
            do {
                let snapshots = try await ScreenCapturer.snapshotAllScreens()
                SelectionOverlay.select(on: snapshots, hint: hint) { [weak self] result in
                    self?.busy = false
                    guard let (snapshot, rect) = result else { return }
                    body(snapshot, rect)
                }
            } catch {
                busy = false
                present(error)
            }
        }
    }

    private func refreshMenuTitles(_ shortcuts: [HotKeyAction: Shortcut]) {
        let recording = recorder.isRecording
        statusItem.button?.image = NSImage(systemSymbolName: recording ? "stop.circle.fill" : "camera.viewfinder",
                                           accessibilityDescription: "Capturador")
        statusItem.button?.contentTintColor = recording ? .systemRed : nil
        for (action, item) in actionItems {
            var title = action == .record ? (recording ? "Parar gravação" : "Gravar tela") : action.title
            if let shortcut = shortcuts[action] { title += "    \(shortcut.display)" }
            item.title = title
        }
        actionItems[.record]?.image = NSImage(systemSymbolName: recording ? "stop.circle" : "record.circle",
                                              accessibilityDescription: nil)
    }

    private func present(_ error: Error) {
        let alert = NSAlert()
        alert.messageText = "Não foi possível capturar"
        alert.informativeText = error.localizedDescription
        if error is CaptureError {
            alert.addButton(withTitle: "Abrir Ajustes do Sistema")
            alert.addButton(withTitle: "Cancelar")
            NSApp.activate(ignoringOtherApps: true)
            if alert.runModal() == .alertFirstButtonReturn { ScreenCapturer.openPermissionSettings() }
        } else {
            NSApp.activate(ignoringOtherApps: true)
            alert.runModal()
        }
    }
}
