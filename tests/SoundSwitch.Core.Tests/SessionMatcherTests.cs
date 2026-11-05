using SoundSwitch.Core.Focus;

namespace SoundSwitch.Core.Tests;

public class SessionMatcherTests
{
    const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    static readonly FocusedApp ChromeWindow = new(1000, Chrome, "Google Chrome");
    static readonly Dictionary<int, int> NoParents = new();

    [Fact]
    public void Mesmo_pid()
    {
        Assert.True(SessionMatcher.Matches(ChromeWindow, new(1000, null), NoParents));
    }

    [Fact]
    public void Processo_filho_com_mesmo_exe()
    {
        // O áudio do Chrome sai do processo "audio service", outro PID com o mesmo exe.
        Assert.True(SessionMatcher.Matches(ChromeWindow, new(2345, Chrome.ToUpperInvariant()), NoParents));
    }

    [Fact]
    public void Descendente_por_arvore_de_processos()
    {
        var parents = new Dictionary<int, int> { [3000] = 2000, [2000] = 1000, [1000] = 500 };
        Assert.True(SessionMatcher.Matches(ChromeWindow, new(3000, @"C:\x\helper.exe"), parents));
    }

    [Fact]
    public void Outro_app_nao_casa()
    {
        var parents = new Dictionary<int, int> { [4000] = 500 };
        Assert.False(SessionMatcher.Matches(ChromeWindow, new(4000, @"C:\Spotify\Spotify.exe"), parents));
    }

    [Fact]
    public void Sons_do_sistema_nunca_casam()
    {
        Assert.False(SessionMatcher.Matches(new FocusedApp(0, null, "x"), new(0, null), NoParents));
    }

    [Fact]
    public void Caminho_desconhecido_nao_casa_por_exe()
    {
        Assert.False(SessionMatcher.Matches(new FocusedApp(1000, null, "x"), new(2000, null), NoParents));
    }

    [Fact]
    public void Ciclo_na_arvore_nao_trava()
    {
        var parents = new Dictionary<int, int> { [3000] = 3001, [3001] = 3000 };
        Assert.False(SessionMatcher.Matches(ChromeWindow, new(3000, null), parents));
    }
}
