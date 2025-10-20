using System;
using System.Threading.Tasks;
using AndroidSideloader.Utilities;
using Avalonia.Threading;
using AvaloniaWebView;

namespace AndroidSideloader.Services;

/// <summary>
/// Service for managing trailer video playback using Avalonia WebView
/// Directly embeds YouTube videos using native platform WebViews (WKWebView on macOS, WebView2 on Windows)
/// Much faster than VLC approach - no CEF interference with subprocesses
/// Uses lazy initialization to avoid blocking app startup
/// </summary>
public class WebViewTrailerPlayerService : IDisposable
{
    private WebView _webView;
    private string _currentVideoId;

    public WebViewTrailerPlayerService()
    {
        Logger.Log("WebViewTrailerPlayerService created");
    }

    /// <summary>
    /// Initializes the service with the WebView control
    /// Must be called before playing videos
    /// </summary>
    public void Initialize(WebView webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        Logger.Log("WebViewTrailerPlayerService initialized");
    }

    /// <summary>
    /// Gets the current video ID being played
    /// </summary>
    public string CurrentVideoId => _currentVideoId;

    /// <summary>
    /// Loads and plays a YouTube video by embedding it directly
    /// Much faster than VLC approach (no yt-dlp extraction needed)
    /// </summary>
    /// <param name="youtubeUrl">YouTube video URL (e.g., https://www.youtube.com/watch?v=xxxxx)</param>
    /// <returns>True if video loaded successfully</returns>
    public async Task<bool> LoadYouTubeVideoAsync(string youtubeUrl)
    {
        if (_webView == null)
        {
            Logger.Log("WebView not initialized", LogLevel.Error);
            return false;
        }

        try
        {
            Logger.Log($"Loading YouTube video: {youtubeUrl}");

            // Extract video ID from URL
            var videoId = ExtractVideoId(youtubeUrl);
            if (string.IsNullOrEmpty(videoId))
            {
                Logger.Log("Failed to extract video ID from URL", LogLevel.Error);
                return false;
            }

            _currentVideoId = videoId;

            // Create an embedded YouTube player URL with autoplay and no controls/branding
            // Using embed URL instead of watch URL for better embedding
            var embedUrl = $"https://www.youtube.com/embed/{videoId}?autoplay=1&mute=1&controls=0&modestbranding=1&rel=0&showinfo=0&iv_load_policy=3";

            Logger.Log($"Navigating to embed URL: {embedUrl}");

            // Navigate to the YouTube embed URL (must be on UI thread)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _webView.Url = new Uri(embedUrl);
            });

            Logger.Log("Trailer loaded successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error loading YouTube video: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// Extracts video ID from YouTube URL
    /// Supports: https://www.youtube.com/watch?v=xxxxx, https://youtu.be/xxxxx
    /// </summary>
    private static string ExtractVideoId(string youtubeUrl)
    {
        try
        {
            var uri = new Uri(youtubeUrl);

            // Handle youtu.be short URLs
            if (uri.Host.Contains("youtu.be"))
            {
                return uri.AbsolutePath.TrimStart('/');
            }

            // Handle youtube.com URLs
            if (uri.Host.Contains("youtube.com"))
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                return query["v"];
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the current video playback by navigating to blank page
    /// </summary>
    public void Clear()
    {
        if (_webView == null)
            return;

        try
        {
            Logger.Log("Clearing trailer");

            // Must run on UI thread
            Dispatcher.UIThread.Post(() =>
            {
                _webView.Url = new Uri("about:blank");
            });

            _currentVideoId = null;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error clearing trailer: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Stops the current video playback (alias for Clear)
    /// </summary>
    public void Stop()
    {
        Clear();
    }

    public void Dispose()
    {
        try
        {
            Clear();
            _webView = null;
            Logger.Log("WebViewTrailerPlayerService disposed");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error disposing WebViewTrailerPlayerService: {ex.Message}", LogLevel.Error);
        }
    }
}
