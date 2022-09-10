using System.Diagnostics;
using Newtonsoft.Json;
using Sharprompt;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace deemix_wrapper;

public static class Spotify
{
    private static SpotifyClient _spotifyClient = null!;

    private static readonly string CredentialsPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, Path.Combine(".cache/deemix-wrapper", "credentials.json"));
    private static readonly string? ClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
    private static readonly EmbedIOAuthServer Server = new(new Uri("http://localhost:5000/callback"), 5000);
    private static ManualResetEventSlim wg = new ManualResetEventSlim(false);

    public static async Task Init()
    {
        Swan.Logging.Logger.NoLogging();

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new NullReferenceException(
                "Please set SPOTIFY_CLIENT_ID via environment variables before starting the program"
            );
        }
        
        await Start();
    }

    private static async Task Start()
    {
        if (!File.Exists(CredentialsPath))
        {
            var wg = new ManualResetEvent(false);
            await StartAuthentication();
            wg.WaitOne(TimeSpan.FromSeconds(10));
        }

        var json = await File.ReadAllTextAsync(CredentialsPath);
        var token = JsonConvert.DeserializeObject<PKCETokenResponse>(json);

        var authenticator = new PKCEAuthenticator(ClientId!, token!);
        authenticator.TokenRefreshed +=
            (_, token) => File.WriteAllText(CredentialsPath, JsonConvert.SerializeObject(token));

        var config = SpotifyClientConfig.CreateDefault()
            .WithAuthenticator(authenticator);

        _spotifyClient = new SpotifyClient(config);
        Server.Dispose();
    }

    private static async Task StartAuthentication()
    {
        var (verifier, challenge) = PKCEUtil.GenerateCodes();

        await Server.Start();
        Server.AuthorizationCodeReceived += async (sender, response) =>
        {
            await Server.Stop();
            var token = await new OAuthClient().RequestToken(
                new PKCETokenRequest(ClientId!, response.Code, Server.BaseUri, verifier)
            );

            await File.WriteAllTextAsync(CredentialsPath, JsonConvert.SerializeObject(token));
            wg.Set();
        };

        var request = new LoginRequest(Server.BaseUri, ClientId!, LoginRequest.ResponseType.Code)
        {
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            Scope = new List<string>
            {
                Scopes.PlaylistReadPrivate, Scopes.PlaylistReadCollaborative, Scopes.PlaylistModifyPrivate, Scopes.PlaylistModifyPublic
            }
        };

        var uri = request.ToUri();
        try
        {
            BrowserUtil.Open(uri);
        }
        catch (Exception)
        {
            Console.WriteLine("Unable to open URL, manually open: {0}", uri);
        }
    }

    private static async Task<SpotifyTrackModel?> Search(Program.Track searchTerm)
    {
        Debug.Assert(searchTerm.Title != null, "searchTerm.Title != null");
        var searchRequest = new SearchRequest(SearchRequest.Types.Track, searchTerm.Title) {Limit = 10};
        var res = await _spotifyClient.Search.Item(searchRequest);
        return res.Tracks.Items != null ? await SelectTrackToAdd(res.Tracks.Items) : null;
    }

    private static async Task<SpotifyTrackModel?> SelectTrackToAdd(IEnumerable<FullTrack> tracks)
    {
        var choice = Prompt.Select("Select song to add to spotify playlist: ",
            tracks.Select(track => new SpotifyTrackModel(
                track.Artists.First().Name,
                track.Name,
                track.Id,
                track.Uri
            )).Append(new SpotifyTrackModel("Track not found", "Modify search term", null, null)));

        if (choice.Artist != "Track not found" || choice.Uri != null || choice.Id != null) return choice;

        var newSearchTerm = Prompt.Input<string>("Enter new search term: ");
        return await Search(new Program.Track(Title: newSearchTerm, Artist: null, Link: null, thumbnail:null));
    }

    private static async Task<SpotifyPlaylistModel> SelectPlaylist()
    {
        var playlists = await _spotifyClient.Playlists.CurrentUsers();
        if (playlists.Items == null || playlists.Items.Count == 0)
        {
            Console.WriteLine("No playlists found. Exiting...");
            Environment.Exit(1);
        }

        var playlist = Prompt.Select("Select playlist: ",
            playlists.Items.Select(p => new SpotifyPlaylistModel(p.Name, p.Id, p.Uri)));
        return playlist;
    }

    public static async Task AddToPlaylist(IEnumerable<Program.Track?> searchTerms)
    {
        var tracks = new List<SpotifyTrackModel>();
        var pl = await SelectPlaylist();
        
        if (pl.Id is null)
        {
            throw new NullReferenceException("Playlist ID is null");
        }

        foreach (var t in searchTerms)
        {
            var res = await Search(t!);
            if (res is not null) tracks.Add(res);
        }
        
        await _spotifyClient.Playlists.AddItems(pl.Id!,
            new PlaylistAddItemsRequest(tracks.Select(t => t.Uri).ToList()!));
    }

    private record SpotifyPlaylistModel(string? Name, string? Id, string? Uri)
    {
        public override string ToString()
        {
            return $"{Name}";
        }
    }
}

public record SpotifyTrackModel(string? Artist, string? Name, string? Id, string? Uri)
{
    public override string ToString()
    {
        return $"{Artist} - {Name}";
    }
}