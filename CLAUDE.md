# CLAUDE.md

App pessoal Windows (C# / .NET 10, WPF, NAudio) que usa o teclado MIDI BK492 para controlar volumes
e trocar a saída de áudio padrão. Substitui MIDI Mixer + SoundSwitch + Bome's MIDI Translator.

- Idioma: documentação, UI e mensagens em **português (pt-BR)**. Código e identificadores em inglês.
- Leia `docs/` antes de implementar; `docs/06-roadmap.md` diz em que fase o projeto está.
- Regras que não podem ser quebradas:
  - Volume = `SetMasterVolumeLevelScalar` / `SimpleAudioVolume.Volume` com a fórmula de
    `docs/04-volume-e-calibracao.md` (linear, inteiro, 0 = mudo). Nunca dB, nunca curva implícita.
  - Dispositivos são resolvidos por `knownIds` **e** por nome (IDs de endpoint mudam neste PC).
  - Callback MIDI nunca chama COM; só escreve no slot de "último valor" do worker de áudio.
  - O overlay nunca pode receber foco (WS_EX_NOACTIVATE) — senão o fader do "app em foco" quebra.
  - Troca de saída altera só os papéis Console e Multimídia.
- Lógica pura fica em `SoundSwitch.Core` e tem testes xUnit; nada de COM/Win32 lá.
- Build/test: `dotnet build`, `dotnet test`. Instalar a versão nova: `scripts/publish.ps1` (fecha a cópia
  em execução, instala em `%LOCALAPPDATA%\Programs\sound-switch` e reabre). O usuário depende do app no
  dia a dia: só publicar com os testes passando.
- Não existe hardware em CI: tudo que depende de BK492/áudio é validado pelo checklist manual do roadmap.
