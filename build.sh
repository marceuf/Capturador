#!/bin/bash
# Compila o Capturador e monta Capturador.app. Uso: ./build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"

swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/Capturador"

# A extensão .noindex impede o Spotlight de listar esta cópia ao lado do app instalado.
APP="build.noindex/Capturador.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Capturador"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

# Assina sempre com a mesma identidade "Apple Development", para que a permissão de Gravação
# de Tela continue valendo entre compilações. A escolha (hash SHA-1) fica em .signing-identity;
# defina SIGN_IDENTITY para trocar. Sem identidade disponível, usa assinatura ad hoc.
if [[ -n "${SIGN_IDENTITY:-}" ]]; then
    IDENTITY="$SIGN_IDENTITY"
elif [[ -s .signing-identity ]]; then
    IDENTITY="$(cat .signing-identity)"
else
    IDENTITY="$(security find-identity -v -p codesigning | awk '/Apple Development/ {print $2}' | sort | head -1)"
    [[ -n "$IDENTITY" ]] && echo "$IDENTITY" > .signing-identity
fi
codesign --force --options runtime --timestamp=none --entitlements Resources/Capturador.entitlements \
    --sign "${IDENTITY:--}" "$APP"
echo "App criado: $PWD/$APP (assinatura: ${IDENTITY:-ad hoc})"

if [[ "${1:-}" == "--install" ]]; then
    mkdir -p "$HOME/Applications"
    pkill -x Capturador 2>/dev/null || true
    rm -rf "$HOME/Applications/Capturador.app"
    cp -R "$APP" "$HOME/Applications/"
    open "$HOME/Applications/Capturador.app"
    echo "Instalado em ~/Applications/Capturador.app"
fi
