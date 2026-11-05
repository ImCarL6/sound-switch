# 06 — Roadmap

Cada fase termina com algo que roda e pode ser testado no PC real. Os apps antigos continuam ligados
até a Fase 4 (o `MidiSrv` deixa os dois lados lerem o BK492 ao mesmo tempo).

## Fase 0 — Medição do hardware (antes de qualquer código "de verdade")

Objetivo: tirar as dúvidas que a análise não consegue responder sem mexer no teclado.

- [x] `tools/MidiMonitor` (console .NET, sem dependências): abre `BK492`, imprime cada mensagem e gera
      relatório por controle (faixa real, valores pulados, pico de msgs/s, suspeita de jitter).
      Salva `captures/captura-*.csv` + `captura-*-resumo.txt`.
- [x] Medir para **cada fader**: os três vão de 0 a 127, sem jitter, pico < 50 msgs/s.
- [x] Medir o **botão**: `B0 14 7F` ao apertar, `B0 14 00` ao soltar.
- [x] Resultados em `docs/01-analise-setup-atual.md` (seção BK492). O `config.example.json` já estava
      certo (`min 0`, `max 127`, `jitterFilter 0`, `triggerValue 127`).

**Fase 0 concluída em 05/11/2026.**

### Roteiro da medição (para repetir se trocar de controlador)

```bash
dotnet run --project tools/MidiMonitor -c Release
```

1. Para cada fader, um de cada vez: leve **devagar** do fim de baixo ao fim de cima e volte
   (encostando bem nas pontas). Depois, uma passada **rápida** de ponta a ponta.
2. Solte o fader no meio e não toque nele por ~10 s (detecta oscilação em repouso).
3. Aperte o botão de troca 3 vezes, segurando ~1 s em cada (mostra o valor ao apertar e ao soltar).
4. Tecle `q`. O resumo aparece na tela e fica salvo em `tools/MidiMonitor/captures/`.

Pode rodar com MIDI Mixer/SoundSwitch abertos (eles vão continuar mudando o volume e trocando a saída
durante o teste — normal).

## Fase 1 — Núcleo: MIDI → volume de dispositivo + overlay mínimo

- [x] Criar solução (`SoundSwitch.Core`, `.Windows`, `.App`, `Core.Tests`) e `git init`.
- [x] `Config` (carregar/validar `config.json`, copiar o exemplo na primeira execução, recarga a quente,
      grava IDs aprendidos em `knownIds`).
- [x] `VolumeMapper` + testes de [04-volume-e-calibracao.md](04-volume-e-calibracao.md#testes-automatizados-obrigatórios).
- [x] `MidiRouter` + testes (inclui debounce do botão).
- [x] `MidiInputService` com reconexão (watchdog de 3 s + reabrir ao voltar da suspensão).
- [x] `EndpointRegistry` (resolução por ID/nome, notificações). Volume aplicado direto no `VolumeEngine`.
- [x] Worker de áudio com coalescência (`VolumeEngine`).
- [x] `OverlayWindow` básico (nome + % + barra, no-activate, click-through) e ícone mínimo na bandeja.
- **Aceite:** faders 1 e 3 controlam EDIFIER e Fones de ouvido, número do overlay = número do Windows
  em todo o curso, 0 = mudo, topo = 100.

**Fase 1 aceita em 05/11/2026.** Teste com os dois faders de ponta a ponta: 513 valores aplicados,
todos iguais à fórmula do doc 04; o mudo foi acionado nas 5 vezes que algum fader chegou ao 0; o Windows ficou
com exatamente o último valor (100%); nenhum aviso ou erro no log. 33 testes automatizados passando.
Ainda não testado: desplugar/replugar o BK492 e o EDIFIER com o app aberto, e suspender/retomar (checklist itens 5–6).

## Fase 2 — App em foco

- [ ] `ForegroundTracker` (WinEvent hook, UWP, ignorar a própria janela).
- [ ] `SessionVolumeService` (todas as saídas, casamento por PID/exe/árvore, cache).
- [ ] Overlay com ícone e nome do app, estado "sem áudio".
- **Aceite:** Spotify, Chrome (aba com vídeo), Discord, um jogo e o player de vídeo do Windows.
  Conferir no Mixer de Volume do Windows que só o app em foco muda.

## Fase 3 — Troca de dispositivo

- [ ] Interop `IPolicyConfig` + `DefaultDeviceService` (console + multimídia).
- [ ] `DeviceCycle` + testes (pular desconectado, padrão fora da lista, lista com 1 item).
- [ ] Debounce do botão; overlay "Saída: …".
- **Aceite:** botão alterna EDIFIER ⇄ Fones de ouvido; com o EDIFIER desplugado o botão mantém/força
  Fones de ouvido; AI Noise-Canceling Speaker nunca é escolhido.

## Fase 4 — Uso diário e migração

- [ ] Bandeja (pausar, recarregar config, abrir config/logs, sair), instância única, autostart.
- [ ] Log rotativo; tratamento de erros (nenhuma exceção derruba o app).
- [ ] Tela de monitor MIDI e assistente de calibração dentro do app.
- [ ] Publicar `win-x64` single-file.
- [ ] **Migração:**
  1. Rodar o app novo junto com os antigos por 1–2 dias, com o MIDI Mixer pausado (desmarcar o
     perfil) para não brigar pelo volume.
  2. Desligar "iniciar com o Windows" no MIDI Mixer (`startOnBoot`), no SoundSwitch e no Bome.
  3. Fechar os três e usar só o app novo por uma semana.
  4. Desinstalar (opcional). Os configs antigos ficam documentados em [01](01-analise-setup-atual.md).

## Fase 5 — Extras (só se fizer falta)

- Tela de configuração gráfica (hoje: editar JSON com recarga automática).
- Atalho de teclado alternativo para a troca de saída (para quando o teclado MIDI não estiver ligado).
- "Soft takeover" no fader do app em foco (só aplica depois que o fader "passa" pelo volume atual do app).
- Usar o LED/saída MIDI do BK492 como indicador, se ele tiver.
- Mute do microfone por botão MIDI (o SoundSwitch tem Ctrl+Alt+M configurado, mas não é usado hoje).

## Checklist de teste manual (rodar ao fim de cada fase e após grandes updates do Windows)

1. Cada fader de ponta a ponta, devagar: Windows e overlay batem, 0 = mudo, 100 só no topo.
2. Movimento rápido de ponta a ponta e parada brusca: valor final correto.
3. Fader 2 com 5 apps diferentes em foco; janela do overlay nunca vira o foco.
4. Botão 10× seguidas rápido: sem trocas duplas, termina no dispositivo esperado.
5. Desplugar/replugar BK492 e EDIFIER com o app aberto.
6. Suspender e retomar o PC.
7. Editar o `config.json` com o app aberto (inclusive com erro de sintaxe).
