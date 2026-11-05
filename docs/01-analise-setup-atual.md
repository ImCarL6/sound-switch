# 01 — Análise do setup atual

Levantamento feito em 05/11/2026 direto no PC (arquivos de config, registro do Windows, logs e
APIs de áudio/MIDI).

> **Registro histórico.** Os três apps foram desinstalados e seus restos removidos no mesmo dia; os
> caminhos citados abaixo não existem mais. O comportamento descrito aqui é o que o sound-switch reproduz. Este documento é a "fonte da verdade" do comportamento que o novo app precisa
reproduzir.

## Visão geral do fluxo atual

```
                    ┌─────────────────────────── BK492 (teclado MIDI USB, canal 1) ───────────────────────────┐
                    │  Fader CC82        Fader CC83        Fader CC28        Botão CC20 (valor 127)            │
                    └─────┬─────────────────┬─────────────────┬─────────────────┬──────────────────────────────┘
                          │                 │                 │                 │
                          ▼                 ▼                 ▼                 ▼
                   ┌─────────────── MIDI Mixer 2.7.3 ───────────────┐   Bome's MIDI Translator Classic 1.61
                   │ slider 1 → volume EDIFIER                      │   CC20=127  →  tecla Ctrl+Alt+S
                   │ slider 2 → volume do app em foco               │                 │
                   │ slider 3 → volume Fones de ouvido              │                 ▼
                   │ + popup de volume no canto da tela             │   SoundSwitch 7.3.3 (hotkey global)
                   └────────────────────────────────────────────────┘   alterna dispositivo padrão:
                                                                         EDIFIER ⇄ Fones de ouvido
```

São 3 apps residentes + uma "gambiarra" de tecla de atalho simulada para ligar dois deles.
O app novo substitui os três num único processo, sem atalho de teclado no meio do caminho.

---

## 1. Controlador MIDI — BK492

| Item | Valor |
|---|---|
| Nome do dispositivo (WinMM) | `BK492` (entrada índice 0; também aparece como saída) |
| USB | `USB\VID_1ACC&PID_1A15` |
| Canal MIDI usado | Canal 1 (índice 0 em código) |
| Serviço MIDI | `MidiSrv` (Windows MIDI Services) rodando — permite que **vários apps abram a mesma entrada** ao mesmo tempo. É por isso que hoje MIDI Mixer e Bome escutam o BK492 juntos. |

### Mapa de controles

| Controle físico | Mensagem MIDI | Bytes | Uso atual |
|---|---|---|---|
| Fader "slider 1" | Control Change #82 | `B0 52 vv` | Volume do dispositivo **EDIFIER** |
| Fader "slider 2" | Control Change #83 | `B0 53 vv` | Volume do **app em foco** |
| Fader "slider 3" | Control Change #28 | `B0 1C vv` | Volume do dispositivo **Fones de ouvido** |
| Botão | Control Change #20, valor 127 | `B0 14 7F` | Trocar dispositivo de saída padrão |

Faixa nominal dos faders: 0–127 (7 bits → 128 posições).

### Medição real (Fase 0, 05/11/2026)

Captura com `tools/MidiMonitor`, demais apps fechados. Arquivos:
`tools/MidiMonitor/captures/captura-20261105-142220.csv` e `…-resumo.txt`.

| Controle | Faixa real | Chega a 0 / 127 | Oscilação em repouso | Pico de msgs | Passo máx. entre msgs |
|---|---|---|---|---|---|
| Fader 1 (CC82) | 0 … 127 | sim / sim | nenhuma | 47/s | 3 |
| Fader 2 (CC83) | 0 … 127 | sim / sim | nenhuma | 35/s | 3 |
| Fader 3 (CC28) | 0 … 127 | sim / sim | nenhuma | 23/s | 2 |

| Botão (CC20) | Apertar | Soltar | Duração típica do toque |
|---|---|---|---|
| | `B0 14 7F` (127) | `B0 14 00` (0) | 110–140 ms |

Conclusões:
- **Os faders cobrem a faixa inteira.** 127 só aparece no fim do curso, depois de 124, 125 e 126 em sequência,
  e 0 só aparece no início. Não há zona morta. A calibração padrão `min 0 / max 127` serve para os três.
- **Não há jitter.** Parados, os faders não mandam nenhuma mensagem. O filtro de jitter fica desligado.
- **Valores "pulados"** (ex.: 33, 42, 49 no fader 1) acontecem só em movimento: o teclado amostra o fader
  periodicamente e, quanto mais rápido o movimento, maior o salto (até 3 passos). Como o valor é absoluto,
  isso não afeta a precisão — a posição final é sempre a enviada.
- **Volume de mensagens é baixo** (< 50/s): a coalescência no worker é uma garantia, não uma necessidade.
- **Botão:** disparar só no 127 (como o Bome faz hoje) e ignorar o 0 da soltura. Toques rápidos ficaram
  a ~500 ms um do outro; o debounce de 250 ms não atrapalha.
- **Não medido:** linearidade física (se o meio do curso dá ~64). Fica para o assistente de calibração,
  caso a sensação no uso real indique necessidade.

---

## 2. MIDI Mixer 2.7.3 (Electron)

- Instalado em `%LOCALAPPDATA%\Programs\midi-mixer-app\` (processo de UI Electron + `MIDI Mixer Core.exe` nativo).
- Config em `%APPDATA%\midi-mixer-app\`:
  - `profiles\01KTKJ1VEPXQNK2SNWNWB0256X\spec.json` — perfil "New profile": entrada `BK492`, saída `Microsoft GS Wavetable Synth`.
  - `user-settings.json` — atribuições de cada grupo, `startOnBoot: true`, `startMinimised: true`.

### Perfil (spec.json, resumido)

| Grupo | Control | Tipo | Canal | CC | Faixa |
|---|---|---|---|---|---|
| slider 1 | slider 1 | Fader (`input-fader`) | 0 (=canal 1) | 82 | 0–127 |
| slider 2 | slider 2 | Fader | 0 | 83 | 0–127 |
| slider 3 | slider 3 | Fader | 0 | 28 | 0–127 |

Só a função **Volume** de cada grupo é usada (Mute/Assign/Run/Indicadores vazios).

### Atribuições (user-settings.json) — achado importante

O MIDI Mixer guarda **uma lista de IDs de endpoint por grupo**, acumulada com o tempo, porque o ID do
dispositivo no Windows muda (troca de porta USB, reinstalação de driver, renomear a saída do Realtek):

| Grupo | Alvo exibido | IDs guardados | ID que existe hoje |
|---|---|---|---|
| slider 1 | EDIFIER (CS43131+SGM8262 HIFI Audio) | 9 IDs | `{95fe9aea-8956-4280-9634-89675f289bec}` |
| slider 2 | `focus` (app em primeiro plano) | — | — |
| slider 3 | Fones de ouvido (Realtek(R) Audio) | 4 IDs | `{079f7cf6-1c5d-437f-bf1a-efb781a24f05}` |

➡ **O app novo não pode identificar dispositivos só pelo ID.** Precisa casar por nome amigável,
com o ID apenas como "dica" (ver [arquitetura](03-arquitetura.md#identificação-de-dispositivos)).

### Comportamento observado

- "Focus": ajusta o volume das sessões de áudio do processo em primeiro plano. O log
  (`logs\main.log`) está cheio de `Foreground checking for session ... took longer than expected;
  event overwritten` — a detecção do app em foco é lenta e às vezes descarta eventos. Ponto a melhorar.
- Popup de feedback: card branco no canto superior esquerdo com ícone, nome do grupo ("slider 1") e
  uma barra de progresso. Não mostra porcentagem nem nome do alvo.
- Ruído no log: erros de analytics/Sentry (sem internet para google-analytics) e `PAGE.md` ausente — irrelevante.

---

## 3. SoundSwitch 7.3.3 (.NET)

- Instalado em `C:\Program Files\SoundSwitch\`; config em `%APPDATA%\SoundSwitch\SoundSwitchConfiguration.json`.

| Configuração | Valor | Significado |
|---|---|---|
| `PlaybackHotKey` | Keys 83, Modifier 3 | **Ctrl+Alt+S** → próximo dispositivo de reprodução |
| `RecordingHotKey` | Ctrl+Alt+F7 | Gravação (não usado no fluxo) |
| `MuteRecordingHotKey` | Ctrl+Alt+M | Mute do microfone (dá conflito de hotkey no log; não usado) |
| `CyclerType` | 1 | Ciclar só entre dispositivos **selecionados e conectados** |
| `ChangeCommunications` | false | Troca só os papéis **Console + Multimídia**, não o de Comunicação |
| `SwitchForegroundProgram` | false | Não move o app em foco para o novo dispositivo |
| `KeepVolumeEnabled` | false | Não copia o volume entre dispositivos ao trocar |
| `SwitchDeviceNotification` | 5, `BannerOnScreenTime` 3s | Mostra banner por 3 s ao trocar |
| `AutoAddNewConnectedDevices` | false | Dispositivos novos não entram no ciclo |

### Dispositivos selecionados

Na tela de config aparecem marcados **EDIFIER** e **Fones de ouvido**; o **AI Noise-Canceling Speaker
(ASUS Utility)** está conectado mas desmarcado de propósito. O JSON guarda 4 entradas
(HEADSET, EDIFIER, CLOUD REVOLVER, Fones de ouvido) — nomes antigos da mesma saída Realtek P2 que foi
renomeada ao longo do tempo. **Os IDs guardados no SoundSwitch nem existem mais no Windows**
(`{984f57de…}` e `{c72de32b…}`) e mesmo assim ele funciona — ou seja, ele também casa por nome.

### Comportamento observado (logs)

```
Set Default device: "[eRender]Fones de ouvido (Realtek(R) Audio)"
Set Default device: "[eRender]EDIFIER (CS43131+SGM8262 HIFI Audio)"
```
Com 2 dispositivos ativos o ciclo é um simples alternar EDIFIER ⇄ Fones de ouvido.

---

## 4. Bome's MIDI Translator Classic 1.61

- Instalado em `C:\Program Files (x86)\Bome's Midi Translator Classic\`.
- Preset: `C:\Users\carlo\Documentos\Bome's Midi Translator\confia.bmtp` (caminho no registro
  `HKCU\Software\Bomers\...\Current\CurrentProject`). Entrada `BK492`, saída `Microsoft GS Wavetable Synth`.

Conteúdo do preset (decodificado):

```ini
[Preset.0]
Name=(always active)
Name0=New Translator
Incoming0=MID1B0147F                       ; MIDI B0 14 7F → CC#20 = 127, canal 1
Outgoing0=KAM10100KSQ10005011012053212211  ; keystroke Ctrl+Alt+S
```

Ou seja: **apertar o botão (CC20 = 127) simula Ctrl+Alt+S**, que o SoundSwitch captura.

Fragilidades desse elo:
- Depende de hotkey global — conflita com outros apps e falha se alguma janela elevada (admin) estiver em foco.
- A tecla simulada vai para a janela em foco também (pode disparar atalhos indesejados).
- O app é de 2008.

## 5. Tentativas anteriores encontradas

- **MidiKey2Key** (`C:\Users\carlo\speakers.ini`): mesma ideia do Bome — CC20 = 127 → `Alt+Control+S`.
- **Mididash** (`%LOCALAPPDATA%\Mididash`, vazio).

Confirma que o objetivo sempre foi "botão MIDI → trocar saída", só que sempre via teclado simulado.

---

## 6. Dispositivos de áudio do PC (estado em 05/11/2026)

Saídas **ativas** (`DeviceState = 1`):

| Nome amigável completo | Endpoint ID | Papel no fluxo |
|---|---|---|
| EDIFIER (CS43131+SGM8262 HIFI Audio) | `{0.0.0.00000000}.{95fe9aea-8956-4280-9634-89675f289bec}` | Fader 1 + ciclo de troca |
| Fones de ouvido (Realtek(R) Audio) | `{0.0.0.00000000}.{079f7cf6-1c5d-437f-bf1a-efb781a24f05}` | Fader 3 + ciclo de troca |
| AI Noise-Canceling Speaker (ASUS Utility) | `{0.0.0.00000000}.{8a7d8427-f32c-41df-863e-eac38400aebf}` | **Excluído** de tudo |

Há dezenas de endpoints desconectados/desativados (monitores via NVIDIA HDMI, "USB Audio CODEC",
DualSense, nomes antigos do P2 Realtek como "HYPERX CLOUD REVOLVER"). O app deve ignorar tudo que
não estiver ativo.

## 7. Ambiente de desenvolvimento disponível

| Ferramenta | Versão |
|---|---|
| Windows | 11 Pro 10.0.26200 |
| .NET SDK | 10.0.301 ✅ |
| Node / npm | 24.19 / 11.17 |
| Python | 3.13 |
| Git | 2.55 |
| Rust | não instalado |

O .NET 10 já instalado é o que define a escolha de stack (ver [arquitetura](03-arquitetura.md)).
