namespace Jellyfin.Api.Models;

/// <summary>
/// Spotify auth data.
/// </summary>
public class SpotifyAuthDataDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyAuthDataDto"/> class.
    /// </summary>
    /// <param name="accessToken">spotify access token.</param>
    public SpotifyAuthDataDto(string accessToken)
    {
        AccessToken = accessToken;
    }

    /// <summary>
    /// Gets spotify clientID for a user.
    /// </summary>
    public string AccessToken { get; }
}
