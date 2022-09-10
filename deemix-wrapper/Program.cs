using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Text.Json.Serialization;
using System.Web;
using Sharprompt;

namespace deemix_wrapper;

internal record DeezerResponse
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    [JsonPropertyName("data")] public IEnumerable<DeezerTrackData>? Data { get; set; }
}

internal record DeezerTrackData
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("artist")] public Artist? Artist { get; set; }
    [JsonPropertyName("link")] public string? Link { get; set; }
    [JsonPropertyName("album")] public AlbumData? Album { get; set; }
}

record AlbumData
{
    [JsonPropertyName("cover_big")] public string? AlbumCover { get; set; }
}

public record Artist
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    // ReSharper restore UnusedAutoPropertyAccessor.Global
}

public static class Program
{
    private const string DeezerApiUrl = "https://api.deezer.com/search/track?q={0}&limit=10";
    private static readonly HttpClient Client = new();
    private static bool _syncSpotify = true;
    private static void Exiting() => Console.CursorVisible = true;

    private static void HandleSelectionChange(Track newTrack)
    {
        var albumThumbnail = newTrack.AlbumThumbnail;
        if (albumThumbnail is not null)
            Process.Start("viu", new[] { albumThumbnail }).WaitForExitAsync();
    }

    private static async Task Main(string[] args)
    {
        // This is a bug in the SWAN Logging library, need this hack to bring back the cursor
        AppDomain.CurrentDomain.ProcessExit += (sender, e) => Exiting();
        
        if (args.Contains("--no-sync"))
            _syncSpotify = false;

        if (_syncSpotify)
            await Spotify.Init();
        
        
        List<Track> selectedSongs = new();
        foreach (var trackTitle in args.Where(song => song != "--no-sync"))
        {
            var requestUri = string.Format(DeezerApiUrl, HttpUtility.UrlEncode(trackTitle));
            var response = await Client.GetFromJsonAsync<DeezerResponse>(requestUri);
            if (response?.Data == null) throw new Exception("Received an empty response from deezer API.");

            if (!response.Data.Any())
            {
                Console.WriteLine($"No songs found matching the given title: \"{trackTitle}\"");
                continue;
            }

            var songs = response.Data.Select(song => new Track(Artist: song.Artist?.Name, Title: song.Title, Link: song.Link, thumbnail: song.Album?.AlbumCover)).ToList();
        }

        if (selectedSongs.Count == 0)
        {
            Console.WriteLine("No songs to download. Exiting...");
            Environment.Exit(0);
        }

        if (_syncSpotify)
            await Spotify.AddToPlaylist(selectedSongs);
        
        await Process.Start("deemix", selectedSongs.Select(s => s.Link)!).WaitForExitAsync();
    }


    public record Track(string? Title, string? Artist, string? Link, string? thumbnail)
    {
        public readonly string? Artist = Artist;
        public readonly string? Title = Title;
        public readonly string? Link = Link;
        public readonly string? AlbumThumbnail = thumbnail;

        public override string ToString()
        {
            return $"{Artist} - {Title}";
        }
    }
}