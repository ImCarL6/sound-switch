# 04 — Mapeamento de volume e calibração

O requisito central: **o volume tem que acompanhar o curso do fader de forma linear e exata.**
Mínimo = mudo, máximo = 100%, e nada de "o fader chega em 80% e o volume já está em 100%".

## Por que isso dá errado em outros apps

Há três causas comuns, e o app trata cada uma:

| Causa | Sintoma | Tratamento |
|---|---|---|
| **Escala errada no Windows.** O volume do endpoint pode ser setado em dB (`SetMasterVolumeLevel`) ou em "escalar" (`SetMasterVolumeLevelScalar`). Mapear o fader em dB ou em amplitude linear faz o número no Windows não bater com a posição do fader. | Volume sobe rápido no começo e "satura" cedo, ou o contrário. | Usar **sempre o escalar** (0.0–1.0). É exatamente a escala do controle deslizante do Windows: escalar 0.37 = "37" no Windows. |
| **Fader físico não chega a 0/127.** Faders baratos enviam 127 antes do fim do curso, ou nunca chegam a 0. | Volume chega a 100% com o fader a 80% (zona morta no topo), ou nunca fica mudo. | **Calibração** por fader: o app aprende o mínimo/máximo reais e estica a faixa. |
| **Curva aplicada pelo app** (log/exponencial "para soar natural"). | Proporção fader × número no Windows não é 1:1. | Curva **linear** como padrão; curva customizável só se o usuário quiser. |

## Fórmula

Entradas por fader (vêm da calibração; padrão `min = 0`, `max = 127`):

```
raw        = valor CC recebido (0..127)
t          = clamp((raw - min) / (max - min), 0, 1)     // posição normalizada do fader
percent    = round(t * 100)                             // inteiro 0..100
scalar     = percent / 100.0                            // valor enviado ao Windows
```

Regras de borda:

- `raw <= min` ⇒ `percent = 0` ⇒ escalar 0.0 **e** `mute = true` (silêncio garantido).
- `raw >= max` ⇒ `percent = 100` ⇒ escalar 1.0.
- `percent > 0` e o alvo estava mutado ⇒ desmutar (assim, subir o fader sempre devolve o som,
  mesmo se alguém mutou pelo Windows).

### Por que arredondar para inteiro

O Windows mostra o volume como inteiro. Setar `scalar = 64/127 = 0.5039…` aparece como "50" e o
próximo passo, `0.5118…`, como "51" — funciona, mas o valor guardado não é "limpo" e o overlay
mostraria 50,4%. Arredondando para inteiro antes, **o número do overlay é exatamente o número do
Windows**. O custo é que, com 128 posições para 101 porcentagens, ~27 porcentagens são "puladas"
(um passo do fader vale ~0,79 p.p.), o que é imperceptível.

Tabela de referência (calibração padrão 0–127):

| raw | 0 | 1 | 13 | 32 | 64 | 95 | 102 | 114 | 126 | 127 |
|---|---|---|---|---|---|---|---|---|---|---|
| % | 0 (mudo) | 1 | 10 | 25 | 50 | 75 | 80 | 90 | 99 | 100 |

Note que **80% só é atingido em raw 102** (80% do curso) — e 100% só em raw 127.

## Calibração

Fluxo (Fase 0/1 do [roadmap](06-roadmap.md)), acessível pela bandeja → "Calibrar fader…":

1. Escolher o fader.
2. "Leve o fader até o **fim de baixo** e solte" → o app registra o menor valor visto em ~1 s.
3. "Leve o fader até o **fim de cima** e solte" → registra o maior valor.
4. Opcional: "Coloque o fader na **marca do meio**" → registra o valor. Se o meio físico não der
   ≈ (min+max)/2, o fader tem resposta não linear e o app grava um ponto intermediário
   (mapeamento linear por partes: `min → 0%`, `meio → 50%`, `max → 100%`).
5. Salvar em `config.json` no fader correspondente:

```json
"calibration": { "min": 0, "max": 127, "points": [] }
```

`points` é opcional, lista de pares `[raw, percent]` ordenados para linear por partes.

## Ruído (jitter) do fader

Faders podem oscilar ±1 em repouso, gerando mensagens sem o usuário mexer. Tratamento:

- Ignorar mensagem com `raw` igual ao último aplicado (barato e sem efeito colateral).
- Se ainda houver oscilação ±1 em repouso (verificar no monitor MIDI na Fase 0), ativar
  `"jitterFilter": 1` no fader: só aceitar mudança de 1 passo se ela vier na mesma direção do
  movimento anterior ou após 150 ms parado. **Nunca** filtrar quando `raw` chega a `min` ou `max`
  (as pontas têm que ser exatas).

Por padrão o filtro fica **desligado** — só liga se a medição mostrar necessidade.

> **Medição de 05/11/2026:** os três faders do BK492 vão de 0 a 127 de verdade e não mandam nada quando
> estão parados (ver [01](01-analise-setup-atual.md#medição-real-fase-0-05112026)). Então: calibração padrão
> `0–127` e `jitterFilter: 0`. A tabela de referência acima vale como está para este hardware.

## Volume por sessão (fader do app em foco)

A mesma fórmula vale para `ISimpleAudioVolume::SetMasterVolume` (escalar 0–1 por sessão). Atenção:
o volume de uma sessão é **relativo** ao volume do dispositivo (o Mixer de Volume do Windows mostra
os dois). 100% no fader do app = app no volume máximo **dentro** do volume atual do dispositivo.
É o mesmo comportamento do MIDI Mixer e do Mixer do Windows.

## Testes automatizados obrigatórios

- `raw = min` → 0% e mute; `raw = max` → 100%; `raw = min - 3` e `max + 3` (fora da calibração) → clamp.
- Monotonicidade: para todo raw de 0 a 127, `percent(raw) >= percent(raw - 1)`.
- Linearidade: `|percent(raw) - 100 * (raw - min) / (max - min)| <= 0.5` para todo raw.
- Linear por partes com ponto do meio.
- Calibrações degeneradas (`min >= max`) são rejeitadas ao carregar o config.
