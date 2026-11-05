# 05 — Overlay (feedback na tela)

Substitui o popup do MIDI Mixer e o banner do SoundSwitch por um único componente.

## Comportamento

- Aparece ao receber qualquer mensagem mapeada (fader ou botão).
- Atualiza **em tempo real** enquanto o fader se move (redesenha no máximo 1× por frame do monitor;
  o volume em si é aplicado independentemente da taxa de quadros).
- Some com fade-out após **1,5 s** sem novas mensagens (2 s para a troca de dispositivo). Configurável.
- Se outro controle for mexido enquanto visível, o conteúdo troca para o novo controle (um card só,
  sem empilhar).

## Requisitos técnicos críticos

| Requisito | Por quê | Como |
|---|---|---|
| **Nunca pegar foco** | Se o overlay virar a janela em foco, o fader 2 passaria a controlar o próprio app e o jogo perderia o input. | `WS_EX_NOACTIVATE`, `ShowActivated = false`, `Focusable = false`, nunca chamar `Activate()`. |
| **Click-through** | Não bloquear cliques no canto da tela. | `WS_EX_TRANSPARENT` + `WS_EX_LAYERED`. |
| Sempre por cima | Visível sobre jogos em janela/borderless. | `Topmost` + `WS_EX_TOPMOST`. Em jogos fullscreen exclusivo não aparece — limitação aceita. |
| Fora do Alt+Tab e da barra de tarefas | — | `WS_EX_TOOLWINDOW`, `ShowInTaskbar = false`. |
| DPI | Monitores com escala diferente. | Per-Monitor V2 DPI awareness no manifest. |
| Monitor | — | Monitor principal por padrão; opção "monitor da janela em foco". |

## Layout

Card compacto, cantos arredondados, tema escuro por padrão (combina com jogos), ~300×64 px a 100%:

```
┌──────────────────────────────────────────┐
│ 🔊  EDIFIER                         73%  │   ← ícone do tipo + nome do alvo + porcentagem
│ ███████████████████████████░░░░░░░░░░░░  │   ← barra = porcentagem exata
└──────────────────────────────────────────┘
```

Variações de conteúdo:

| Situação | Ícone | Texto | Valor |
|---|---|---|---|
| Fader de dispositivo | alto-falante / fone | Nome curto do dispositivo ("EDIFIER") | `73%` + barra |
| Fader em 0 | alto-falante cortado | idem | `Mudo` + barra vazia |
| Fader do app em foco | ícone do executável | Nome do app ("Spotify") | `40%` + barra |
| App em foco sem áudio | ícone do executável, esmaecido | "Discord — sem áudio" | — |
| Dispositivo desconectado | ⚠ | "EDIFIER — desconectado" | — |
| Troca de dispositivo | 🔊 | "Saída: Fones de ouvido" | lista com o ativo destacado: `● Fones de ouvido  ○ EDIFIER` |

Nome curto do dispositivo: definido no config (`"label"`), senão a parte antes do parênteses do nome amigável.

## Posição

Padrão: **canto superior esquerdo**, margem de 16 px (onde o MIDI Mixer mostra hoje).
Configurável: `top-left | top-center | top-right | bottom-left | bottom-center | bottom-right`, mais margem.

## Configuração (trecho do config.json)

```json
"overlay": {
  "enabled": true,
  "position": "top-left",
  "margin": 16,
  "hideAfterMs": 1500,
  "deviceSwitchHideAfterMs": 2000,
  "theme": "dark",
  "monitor": "primary"
}
```
