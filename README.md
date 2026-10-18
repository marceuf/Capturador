# Capturador

App de captura de tela para a barra de menus do macOS (15+). Usa só frameworks da Apple:
ScreenCaptureKit, Vision, Core Image e Carbon.

## Instalar

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

## Atalhos

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

## Licença

MIT © 2026 Marcéu Farias. Veja [LICENSE](LICENSE).
