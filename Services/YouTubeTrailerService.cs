using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AndroidSideloader.Utilities;

namespace AndroidSideloader.Services;

/// <summary>
/// Service for searching and retrieving YouTube video trailers
/// Scrapes YouTube search results to find video URLs (like original implementation)
/// </summary>
public class YouTubeTrailerService
{
    private static readonly Regex VideoIdRegex = new("^[a-zA-Z0-9_-]{11}$", RegexOptions.Compiled);
    
    private static readonly Regex[] YouTubeUrlRegexs =
    [
        new(@"""videoId"":""([^""]+)""", RegexOptions.Compiled),  // Current YouTube format
        new(@"url""\:\""/watch\?v\=([^""]+)""", RegexOptions.Compiled),  // Original pattern
        new(@"/watch\?v=([a-zA-Z0-9_-]{11})", RegexOptions.Compiled),  // Direct watch URL
        new(@"""watchEndpoint"":{""videoId"":""([^""]+)""", RegexOptions.Compiled)  // Alternative format
    ];
    
    /// <summary>
    /// Searches YouTube for a VR game trailer by scraping HTML
    /// </summary>
    /// <param name="gameName">Name of the game to search for</param>
    /// <returns>YouTube video URL if found, null otherwise</returns>
    public static async Task<string> SearchForTrailerAsync(string gameName)
    {
        try
        {
            Logger.Log($"Searching YouTube for: {gameName} VR trailer");

            // Create search query and URL (same as original)
            var query = $"{gameName} VR trailer";
            var encodedQuery = WebUtility.UrlEncode(query);
            var searchUrl = $"https://www.youtube.com/results?search_query={encodedQuery}";

            // Download the search results page HTML
            string html;
            using (var client = new HttpClient())
            {
                html = await client.GetStringAsync(searchUrl);
            }

            // Extract the first video URL using regex (same pattern as original)
            var videoUrl = ExtractVideoUrl(html);

            if (!string.IsNullOrEmpty(videoUrl))
            {
                Logger.Log($"Found trailer: {videoUrl}");
                return videoUrl;
            }

            Logger.Log($"No trailer found for: {gameName}", LogLevel.Warning);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error searching YouTube for {gameName}: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// Extracts video URL from YouTube search results HTML
    /// Uses multiple regex patterns to handle YouTube's changing HTML structure
    /// </summary>
    private static string ExtractVideoUrl(string html)
    {
        try
        {
            foreach (var pattern in YouTubeUrlRegexs)
            {
                var match = pattern.Match(html);
                if (match.Success && match.Groups.Count > 1)
                {
                    var videoId = match.Groups[1].Value;

                    // Ensure videoId looks valid (11 characters, alphanumeric plus _ and -)
                    if (!string.IsNullOrEmpty(videoId) 
                        && videoId.Length == 11 
                        && VideoIdRegex.IsMatch(videoId))
                    {
                        Logger.Log($"Extracted video ID: {videoId} using pattern: {pattern}");
                        return $"https://www.youtube.com/watch?v={videoId}";
                    }
                }
            }

            Logger.Log("No valid video ID found in HTML", LogLevel.Warning);
            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error extracting video URL from HTML: {ex.Message}", LogLevel.Error);
            return string.Empty;
        }
    }
}
