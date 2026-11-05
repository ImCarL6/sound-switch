# 02 — Requisitos

App pessoal, de um usuário só, rodando em um PC só. Os requisitos refletem exatamente o setup
descrito em [01-analise-setup-atual.md](01-analise-setup-atual.md) — sem perfis múltiplos, sem
suporte genérico a outros controladores (mas sem amarrar nada no código: tudo vem do arquivo de config).

## Requisitos funcionais

### RF1 — Entrada MIDI
- RF1.1 Abrir a entrada MIDI pelo **nome** (`BK492`), não pelo índice.
- RF1.2 Reconectar sozinho se o teclado for desplugado/replugado ou se o PC voltar da suspensão.
- RF1.3 Considerar apenas o canal configurado (canal 1).
- RF1.4 Funcionar junto com outros apps abrindo o BK492 (multi-cliente via `MidiSrv`) — necessário
  durante a migração, quando o app novo e os antigos vão conviver.

### RF2 — Volume por dispositivo (faders 1 e 3)
- RF2.1 Fader CC82 controla o volume master do endpoint **EDIFIER**; CC28 controla **Fones de ouvido**.
- RF2.2 Funciona independentemente de qual dispositivo é o padrão no momento.
- RF2.3 Se o dispositivo-alvo estiver desconectado, a mensagem é ignorada e o overlay mostra
  "EDIFIER — desconectado".
- RF2.4 Ao reconectar o dispositivo, o próximo movimento do fader volta a funcionar sem reiniciar o app.

### RF3 — Volume do app em foco (fader 2)
- RF3.1 Fader CC83 controla o volume de **todas as sessões de áudio** do app em primeiro plano,
  em **todos** os dispositivos de saída ativos.
- RF3.2 "App" = mesmo executável/árvore de processos da janela em foco (ex.: Chrome e Discord tocam
  áudio por processos filhos, não pelo processo da janela). Apps UWP/Store (janela
  `ApplicationFrameHost`) devem resolver para o processo real.
- RF3.3 A janela do próprio overlay nunca pode virar "o app em foco".
- RF3.4 Se o app em foco não tem sessão de áudio, nada é alterado e o overlay mostra "<App> — sem áudio".
- RF3.5 Trocar de janela **não** aplica o valor do fader no novo app; só o movimento do fader aplica
  (mesmo comportamento do MIDI Mixer). O overlay mostra o volume atual do novo app no próximo movimento.
- RF3.6 Ignorar a sessão "Sons do sistema".

### RF4 — Troca de dispositivo padrão (botão)
- RF4.1 Botão CC20 (valor 127) troca o dispositivo de saída padrão para o próximo da lista
  `[EDIFIER, Fones de ouvido]`, pulando os que estiverem desconectados.
- RF4.2 Alterar os papéis **Console** e **Multimídia** (não Comunicação), igual ao SoundSwitch atual.
- RF4.3 A troca é feita por API do Windows, **sem simular teclado**.
- RF4.4 Debounce: ignorar repetições do botão em menos de 250 ms.
- RF4.5 Overlay mostra o dispositivo novo (ex.: "🔊 Fones de ouvido") por ~2 s.
- RF4.6 Dispositivos fora da lista (AI Noise-Canceling Speaker etc.) nunca entram no ciclo.
- RF4.7 Se o dispositivo padrão atual não estiver na lista, vai para o primeiro da lista.

### RF5 — Overlay de feedback
Especificado em [05-overlay.md](05-overlay.md). Resumo: aparece a cada movimento de fader/botão,
mostra alvo + porcentagem + barra em tempo real, não rouba foco, some sozinho.

### RF6 — Operação em segundo plano
- RF6.1 Ícone na bandeja com: Pausar/Retomar, Abrir config, Recarregar config, Abrir logs, Sair.
- RF6.2 Iniciar com o Windows (sem janela), configurável.
- RF6.3 Instância única.
- RF6.4 Config em JSON (`%APPDATA%\sound-switch\config.json`), recarregada automaticamente ao salvar.
- RF6.5 Log em arquivo rotativo (`%APPDATA%\sound-switch\logs\`).

### RF7 — Ferramentas de diagnóstico
- RF7.1 Modo "monitor MIDI": lista em tempo real as mensagens recebidas (para descobrir CCs e medir
  o mínimo/máximo real de cada fader).
- RF7.2 Modo "calibração" por fader (ver [04-volume-e-calibracao.md](04-volume-e-calibracao.md)).

## Requisitos não funcionais

| ID | Requisito | Meta |
|---|---|---|
| RNF1 | **Precisão do volume** | Fader no mínimo ⇒ 0% (mudo). Fader no máximo ⇒ 100%. Linear em todo o curso: posição X% do fader ⇒ X% no Windows (±1 p.p.). Detalhes em [04](04-volume-e-calibracao.md). |
| RNF2 | Latência fader → volume | < 10 ms típico, < 30 ms pior caso |
| RNF3 | Latência fader → overlay | < 1 frame (≈16 ms) após a aplicação do volume |
| RNF4 | Sem "perder" o valor final | Ao parar o fader, o último valor recebido é **sempre** o aplicado (coalescência, nunca descarte do último) |
| RNF5 | Detecção do app em foco | Evento de foreground (sem polling); troca de janela reconhecida em < 50 ms |
| RNF6 | Recursos | < 50 MB RAM, ~0% CPU parado |
| RNF7 | Sem privilégios de admin | Roda como usuário comum |
| RNF8 | Robustez | Nenhuma exceção derruba o app; erro de COM/MIDI é logado e o serviço se recupera |

## Critérios de aceite (resumo)

1. Os três apps antigos podem ser fechados e desativados da inicialização sem perder nada do fluxo.
2. Fader 1 em todo o curso: Windows mostra 0 → 100 do EDIFIER, sem saltos, chegando a 100 só no fim do curso.
3. Fader 3 idem para Fones de ouvido.
4. Fader 2 com Spotify/Chrome/Discord/jogo em foco ajusta só aquele app (conferir no Mixer de Volume do Windows).
5. Botão alterna EDIFIER ⇄ Fones de ouvido, com o AI Noise-Canceling Speaker conectado e nunca selecionado.
6. Overlay aparece e acompanha o fader em tempo real sem tirar o foco do jogo/app atual.
7. Desplugar e replugar o BK492 e o EDIFIER com o app aberto: tudo volta a funcionar sozinho.
