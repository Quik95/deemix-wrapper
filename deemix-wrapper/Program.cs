using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace deemix_wrapper;

record DeezerResponse
{
    [JsonPropertyName("data")] public IEnumerable<DeezerTrackData> Data { get; init; }
}

record DeezerTrackData
{
    [JsonPropertyName("id")] public int ID { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; }
    [JsonPropertyName("artist")] public Artist Artist { get; init; }
    [JsonPropertyName("link")] public string Link { get; init; }
}

public record Artist
{
    [JsonPropertyName("name")] public string Name { get; init; }
}

internal static class Program
{
    private const string DeezerApiUrl = "https://api.deezer.com/search/track?q={0}&limit=10";
    private static readonly HttpClient Client = new();
    private static async Task Main(string[] args)
    {
        List<string> downloadLinks = new();

        foreach (var trackTitle in args)
        {
            var requestUri = string.Format(DeezerApiUrl, HttpUtility.UrlEncode(trackTitle));
            var response = await Client.GetFromJsonAsync<DeezerResponse>(requestUri);
                if (response?.Data == null) throw new Exception("Empty response");

                foreach (var (item, i) in response.Data.Select((value, i) => (value, i)))
                {
                    Console.WriteLine($"{i + 1}: {item.Artist.Name} - {item.Title} => {item.Link}");
                }
                
                Console.Write("Selected song: ");
                var choice = int.Parse(Console.ReadLine()!.Normalize());

                try
                {
                    var chosenTrack = response.Data.ToList()[choice-1];
                    Console.WriteLine($"Chosen track: {chosenTrack.Artist.Name} - {chosenTrack.Title} => {chosenTrack.Link}");
                    downloadLinks.Add(chosenTrack.Link);
                }
                catch (ArgumentOutOfRangeException)
                {
                    Console.WriteLine("Invalid choice.");
                    Environment.Exit(1);
                }
        }

        await Process.Start("deemix", downloadLinks).WaitForExitAsync();
    }
}