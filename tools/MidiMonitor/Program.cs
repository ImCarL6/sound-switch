// MidiMonitor — ferramenta da Fase 0 (ver docs/06-roadmap.md).
// Abre a entrada MIDI (padrão: BK492), mostra cada mensagem em tempo real e, ao sair,
// gera um relatório por controle: mínimo/máximo reais, valores que nunca apareceram,
// taxa de mensagens e suspeitas de oscilação (jitter). Tudo é salvo em CSV + TXT.
//
// Uso:
//   dotnet run --project tools/MidiMonitor                 (interativo; q = sair, s = resumo, r = zerar)
//   dotnet run --project tools/MidiMonitor -- --list       (lista as entradas MIDI)
//   dotnet run --project tools/MidiMonitor -- --device BK492 --seconds 60
//
// Não depende de nenhum pacote: usa WinMM (midiIn*) direto. Funciona junto com o MIDI Mixer e o
// Bome abertos, porque o Windows MIDI Services (MidiSrv) permite vários leitores na mesma entrada.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MidiMonitor;

var opts = Options.Parse(args);
if (opts is null) return 2;

Console.OutputEncoding = Encoding.UTF8;

var inputs = WinMM.ListInputs();
if (opts.List)
{
    Console.WriteLine("Entradas MIDI:");
    if (inputs.Count == 0) Console.WriteLine("  (nenhuma)");
    foreach (var (id, name) in inputs) Console.WriteLine($"  [{id}] {name}");
    return 0;
}

var match = inputs.FirstOrDefault(i => i.Name.Contains(opts.Device, StringComparison.OrdinalIgnoreCase));
if (match.Name is null)
{
    Console.Error.WriteLine($"Entrada MIDI contendo \"{opts.Device}\" não encontrada. Disponíveis:");
    foreach (var (id, name) in inputs) Console.Error.WriteLine($"  [{id}] {name}");
    return 1;
}

var outDir = opts.OutDir ?? Paths.DefaultCaptureDir();
Directory.CreateDirectory(outDir);
var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var csvPath = Path.Combine(outDir, $"captura-{stamp}.csv");
var reportPath = Path.Combine(outDir, $"captura-{stamp}-resumo.txt");

var queue = new BlockingCollection<MidiEvent>();
var clock = Stopwatch.StartNew();
using var midi = new WinMM.MidiInput(match.Id, queue, clock);

try
{
    midi.Open();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Falha ao abrir \"{match.Name}\": {ex.Message}");
    return 1;
}

var stats = new StatsBook();
using var csv = new StreamWriter(csvPath, false, new UTF8Encoding(false));
csv.WriteLine("t_ms;device_ms;status_hex;data1;data2;channel;type;number;value;raw_hex");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (opts.Seconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(opts.Seconds));

Console.WriteLine($"Ouvindo \"{match.Name}\" (entrada {match.Id}).");
Console.WriteLine(Console.IsInputRedirected
    ? "Ctrl+C para sair."
    : "Teclas: [q] sair   [s] resumo agora   [r] zerar estatísticas");
Console.WriteLine($"Gravando em {csvPath}");
Console.WriteLine();

while (!cts.IsCancellationRequested)
{
    if (!Console.IsInputRedirected && Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true).Key;
        if (key == ConsoleKey.Q) break;
        if (key == ConsoleKey.S) { Console.WriteLine(); Console.WriteLine(stats.Report(clock.Elapsed)); }
        if (key == ConsoleKey.R) { stats = new StatsBook(); Console.WriteLine("— estatísticas zeradas —"); }
    }

    if (!queue.TryTake(out var ev, 50)) continue;

    if (ev.Kind != EventKind.Data)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{Fmt.Time(ev.HostMs)}  [{ev.Kind}] {ev.Info}");
        Console.ResetColor();
        if (ev.Kind == EventKind.Closed) break;
        continue;
    }

    var msg = MidiMessage.Decode(ev.Raw);
    csv.WriteLine(string.Join(';',
        ev.HostMs.ToString("F1", CultureInfo.InvariantCulture),
        ev.DeviceMs,
        $"{msg.Status:X2}", msg.Data1, msg.Data2,
        msg.Channel, msg.Type, msg.Number?.ToString() ?? "", msg.Value?.ToString() ?? "",
        $"{ev.Raw:X6}"));

    var s = msg.Number is not null && msg.Value is not null ? stats.Add(msg, ev.HostMs) : null;
    if (!opts.Quiet) Console.WriteLine(Fmt.Line(ev, msg, s));
}

csv.Flush();
midi.Close();

var report = stats.Report(clock.Elapsed);
Console.WriteLine();
Console.WriteLine(report);
File.WriteAllText(reportPath,
    $"Captura {stamp} — entrada \"{match.Name}\"{Environment.NewLine}{report}", new UTF8Encoding(false));
Console.WriteLine($"Resumo salvo em {reportPath}");
Console.WriteLine($"Mensagens em  {csvPath}");
return 0;

namespace MidiMonitor
{
    sealed record Options(string Device, bool List, int Seconds, bool Quiet, string? OutDir)
    {
        public static Options? Parse(string[] args)
        {
            string device = "BK492"; bool list = false, quiet = false; int seconds = 0; string? outDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--list": list = true; break;
                    case "--quiet": quiet = true; break;
                    case "--device" when i + 1 < args.Length: device = args[++i]; break;
                    case "--seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n): seconds = n; i++; break;
                    case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                    case "-h" or "--help":
                        Console.WriteLine("MidiMonitor [--list] [--device NOME] [--seconds N] [--quiet] [--out PASTA]");
                        return null;
                    default:
                        Console.Error.WriteLine($"Argumento desconhecido: {args[i]} (use --help)");
                        return null;
                }
            }
            return new Options(device, list, seconds, quiet, outDir);
        }
    }

    static class Paths
    {
        /// <summary>tools/MidiMonitor/captures (achando o .csproj subindo a partir do executável); senão ./captures.</summary>
        public static string DefaultCaptureDir()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "MidiMonitor.csproj")))
                    return Path.Combine(dir.FullName, "captures");
            return Path.Combine(Environment.CurrentDirectory, "captures");
        }
    }

    /// <summary>Rótulos do setup atual (docs/01-analise-setup-atual.md). Só para exibição.</summary>
    static class KnownControls
    {
        public static string? Label(int channel, string type, int number) => (channel, type, number) switch
        {
            (1, "CC", 82) => "Fader 1 → EDIFIER",
            (1, "CC", 83) => "Fader 2 → app em foco",
            (1, "CC", 28) => "Fader 3 → Fones de ouvido",
            (1, "CC", 20) => "Botão → troca de saída",
            _ => null,
        };
    }

    static class Fmt
    {
        public static string Time(double ms) => (ms / 1000).ToString("0000.000", CultureInfo.InvariantCulture) + "s";

        public static string Line(MidiEvent ev, MidiMessage m, ControlStats? s)
        {
            var sb = new StringBuilder();
            sb.Append(Time(ev.HostMs)).Append("  ").Append($"{m.Status:X2} {m.Data1:X2} {m.Data2:X2}").Append("  ");
            sb.Append($"ch{m.Channel,-2} {m.Type,-4}");
            if (m.Number is int n) sb.Append($" {n,3}");
            if (m.Value is int v)
            {
                sb.Append($"  val {v,3}  ");
                int filled = (int)Math.Round(v / 127.0 * 20);
                sb.Append('[').Append(new string('█', filled)).Append(new string('·', 20 - filled)).Append(']');
                sb.Append($" {Math.Round(v * 100.0 / 127),3}%");
            }
            if (s is not null) sb.Append($"   min {s.Min,3}  max {s.Max,3}");
            if (m.Number is int num && KnownControls.Label(m.Channel, m.Type, num) is string label) sb.Append("   ").Append(label);
            return sb.ToString();
        }
    }

    enum EventKind { Data, Error, LongData, Opened, Closed }

    readonly record struct MidiEvent(EventKind Kind, uint Raw, uint DeviceMs, double HostMs, string? Info = null);

    readonly record struct MidiMessage(byte Status, byte Data1, byte Data2, int Channel, string Type, int? Number, int? Value)
    {
        public static MidiMessage Decode(uint raw)
        {
            byte st = (byte)(raw & 0xFF), d1 = (byte)((raw >> 8) & 0x7F), d2 = (byte)((raw >> 16) & 0x7F);
            int ch = (st & 0x0F) + 1;
            return (st & 0xF0) switch
            {
                0x80 => new(st, d1, d2, ch, "NoteOff", d1, d2),
                0x90 => new(st, d1, d2, ch, d2 == 0 ? "NoteOff" : "NoteOn", d1, d2),
                0xA0 => new(st, d1, d2, ch, "PolyAT", d1, d2),
                0xB0 => new(st, d1, d2, ch, "CC", d1, d2),
                0xC0 => new(st, d1, d2, ch, "PC", d1, null),
                0xD0 => new(st, d1, d2, ch, "ChanAT", null, d1),
                0xE0 => new(st, d1, d2, ch, "Pitch", null, (d2 << 7) | d1),
                _ => new(st, d1, d2, 0, $"Sys{st:X2}", null, null),
            };
        }
    }

    sealed class ControlStats
    {
        const double IdleMs = 300;          // parado há mais que isso = "repouso"
        public required string Key { get; init; }
        public int Count, Min = int.MaxValue, Max = int.MinValue, Last = -1, MaxPerSecond, Reversals, IdleSmallChanges;
        public double FirstMs, LastMs;
        public readonly bool[] Seen = new bool[128];
        public readonly SortedDictionary<int, int> Histogram = new();
        readonly Queue<double> window = new();
        int lastDir;
        // Mudança de ±1 após repouso só conta como jitter se o movimento NÃO continuar na mesma direção
        // logo em seguida (senão é apenas o começo de um movimento real).
        bool suspectPending; double suspectMs; int suspectDir;

        /// <summary>Jitter confirmado + suspeita ainda pendente no fim da captura.</summary>
        public int JitterSuspects => IdleSmallChanges + (suspectPending ? 1 : 0);

        public void Add(int value, double ms)
        {
            if (Count == 0) FirstMs = ms;
            if (Count > 0)
            {
                int delta = value - Last;
                if (suspectPending)
                {
                    bool continued = ms - suspectMs <= IdleMs && Math.Sign(delta) == suspectDir;
                    if (!continued) IdleSmallChanges++;
                    suspectPending = false;
                }
                if (ms - LastMs > IdleMs && Math.Abs(delta) == 1)
                {
                    suspectPending = true; suspectMs = ms; suspectDir = Math.Sign(delta);
                }
                int dir = Math.Sign(delta);
                if (dir != 0 && lastDir != 0 && dir != lastDir) Reversals++;
                if (dir != 0) lastDir = dir;
            }
            Count++; Last = value; LastMs = ms;
            Min = Math.Min(Min, value); Max = Math.Max(Max, value);
            if (value is >= 0 and < 128) Seen[value] = true;
            Histogram[value] = Histogram.GetValueOrDefault(value) + 1;

            window.Enqueue(ms);
            while (window.Count > 0 && ms - window.Peek() > 1000) window.Dequeue();
            MaxPerSecond = Math.Max(MaxPerSecond, window.Count);
        }
    }

    sealed class StatsBook
    {
        readonly Dictionary<string, ControlStats> byKey = new();

        public ControlStats Add(MidiMessage m, double ms)
        {
            var key = $"ch{m.Channel} {m.Type} {m.Number}";
            if (!byKey.TryGetValue(key, out var s)) byKey[key] = s = new ControlStats { Key = key };
            s.Add(m.Value!.Value, ms);
            return s;
        }

        public string Report(TimeSpan elapsed)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"══ Resumo ({elapsed.TotalSeconds:F0} s de captura) ══");
            if (byKey.Count == 0) { sb.AppendLine("Nenhuma mensagem recebida."); return sb.ToString(); }

            foreach (var s in byKey.Values.OrderBy(s => s.Key))
            {
                var parts = s.Key.Split(' ');
                var label = KnownControls.Label(int.Parse(parts[0][2..]), parts[1], int.Parse(parts[2]));
                sb.AppendLine();
                sb.AppendLine($"■ {s.Key}{(label is null ? "" : "  — " + label)}");
                sb.AppendLine($"  mensagens: {s.Count}   pico: {s.MaxPerSecond}/s   faixa real: {s.Min} … {s.Max}");

                int distinct = s.Seen.Count(b => b);
                if (s.Histogram.Count <= 4)
                {
                    // Provável botão: mostra os valores que chegaram.
                    sb.AppendLine("  valores: " + string.Join(", ", s.Histogram.Select(kv => $"{kv.Key} (×{kv.Value})")));
                    continue;
                }

                sb.AppendLine($"  chega a 0? {(s.Seen[0] ? "sim" : "NÃO")}   chega a 127? {(s.Seen[127] ? "sim" : "NÃO")}   valores distintos: {distinct}/128");
                var missing = Enumerable.Range(s.Min, s.Max - s.Min + 1).Where(v => !s.Seen[v]).ToList();
                if (missing.Count > 0)
                    sb.AppendLine($"  valores pulados dentro da faixa ({missing.Count}): {Ranges(missing)}");
                sb.AppendLine($"  inversões de direção: {s.Reversals}   oscilações de ±1 em repouso (jitter): {s.JitterSuspects}");
            }
            return sb.ToString();
        }

        static string Ranges(List<int> v)
        {
            var parts = new List<string>();
            for (int i = 0; i < v.Count;)
            {
                int j = i;
                while (j + 1 < v.Count && v[j + 1] == v[j] + 1) j++;
                parts.Add(i == j ? $"{v[i]}" : $"{v[i]}–{v[j]}");
                i = j + 1;
            }
            return string.Join(", ", parts);
        }
    }
}
