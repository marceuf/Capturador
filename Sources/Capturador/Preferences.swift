import AppKit
import ServiceManagement

enum ImageFormat: String, CaseIterable, Identifiable {
    case png, jpg
    var id: String { rawValue }
    var label: String { rawValue.uppercased() }
}

/// Preferências persistidas em UserDefaults.
final class Preferences: ObservableObject {
    static let shared = Preferences()
    private let defaults = UserDefaults.standard

    /// Pasta das capturas. Fora da pasta Imagens, o sandbox só permite acesso a uma pasta
    /// que o usuário escolheu; o marcador de segurança guarda essa permissão entre aberturas.
    @Published var saveFolder: URL {
        didSet {
            oldValue.stopAccessingSecurityScopedResource()
            let bookmark = try? saveFolder.bookmarkData(options: .withSecurityScope,
                                                        includingResourceValuesForKeys: nil, relativeTo: nil)
            defaults.set(bookmark, forKey: "saveFolderBookmark")
            _ = saveFolder.startAccessingSecurityScopedResource()
        }
    }
    @Published var format: ImageFormat {
        didSet { defaults.set(format.rawValue, forKey: "format") }
    }
    @Published var copyAfterSave: Bool {
        didSet { defaults.set(copyAfterSave, forKey: "copyAfterSave") }
    }
    @Published var launchAtLogin: Bool {
        didSet { applyLaunchAtLogin() }
    }
    /// Atalho de cada ação; uma ação ausente está desativada.
    @Published var shortcuts: [HotKeyAction: Shortcut] {
        didSet { saveShortcuts() }
    }

    private init() {
        let defaultFolder = Preferences.realHome.appendingPathComponent("Pictures/Capturador", isDirectory: true)
        saveFolder = Preferences.restoreFolder(from: defaults) ?? defaultFolder
        format = ImageFormat(rawValue: defaults.string(forKey: "format") ?? "") ?? .png
        copyAfterSave = defaults.object(forKey: "copyAfterSave") as? Bool ?? true
        launchAtLogin = SMAppService.mainApp.status == .enabled
        shortcuts = Preferences.loadShortcuts(from: defaults)
    }

    /// Pasta pessoal real. No sandbox, `homeDirectoryForCurrentUser` aponta para o contêiner do app.
    private static var realHome: URL {
        if let entry = getpwuid(getuid()), let dir = entry.pointee.pw_dir {
            return URL(fileURLWithPath: String(cString: dir), isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
    }

    /// Reabre a pasta escolhida pelo marcador de segurança; sem ele, o sandbox negaria o acesso.
    private static func restoreFolder(from defaults: UserDefaults) -> URL? {
        guard let data = defaults.data(forKey: "saveFolderBookmark") else { return nil }
        var stale = false
        guard let url = try? URL(resolvingBookmarkData: data, options: .withSecurityScope,
                                 relativeTo: nil, bookmarkDataIsStale: &stale) else { return nil }
        if stale, let fresh = try? url.bookmarkData(options: .withSecurityScope,
                                                    includingResourceValuesForKeys: nil, relativeTo: nil) {
            defaults.set(fresh, forKey: "saveFolderBookmark")
        }
        _ = url.startAccessingSecurityScopedResource()
        return url
    }

    /// Formato salvo: {"capture": {"keyCode":0,"modifiers":4352}, "ocr": null, ...}.
    /// `null` marca uma ação desativada; uma ação sem entrada usa o atalho padrão.
    private static func loadShortcuts(from defaults: UserDefaults) -> [HotKeyAction: Shortcut] {
        var saved: [String: Shortcut?] = [:]
        if let data = defaults.data(forKey: "shortcuts"),
           let decoded = try? JSONDecoder().decode([String: Shortcut?].self, from: data) {
            saved = decoded
        }
        var result: [HotKeyAction: Shortcut] = [:]
        for action in HotKeyAction.allCases {
            if let entry = saved[action.rawValue] {
                if let shortcut = entry { result[action] = shortcut }
            } else {
                result[action] = action.defaultShortcut
            }
        }
        return result
    }

    private func saveShortcuts() {
        var out: [String: Shortcut?] = [:]
        for action in HotKeyAction.allCases { out[action.rawValue] = shortcuts[action] }
        if let data = try? JSONEncoder().encode(out) { defaults.set(data, forKey: "shortcuts") }
    }

    private func applyLaunchAtLogin() {
        do {
            if launchAtLogin {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            NSLog("Capturador: falha ao alterar abertura no login: \(error)")
        }
    }

    /// Garante que a pasta exista e devolve um URL novo e único para o arquivo.
    func nextFileURL(extension ext: String, prefix: String = "Captura") -> URL {
        try? FileManager.default.createDirectory(at: saveFolder, withIntermediateDirectories: true)
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "pt_BR")
        formatter.dateFormat = "yyyy-MM-dd 'às' HH.mm.ss"
        let base = "\(prefix) \(formatter.string(from: Date()))"
        var url = saveFolder.appendingPathComponent("\(base).\(ext)")
        var n = 2
        while FileManager.default.fileExists(atPath: url.path) {
            url = saveFolder.appendingPathComponent("\(base) (\(n)).\(ext)")
            n += 1
        }
        return url
    }
}
