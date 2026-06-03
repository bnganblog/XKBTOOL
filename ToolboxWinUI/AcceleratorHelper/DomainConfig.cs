namespace AcceleratorHelper;

public static class DomainConfig
{
    public static readonly Dictionary<string, string[]> Services = new()
    {
        ["GitHub"] = new[]
        {
            "github.com",
            "api.github.com",
            "raw.githubusercontent.com",
            "objects.githubusercontent.com",
            "gist.github.com",
            "codeload.github.com",
            "github.githubassets.com",
            "githubstatus.com",
            "alive.github.com",
            "collector.github.com",
            "pipelines.actions.githubusercontent.com"
        },
        ["Steam"] = new[]
        {
            "store.steampowered.com",
            "steamcommunity.com",
            "api.steampowered.com",
            "login.steampowered.com",
            "help.steampowered.com",
            "checkout.steampowered.com",
            "partner.steampowered.com",
            "clan.akamai.steamstatic.com",
            "avatars.akamai.steamstatic.com",
            "community.akamai.steamstatic.com",
            "cdn.akamai.steamstatic.com",
            "steamcdn-a.akamaihd.net",
            "steamuserimages-a.akamaihd.net",
            "steamcommunity-a.akamaihd.net",
            "media.steampowered.com",
            "clientconfig.akamai.steamcloud.net"
        },
        ["Spotify"] = new[]
        {
            "open.spotify.com",
            "api.spotify.com",
            "xpui.app.spotify.com",
            "xpui.spotify.com",
            "spclient.wg.spotify.com",
            "gew4-spclient.spotify.com",
            "gae2-spclient.spotify.com",
            "audio-fa.spotifycdn.com",
            "encore.scdn.co",
            "i.scdn.co",
            "download.scdn.co",
            "partner.scdn.co"
        },
        ["Cloudflare"] = new[]
        {
            "cloudflare.com",
            "www.cloudflare.com",
            "dash.cloudflare.com",
            "api.cloudflare.com",
            "workers.cloudflare.com",
            "developers.cloudflare.com",
            "community.cloudflare.com",
            "blog.cloudflare.com",
            "status.cloudflare.com",
            "speed.cloudflare.com",
            "one.dash.cloudflare.com",
            "api.dash.cloudflare.com"
        }
    };

    public static string[] GetEnabledDomains(string[] enabledServices)
    {
        var domains = new List<string>();
        foreach (var service in enabledServices)
        {
            if (Services.TryGetValue(service, out var serviceDomains))
            {
                domains.AddRange(serviceDomains);
            }
        }
        return domains.Distinct().ToArray();
    }
}
