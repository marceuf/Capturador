import AppKit
import Carbon.HIToolbox

/// Ações que podem ter atalho global.
enum HotKeyAction: String, CaseIterable, Identifiable {
    case capture, ocr, record

    var id: String { rawValue }

    var title: String {
        switch self {
        case .capture: return "Capturar área ou janela"
        case .ocr: return "Extrair texto (OCR)"
        case .record: return "Iniciar/parar gravação"
        }
    }

    var defaultShortcut: Shortcut {
        let ctrlCmd = UInt32(controlKey | cmdKey)
        switch self {
        case .capture: return Shortcut(keyCode: UInt32(kVK_ANSI_A), modifiers: UInt32(shiftKey | cmdKey))
        case .ocr: return Shortcut(keyCode: UInt32(kVK_ANSI_O), modifiers: ctrlCmd)
        case .record: return Shortcut(keyCode: UInt32(kVK_ANSI_R), modifiers: ctrlCmd)
        }
    }

    fileprivate var carbonID: UInt32 { UInt32(HotKeyAction.allCases.firstIndex(of: self)! + 1) }
}

/// Combinação de tecla, com modificadores no formato Carbon.
struct Shortcut: Codable, Equatable {
    let keyCode: UInt32
    let modifiers: UInt32

    init(keyCode: UInt32, modifiers: UInt32) {
        self.keyCode = keyCode
        self.modifiers = modifiers
    }

    /// Converte um evento de teclado; devolve nil se a combinação não serve como atalho global.
    init?(event: NSEvent) {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        var carbon: UInt32 = 0
        if flags.contains(.control) { carbon |= UInt32(controlKey) }
        if flags.contains(.option) { carbon |= UInt32(optionKey) }
        if flags.contains(.shift) { carbon |= UInt32(shiftKey) }
        if flags.contains(.command) { carbon |= UInt32(cmdKey) }
        let code = UInt32(event.keyCode)
        // Sem ⌃, ⌥ ou ⌘ só aceitamos teclas de função, para não sequestrar a digitação normal.
        let strong = UInt32(controlKey | optionKey | cmdKey)
        guard carbon & strong != 0 || Shortcut.functionKeys.contains(Int(code)) else { return nil }
        self.init(keyCode: code, modifiers: carbon)
    }

    var display: String {
        var s = ""
        if modifiers & UInt32(controlKey) != 0 { s += "⌃" }
        if modifiers & UInt32(optionKey) != 0 { s += "⌥" }
        if modifiers & UInt32(shiftKey) != 0 { s += "⇧" }
        if modifiers & UInt32(cmdKey) != 0 { s += "⌘" }
        return s + Shortcut.keyName(Int(keyCode))
    }

    private static let functionKeys: Set<Int> = [
        kVK_F1, kVK_F2, kVK_F3, kVK_F4, kVK_F5, kVK_F6, kVK_F7, kVK_F8, kVK_F9, kVK_F10,
        kVK_F11, kVK_F12, kVK_F13, kVK_F14, kVK_F15, kVK_F16, kVK_F17, kVK_F18, kVK_F19, kVK_F20
    ]

    private static let specialNames: [Int: String] = [
        kVK_Return: "↩", kVK_Tab: "⇥", kVK_Space: "Espaço", kVK_Delete: "⌫", kVK_ForwardDelete: "⌦",
        kVK_Escape: "⎋", kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_UpArrow: "↑", kVK_DownArrow: "↓",
        kVK_Home: "↖", kVK_End: "↘", kVK_PageUp: "⇞", kVK_PageDown: "⇟", kVK_ANSI_KeypadEnter: "⌤",
        kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6",
        kVK_F7: "F7", kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12",
        kVK_F13: "F13", kVK_F14: "F14", kVK_F15: "F15", kVK_F16: "F16", kVK_F17: "F17",
        kVK_F18: "F18", kVK_F19: "F19", kVK_F20: "F20"
    ]

    /// Nome da tecla no layout de teclado atual (por exemplo, "Ç" num teclado ABNT2).
    private static func keyName(_ code: Int) -> String {
        if let name = specialNames[code] { return name }
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let raw = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else {
            return "#\(code)"
        }
        let data = Unmanaged<CFData>.fromOpaque(raw).takeUnretainedValue() as Data
        var deadKeys: UInt32 = 0
        var chars = [UniChar](repeating: 0, count: 4)
        var length = 0
        let status = data.withUnsafeBytes { buffer -> OSStatus in
            let layout = buffer.bindMemory(to: UCKeyboardLayout.self).baseAddress!
            return UCKeyTranslate(layout, UInt16(code), UInt16(kUCKeyActionDisplay), 0, UInt32(LMGetKbdType()),
                                  OptionBits(kUCKeyTranslateNoDeadKeysBit), &deadKeys, chars.count, &length, &chars)
        }
        guard status == noErr, length > 0 else { return "#\(code)" }
        return String(utf16CodeUnits: chars, count: length).uppercased()
    }
}

/// Atalhos globais via Carbon `RegisterEventHotKey` (não exige permissão de Acessibilidade).
final class HotKeyCenter: ObservableObject {
    static let shared = HotKeyCenter()

    /// Ações cujo atalho o sistema recusou (geralmente porque outro app já o usa).
    @Published private(set) var failed: Set<HotKeyAction> = []

    private var handlers: [HotKeyAction: () -> Void] = [:]
    private var refs: [HotKeyAction: EventHotKeyRef] = [:]
    private var current: [HotKeyAction: Shortcut] = [:]
    private var suspended = false

    private init() { installHandler() }

    func bind(_ action: HotKeyAction, handler: @escaping () -> Void) {
        handlers[action] = handler
    }

    /// Substitui todos os atalhos registrados pelos informados.
    func apply(_ shortcuts: [HotKeyAction: Shortcut]) {
        current = shortcuts
        if !suspended { registerAll() }
    }

    /// Libera os atalhos enquanto o usuário grava um novo, para que a combinação chegue ao gravador.
    func suspend() {
        suspended = true
        unregisterAll()
    }

    func resume() {
        suspended = false
        registerAll()
    }

    private func registerAll() {
        unregisterAll()
        var failures: Set<HotKeyAction> = []
        for (action, shortcut) in current {
            var ref: EventHotKeyRef?
            let id = EventHotKeyID(signature: OSType(0x4341_5054), id: action.carbonID) // "CAPT"
            let status = RegisterEventHotKey(shortcut.keyCode, shortcut.modifiers, id,
                                             GetApplicationEventTarget(), 0, &ref)
            if status == noErr, let ref {
                refs[action] = ref
            } else {
                failures.insert(action)
                NSLog("Capturador: atalho \(shortcut.display) indisponível (status \(status))")
            }
        }
        failed = failures
    }

    private func unregisterAll() {
        refs.values.forEach { UnregisterEventHotKey($0) }
        refs.removeAll()
    }

    private func installHandler() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var hotKeyID = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &hotKeyID)
            let id = hotKeyID.id
            DispatchQueue.main.async {
                let center = HotKeyCenter.shared
                if let action = HotKeyAction.allCases.first(where: { $0.carbonID == id }) {
                    center.handlers[action]?()
                }
            }
            return noErr
        }, 1, &spec, nil, nil)
    }
}
