# 03 — Arquitetura

## Stack escolhida

| Camada | Escolha | Motivo |
|---|---|---|
| Linguagem/runtime | **C# / .NET 10** (`net10.0-windows`) | SDK já instalado; acesso direto às APIs COM de áudio do Windows; é o que o SoundSwitch usa. |
| UI (overlay, bandeja, config) | **WPF** | Janela transparente/topmost/no-activate sem esforço; DPI per-monitor; animações. Bandeja via `System.Windows.Forms.NotifyIcon` (`UseWindowsForms=true`). |
| Áudio | **NAudio.Wasapi** (`NAudio.CoreAudioApi`) + interop próprio para `IPolicyConfig` | NAudio cobre `MMDeviceEnumerator`, `AudioEndpointVolume`, `AudioSessionManager2`, notificações. `IPolicyConfig` (definir dispositivo padrão) não é documentado e não está no NAudio — ~40 linhas de interop. |
| MIDI | **NAudio.Midi** (WinMM `midiIn*`) | Simples, estável, multi-cliente via `MidiSrv` no Windows 11. Migrar para Windows MIDI Services SDK só se surgir problema. |
| Config | `System.Text.Json` + `FileSystemWatcher` | Recarga a quente. |
| Log | `Microsoft.Extensions.Logging` + sink de arquivo simples (ou Serilog) | — |
| Testes | xUnit | Lógica pura (mapeamento, roteamento, ciclo) testada sem hardware. |
| Distribuição | `dotnet publish` self-contained single-file, `win-x64` | Um `.exe`, sem instalador. |

Alternativas descartadas: Electron/Tauri (precisaria de módulo nativo para as APIs de áudio de
qualquer jeito; mais memória — é exatamente o que o MIDI Mixer faz), Python (distribuição e latência
de COM piores), Rust (não instalado, curva maior para UI).

## Estrutura da solução

```
sound-switch/
├─ src/
│  ├─ SoundSwitch.Core/          ← lógica pura, sem Windows/COM (100% testável)
│  │   ├─ Config/                (modelos + validação do config.json)
│  │   ├─ Mapping/               VolumeMapper (fórmula + calibração), JitterFilter
│  │   ├─ Routing/               MidiRouter: (canal, cc, valor) → Ação
│  │   └─ Switching/             DeviceCycle: escolhe o próximo dispositivo
│  ├─ SoundSwitch.Windows/       ← adaptadores de SO
│  │   ├─ Midi/                  MidiInputService (WinMM, reconexão)
│  │   ├─ Audio/                 EndpointRegistry, EndpointVolumeService, SessionVolumeService,
│  │   │                         DefaultDeviceService (IPolicyConfig)
│  │   └─ Focus/                 ForegroundTracker (SetWinEventHook), ProcessTree
│  └─ SoundSwitch.App/           ← WPF: bootstrap, bandeja, OverlayWindow, telas de monitor/calibração
├─ tests/SoundSwitch.Core.Tests/
├─ tools/MidiMonitor/            ← console app da Fase 0 (opcional, pode virar tela do app)
├─ config/config.example.json
└─ docs/
```

> Nome dos projetos é provisório (`SoundSwitch.*` aqui é o nome do repositório, não o app de terceiros).
> Dados do app ficam em `%APPDATA%\sound-switch\` para não colidir com `%APPDATA%\SoundSwitch\`.

## Fluxo de dados e threads

```
 WinMM callback thread            Audio worker (thread STA/MTA dedicada)         UI thread (WPF)
 ─────────────────────            ─────────────────────────────────────          ───────────────
 midiIn → MidiInputService
   → MidiRouter.Route(msg)
   → Ação { Target, raw }  ──►  "latest value" slot por controle
                                (coalescência: só o último valor importa)
                                   │  sinaliza AutoResetEvent
                                   ▼
                                VolumeMapper → percent
                                EndpointVolume / SessionVolume / DefaultDevice
                                   │
                                   └──► OverlayState (percent, alvo, status) ──► Dispatcher
                                                                                  (render ≤ 1×/frame)
```

Pontos-chave:

- **Callback MIDI não chama COM.** Ele só escreve o último valor num slot (`Interlocked.Exchange`) e
  acorda o worker. Se o fader mandar 50 mensagens em 100 ms e o COM estiver lento, valores intermediários
  são pulados, mas **o último sempre é aplicado** (RNF4).
- **Um único worker de áudio** dono de todos os objetos COM (evita problemas de apartment/threading).
- O botão de troca entra na mesma fila, como evento (não coalescido), com debounce.

## Identificação de dispositivos

Lição do setup atual: IDs de endpoint mudam (o MIDI Mixer acumulou 9 IDs para o EDIFIER; os IDs do
SoundSwitch nem existem mais). Regra de resolução de um alvo do config:

1. Se algum `knownIds` do alvo corresponde a um endpoint **ativo** → usa ele.
2. Senão, procura endpoint **ativo** de saída cujo nome amigável casa com `match`:
   - `match.name` comparado ao nome do endpoint ("EDIFIER", "Fones de ouvido") — exato, sem maiúsc./minúsc.
   - `match.adapter` opcional, comparado ao nome do adaptador ("CS43131+SGM8262 HIFI Audio", "Realtek(R) Audio").
3. Achou por nome com ID novo → usa e **adiciona o ID em `knownIds`** no config (auto-aprendizado, logado).
4. Nada → alvo "desconectado".

`EndpointRegistry` mantém esse mapa em cache e o invalida via `IMMNotificationClient`
(`OnDeviceStateChanged`, `OnDeviceAdded/Removed`, `OnDefaultDeviceChanged`). Nada de enumerar
dispositivos a cada mensagem MIDI.

## Volume de dispositivo

`MMDevice.AudioEndpointVolume`:
- `MasterVolumeLevelScalar = percent / 100f` (com GUID de contexto próprio, para reconhecer e ignorar
  as notificações geradas pelo próprio app).
- `Mute = percent == 0`, desmutar quando `percent > 0`.

## Volume do app em foco

### Detecção do foco (sem polling)

`ForegroundTracker`:
- `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, …, WINEVENT_OUTOFCONTEXT)` na UI thread.
- No evento: `GetWindowThreadProcessId(hwnd)` → PID.
  - Se o processo for `ApplicationFrameHost.exe` (UWP), achar a janela filha `Windows.UI.Core.CoreWindow`
    e usar o PID dela.
  - Se for o próprio processo do app (overlay/config) → **ignorar** e manter o foco anterior.
- Guarda `FocusedApp { Pid, ExePath, DisplayName, Icon }` (nome via `FileVersionInfo.FileDescription`,
  fallback para o nome do exe).

### Casamento de sessões

Ao aplicar o fader 2, o worker:
1. Para cada endpoint de saída ativo, `AudioSessionManager2.Sessions` (lista em cache, atualizada por
   `OnSessionCreated` e invalidada quando o foco muda).
2. Seleciona sessões em que **qualquer** destas vale:
   - `session.ProcessId == FocusedApp.Pid`;
   - caminho do exe da sessão == `FocusedApp.ExePath` (pega Chrome/Edge/Discord, que tocam áudio em processos filhos com o mesmo exe);
   - o processo da sessão é descendente do processo em foco (árvore de pais via `NtQueryInformationProcess`/Toolhelp, em cache).
3. Ignora `IsSystemSoundsSession` e sessões expiradas.
4. `SimpleAudioVolume.Volume = percent / 100f` e `Mute` igual ao dispositivo.

Sem sessão → overlay "sem áudio". Processos protegidos (acesso negado ao caminho do exe, como aparece
no log do SoundSwitch) → casar só por PID.

Isso resolve a lentidão vista no log do MIDI Mixer ("Foreground checking … took longer than expected"):
o foco é resolvido uma vez no evento, não a cada mensagem do fader.

## Troca do dispositivo padrão

`DefaultDeviceService` usa a interface COM não documentada **`IPolicyConfig`**
(CLSID `870af99c-171d-4f9e-af0d-e63df40c2bc9`), a mesma usada por SoundSwitch, EarTrumpet e afins:

```
SetDefaultEndpoint(deviceId, ERole.eConsole)
SetDefaultEndpoint(deviceId, ERole.eMultimedia)
// eCommunications NÃO é alterado (ChangeCommunications=false no setup atual)
```

`DeviceCycle` (lógica pura): dado `cycle = [EDIFIER, Fones de ouvido]`, o padrão atual e quais estão
ativos → retorna o próximo ativo depois do atual (ou o primeiro, se o atual não está na lista).

Risco: por ser não documentada, uma atualização do Windows pode mudar a interface. Mitigação: isolada
num arquivo, com teste manual no checklist de cada atualização grande do Windows.

## Entrada MIDI

`MidiInputService`:
- Procura a entrada pelo nome (`"BK492"`, comparação "contém" sem maiúsc./minúsc.).
- `MidiIn.MessageReceived` → decodifica `ControlChangeEvent` → `MidiRouter`.
- Reconexão: escuta `WM_DEVICECHANGE` (via janela oculta) e também tenta reabrir a cada 3 s enquanto
  desconectado. `MidiIn.ErrorReceived` e exceções → fecha e entra no ciclo de reconexão.
- Suspensão/retomada (`SystemEvents.PowerModeChanged`) → reabre.

## Config

Arquivo: `%APPDATA%\sound-switch\config.json`. Exemplo completo com o setup atual em
[`config/config.example.json`](../config/config.example.json). Estrutura:

- `midi`: nome da entrada, canal.
- `devices`: dicionário de alvos nomeados (`edifier`, `fones`) com `match`, `knownIds`, `label`.
- `controls`: lista de controles `{ cc, type: "fader"|"button", action, … }`
  - `action: "deviceVolume"` + `device`
  - `action: "focusVolume"`
  - `action: "cycleDefaultDevice"` + `cycle: [...]`, `roles`, `triggerValue`, `debounceMs`
- `overlay`, `app` (autostart, log level).

Validação ao carregar: CC duplicado, alvo inexistente, calibração inválida → erro mostrado na bandeja e
o app continua com o último config válido.

## Inicialização com o Windows

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\sound-switch = "<exe>" --tray`. Sem admin.
