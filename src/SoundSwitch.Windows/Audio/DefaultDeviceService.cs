using System.Runtime.InteropServices;

namespace SoundSwitch.Windows.Audio;

/// <summary>
/// Define o dispositivo padrão via <c>IPolicyConfig</c>, interface COM não documentada do Windows
/// (a mesma usada por SoundSwitch, EarTrumpet etc.). Isolada aqui de propósito: se uma atualização do
/// Windows mudar a interface, só este arquivo muda (docs/03-arquitetura.md#troca-do-dispositivo-padrão).
/// </summary>
public static class DefaultDeviceService
{
    /// <summary>Papéis do Windows (ERole). Valores iguais aos da API.</summary>
    public enum EndpointRole { Console = 0, Multimedia = 1, Communications = 2 }

    public static EndpointRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "console" => EndpointRole.Console,
        "multimedia" => EndpointRole.Multimedia,
        "communications" => EndpointRole.Communications,
        _ => throw new ArgumentException($"role desconhecido: {role}"),
    };

    /// <summary>Define <paramref name="endpointId"/> como padrão para cada papel. Lança COMException em falha.</summary>
    public static void SetDefault(string endpointId, IEnumerable<EndpointRole> roles)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            foreach (var role in roles)
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(endpointId, role));
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }

    // A ordem dos métodos define a vtable: não reordenar. Só SetDefaultEndpoint é usado.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, EndpointRole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
