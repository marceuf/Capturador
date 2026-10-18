import AppKit
import Carbon.HIToolbox
import SwiftUI

/// Campo que grava a próxima combinação de teclas para uma ação.
/// Esc cancela; ⌫ sem modificadores desativa o atalho.
struct ShortcutRecorder: View {
    let action: HotKeyAction
    @ObservedObject var prefs = Preferences.shared
    @ObservedObject var hotKeys = HotKeyCenter.shared
    @State private var recording = false
    @State private var monitor: Any?
    @State private var message: String?

    var body: some View {
        VStack(alignment: .trailing, spacing: 2) {
            HStack(spacing: 6) {
                Button(action: toggle) {
                    Text(label)
                        .frame(minWidth: 110)
                        .foregroundStyle(recording ? Color.accentColor : .primary)
                }
                Button {
                    stop()
                    prefs.shortcuts[action] = action.defaultShortcut
                } label: {
                    Image(systemName: "arrow.counterclockwise")
                }
                .help("Restaurar padrão (\(action.defaultShortcut.display))")
                .disabled(prefs.shortcuts[action] == action.defaultShortcut)
            }
            if let message {
                Text(message).font(.caption).foregroundStyle(.orange)
            } else if hotKeys.failed.contains(action) {
                Text("Em uso por outro app — escolha outro").font(.caption).foregroundStyle(.red)
            }
        }
        .onDisappear(perform: stop)
    }

    private var label: String {
        if recording { return "Pressione o atalho…" }
        return prefs.shortcuts[action]?.display ?? "Desativado"
    }

    private func toggle() {
        recording ? stop() : start()
    }

    private func start() {
        message = nil
        recording = true
        HotKeyCenter.shared.suspend()
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            handle(event)
            return nil
        }
    }

    private func stop() {
        guard recording else { return }
        recording = false
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        HotKeyCenter.shared.resume()
    }

    private func handle(_ event: NSEvent) {
        let flags = event.modifierFlags.intersection([.command, .control, .option, .shift])
        switch (Int(event.keyCode), flags.isEmpty) {
        case (kVK_Escape, true):
            stop()
            return
        case (kVK_Delete, true), (kVK_ForwardDelete, true):
            prefs.shortcuts[action] = nil
            stop()
            return
        default:
            break
        }
        guard let shortcut = Shortcut(event: event) else {
            message = "Use ⌃, ⌥ ou ⌘ (ou uma tecla F)"
            NSSound.beep()
            return
        }
        if let other = HotKeyAction.allCases.first(where: { $0 != action && prefs.shortcuts[$0] == shortcut }) {
            message = "Já usado em “\(other.title)”"
            NSSound.beep()
            return
        }
        message = nil
        prefs.shortcuts[action] = shortcut
        stop()
    }
}
