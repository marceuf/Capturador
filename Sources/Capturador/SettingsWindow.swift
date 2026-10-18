import AppKit
import SwiftUI

struct SettingsView: View {
    @ObservedObject var prefs = Preferences.shared

    var body: some View {
        Form {
            Section("Geral") {
                Toggle("Abrir ao iniciar sessão no Mac", isOn: $prefs.launchAtLogin)
                Toggle("Copiar também ao salvar", isOn: $prefs.copyAfterSave)
            }
            Section("Salvar") {
                Picker("Formato", selection: $prefs.format) {
                    ForEach(ImageFormat.allCases) { Text($0.label).tag($0) }
                }
                LabeledContent("Pasta") {
                    HStack {
                        Text(prefs.saveFolder.path).lineLimit(1).truncationMode(.middle)
                        Button("Escolher…", action: chooseFolder)
                        Button("Abrir") {
                            try? FileManager.default.createDirectory(at: prefs.saveFolder, withIntermediateDirectories: true)
                            NSWorkspace.shared.open(prefs.saveFolder)
                        }
                    }
                }
            }
            Section {
                ForEach(HotKeyAction.allCases) { action in
                    LabeledContent(action.title) { ShortcutRecorder(action: action) }
                }
            } header: {
                Text("Atalhos globais")
            } footer: {
                Text("Clique no atalho e pressione a nova combinação. Esc cancela, ⌫ desativa.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Permissões") {
                HStack {
                    Text(ScreenCapturer.hasPermission() ? "Gravação de Tela: concedida" : "Gravação de Tela: não concedida")
                    Spacer()
                    Button("Abrir Ajustes do Sistema") { ScreenCapturer.openPermissionSettings() }
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 560, height: 500)
    }

    private func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.directoryURL = prefs.saveFolder
        if panel.runModal() == .OK, let url = panel.url { prefs.saveFolder = url }
    }
}

enum SettingsWindow {
    private static var window: NSWindow?

    static func show() {
        if window == nil {
            let w = NSWindow(contentViewController: NSHostingController(rootView: SettingsView()))
            w.title = "Ajustes do Capturador"
            w.styleMask = [.titled, .closable]
            w.isReleasedWhenClosed = false
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}
