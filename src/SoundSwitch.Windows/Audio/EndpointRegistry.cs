using NAudio.CoreAudioApi;
using SoundSwitch.Core.Devices;
using SoundSwitch.Windows.Diagnostics;

namespace SoundSwitch.Windows.Audio;

/// <summary>
/// Cache dos endpoints de saída ativos. Recarrega só quando o Windows avisa que algo mudou
/// (IMMNotificationClient) — nunca a cada mensagem MIDI.
/// Uso exclusivo da thread do <see cref="VolumeEngine"/> (dona dos objetos COM).
/// </summary>
internal sealed class EndpointRegistry : IDisposable
{
    readonly MMDeviceEnumerator enumerator = new();
    readonly MMDeviceNotificationClient notifications;
    readonly Dictionary<string, MMDevice> devices = new(StringComparer.OrdinalIgnoreCase);
    List<EndpointInfo> active = new();
    volatile bool dirty = true;

    public EndpointRegistry()
    {
        // false = eventos na thread do áudio do Windows; só marcamos "sujo", sem bloquear.
        notifications = enumerator.CreateNotificationClient(false);
        notifications.DeviceStateChanged += (_, _) => Invalidate();
        notifications.DeviceAdded += (_, _) => Invalidate();
        notifications.DeviceRemoved += (_, _) => Invalidate();
        notifications.PropertyValueChanged += (_, _) => Invalidate();
    }

    public void Invalidate() => dirty = true;

    public IReadOnlyList<EndpointInfo> Active
    {
        get { RefreshIfDirty(); return active; }
    }

    public MMDevice? Get(string id)
    {
        RefreshIfDirty();
        return devices.GetValueOrDefault(id);
    }

    /// <summary>Todos os endpoints de saída ativos (para enumerar sessões de áudio).</summary>
    public IReadOnlyCollection<MMDevice> ActiveDevices
    {
        get { RefreshIfDirty(); return devices.Values; }
    }

    /// <summary>Muda a cada recarga: objetos derivados dos MMDevice antigos ficam inválidos.</summary>
    public int Version { get; private set; }

    void RefreshIfDirty()
    {
        if (!dirty) return;
        dirty = false;
        Version++;

        foreach (var d in devices.Values) d.Dispose();
        devices.Clear();
        var list = new List<EndpointInfo>();

        using var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (int i = 0; i < collection.Count; i++)
        {
            var dev = collection[i];
            try
            {
                var info = Describe(dev);
                devices[info.Id] = dev;
                list.Add(info);
            }
            catch (Exception ex)
            {
                Log.Warn($"Áudio: não consegui ler o endpoint {i}: {ex.Message}");
                dev.Dispose();
            }
        }
        active = list;
        Log.Info("Áudio: saídas ativas = " + string.Join(" | ", list.Select(e => $"{e.FullName} {e.Id}")));
    }

    static EndpointInfo Describe(MMDevice dev)
    {
        string fullName = dev.FriendlyName;
        string adapter = dev.DeviceFriendlyName;
        string? desc = null;
        try
        {
            var props = dev.Properties;
            if (props.Contains(PropertyKeys.PKEY_Device_DeviceDesc))
                desc = props[PropertyKeys.PKEY_Device_DeviceDesc].Value as string;
        }
        catch { /* fallback abaixo */ }
        if (string.IsNullOrWhiteSpace(desc))
        {
            // "EDIFIER (CS43131+SGM8262 HIFI Audio)" → "EDIFIER"
            var suffix = $" ({adapter})";
            desc = fullName.EndsWith(suffix, StringComparison.Ordinal) ? fullName[..^suffix.Length] : fullName;
        }
        return new EndpointInfo(dev.ID, desc!, adapter, fullName);
    }

    public void Dispose()
    {
        notifications.Dispose();
        foreach (var d in devices.Values) d.Dispose();
        devices.Clear();
        enumerator.Dispose();
    }
}
