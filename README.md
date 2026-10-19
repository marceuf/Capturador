# Capturador

App de captura de tela com anotação, OCR e gravação, para **macOS** (barra de menus) e
**Windows** (bandeja, ao lado do relógio). Tudo roda no computador: sem internet, sem conta.

- **macOS 15+:** frameworks da Apple (ScreenCaptureKit, Vision, Core Image e Carbon). Instalação
  compilando o código, abaixo.
- **Windows 10 (22H2) e 11:** app nativo em C#/.NET, com instalador pronto. Veja [Windows](#windows).

## Instalar no macOS

Requisitos: macOS 15 ou mais novo e as ferramentas de linha de comando do Xcode
(`xcode-select --install`) ou o Xcode completo.

    git clone https://github.com/marceuf/Capturador.git
    cd Capturador
    ./build.sh --install     # compila, gera build.noindex/Capturador.app e copia para ~/Applications

O app aparece na barra de menus (não no Dock). Na primeira captura, conceda **Gravação de Tela** em
Ajustes do Sistema › Privacidade e Segurança e reabra o app.

O `build.sh` assina com o seu certificado "Apple Development", se houver um no chaveiro; sem ele, usa
assinatura local (ad hoc). Nesse caso o macOS pode pedir a permissão de Gravação de Tela de novo a cada
nova compilação.

Para atualizar: `git pull` e `./build.sh --install` de novo. Para desinstalar, apague
`~/Applications/Capturador.app`.

## Atalhos no macOS

Os atalhos globais são configuráveis em **Ajustes… › Atalhos globais**: clique no atalho e pressione a
nova combinação (precisa de ⌃, ⌥ ou ⌘, ou ser uma tecla F). Esc cancela, ⌫ desativa e ↺ restaura o padrão.

| Ação | Padrão |
| --- | --- |
| Capturar área ou janela | ⇧⌘A |
| Extrair texto (OCR) | ⌃⌘O |
| Iniciar/parar gravação | ⌃⌘R |

Na seleção, todas as telas são congeladas; a área fica na tela onde o arraste começou.
Na seleção: arraste = área · clique = janela sob o cursor · Enter = tela inteira · Esc = cancelar.
Na gravação: depois de escolher a área, uma contagem 3, 2, 1 aparece sobre ela (Esc ou ⌃⌘R cancelam).
Durante a gravação, uma moldura vermelha marca a área e um controle abaixo dela mostra o tempo e o botão
Parar; nenhum dos dois entra no vídeo. Também dá para parar com ⌃⌘R ou pelo menu.

Depois da seleção, a captura fica temporária, flutuando sobre a área capturada, com uma cápsula de
ferramentas logo abaixo (arraste a cápsula para soltá-la da imagem). Nada é salvo até você pedir.

No editor: 1–9 ou V R O A L P T H B = ferramenta · clique numa marcação com Selecionar para mover,
redimensionar pelas alças, trocar cor/espessura ou apagar (⌫) · duplo clique no texto para reeditar ·
⌘Z/⇧⌘Z desfazer/refazer · ⌘C ou Enter copiar (a captura voa para o canto e some) · ⌘S salvar ·
⇧⌘S salvar como · ⌘⌫ ou Esc descartar (Esc primeiro cancela o texto ou a seleção).

O Desfoque (B) borra e pixeliza a área em blocos grandes, o que impede ler o texto embaixo. Se o filtro
falhar, a área fica preta; se a imagem final não puder ser gerada, nada é copiado nem salvo.

## Área de transferência

Copiar no editor (⌘C ou Enter) põe a imagem na área de transferência. O OCR copia o texto reconhecido
automaticamente assim que termina. Com a Área de Transferência Universal ligada, o que for copiado também
fica disponível nos seus outros dispositivos Apple.

## Windows

### Instalar

1. Abra a página de [versões](https://github.com/marceuf/Capturador/releases) e baixe o instalador
   do seu computador:
   - `Capturador-<versão>-x64-setup.exe` para a maioria dos PCs (Intel ou AMD);
   - `Capturador-<versão>-arm64-setup.exe` para PCs com processador ARM (Snapdragon, por exemplo).
2. Abra o arquivo baixado. Como o instalador ainda não tem assinatura digital, o Windows mostra
   **"O Windows protegeu o computador"**. Clique em **Mais informações** e depois em
   **Executar assim mesmo**.
3. Siga o instalador. Ele instala só para o seu usuário e não pede senha de administrador.

O Capturador fica na bandeja, ao lado do relógio (às vezes dentro da seta **^**). Para conferir o
arquivo baixado, compare o SHA-256 com o `SHA256SUMS.txt` da mesma versão.

Para desinstalar: Configurações › Aplicativos › Aplicativos instalados › Capturador › Desinstalar.

### Atalhos

| Ação | Padrão |
| --- | --- |
| Capturar área ou janela | Win+Shift+X |
| Extrair texto (OCR) | Win+Shift+O |
| Iniciar/parar gravação | Win+Shift+G |

Troque em **Ajustes…**, no menu do ícone. Atalhos com Ctrl+Alt+letra não são aceitos: no Windows
eles equivalem a AltGr e bloqueariam letras acentuadas.

O editor, a seleção e a gravação funcionam como no Mac, com Ctrl no lugar de ⌘ (Ctrl+C copia,
Ctrl+S salva, Ctrl+Z desfaz, Ctrl+W descarta). As gravações são salvas em MP4. O OCR usa o
reconhecimento de texto do Windows: se não houver idioma com OCR instalado, a janela de OCR
explica como adicionar o Português (Brasil).

### Compilar

Requisitos: .NET SDK 10. O código fica em `windows/`.

    dotnet test windows/tests/Capturador.Core.Tests
    dotnet publish windows/src/Capturador -c Release -r win-x64 -o out/x64

Os instaladores das versões são gerados pelo GitHub Actions (`.github/workflows/windows-release.yml`)
com o [Inno Setup](https://jrsoftware.org/isinfo.php) a partir de `windows/installer/Capturador.iss`.

## Licença

MIT © 2026 Marcéu Farias. Veja [LICENSE](LICENSE).
