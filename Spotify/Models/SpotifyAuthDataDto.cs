namespace Spotify.Models;

/// <summary>
/// Spotify auth data.
/// </summary>
public class SpotifyAuthDataDto
{
    /// <summary>
    /// Gets or sets spotify access token for a user.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets spotify redirectURL for a user.
    /// </summary>
    public string? RedirectURL { get; set; }
}
