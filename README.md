# sound-switch

App pessoal para Windows que transforma o teclado MIDI **BK492** em mixer de som e seletor de saída,
substituindo três programas que hoje fazem esse trabalho juntos:

| Hoje | Função | No app novo |
|---|---|---|
| MIDI Mixer 2.7.3 | Faders → volume do EDIFIER, do app em foco e dos Fones de ouvido + popup | Volume por dispositivo e por app em foco, com overlay próprio |
| Bome's MIDI Translator Classic 1.61 | Botão MIDI (CC20) → simula Ctrl+Alt+S | Desnecessário: o botão aciona a troca direto |
| SoundSwitch 7.3.3 | Ctrl+Alt+S → alterna dispositivo padrão | Troca de dispositivo padrão via API do Windows |

**Status:** em uso diário. Os três faders e o botão de troca de saída funcionam, com overlay; o app está
instalado, inicia com o Windows e substituiu MIDI Mixer, SoundSwitch e Bome (desinstalados).

## Baixar

Executável pronto (Windows 10/11 x64, sem .NET, sem admin) em
[Releases](https://github.com/ImCarL6/sound-switch/releases/latest). Instruções de instalação e de
config na página da release.

## Instalar / atualizar a partir do código

```bash
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

Roda os testes, gera um `.exe` único (autocontido) em `%LOCALAPPDATA%\Programs\sound-switch`, troca a cópia
em execução e registra a inicialização com o Windows (desligável na bandeja ou em `app.startWithWindows`).

Para desenvolver sem mexer na cópia instalada (feche-a antes pela bandeja → Sair):

```bash
dotnet run --project src/SoundSwitch.App
```

Config e logs em `%APPDATA%\sound-switch\` (criado na primeira execução a partir de
`config/config.example.json`). Testes: `dotnet test`.

## O que o app faz

| Controle BK492 (canal 1) | Ação |
|---|---|
| Fader CC82 | Volume do **EDIFIER** (CS43131+SGM8262 HIFI Audio) |
| Fader CC83 | Volume do **app em primeiro plano** |
| Fader CC28 | Volume dos **Fones de ouvido** (Realtek(R) Audio) |
| Botão CC20 (127) | Alterna a saída padrão **EDIFIER ⇄ Fones de ouvido** |

- Volume **linear e exato**: fader no mínimo = mudo, no máximo = 100%, posição X% = X% no Windows.
- Overlay em tempo real (alvo, porcentagem, barra), sem roubar o foco.
- Roda na bandeja, inicia com o Windows, config em JSON com recarga automática.

## Documentação

1. [Análise do setup atual](docs/01-analise-setup-atual.md) — o que cada app faz hoje, configs, IDs, achados.
2. [Requisitos](docs/02-requisitos.md) — funcionais, não funcionais e critérios de aceite.
3. [Arquitetura](docs/03-arquitetura.md) — stack (.NET 10 + WPF + NAudio), componentes, threads, APIs do Windows.
4. [Volume e calibração](docs/04-volume-e-calibracao.md) — a fórmula de mapeamento fader → volume.
5. [Overlay](docs/05-overlay.md) — especificação do feedback na tela.
6. [Roadmap](docs/06-roadmap.md) — fases, migração dos apps antigos, checklist de testes.

Exemplo de config com o setup atual: [config/config.example.json](config/config.example.json).

## Stack

C# / .NET 10 (`net10.0-windows`), WPF, NAudio.Wasapi (Core Audio), WinMM direto para MIDI, xUnit.
Para compilar: .NET SDK 10. O executável publicado não precisa de nada instalado.

## Decisões em aberto

- **Nome do app.** "sound-switch" é provisório (dados ficam em `%APPDATA%\sound-switch\` para não
  colidir com o SoundSwitch original).
- **Fader do app em foco ao trocar de janela.** Padrão: não aplica nada até o fader se mover (igual ao
  MIDI Mixer). "Soft takeover" fica como extra.

## Licença

[MIT](LICENSE) © 2026 Carlos Aparecido.
