namespace Jellyfin.Api.Models;

/// <summary>
/// Spotify auth data.
/// </summary>
public class SpotifyAuthDataDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyAuthDataDto"/> class.
    /// </summary>
    /// <param name="clientId">spotify client id.</param>
    /// <param name="state">spotify auth state.</param>
    public SpotifyAuthDataDto(string clientId, string state)
    {
        ClientId = clientId;
        State = state;
    }

    /// <summary>
    /// Gets spotify clientID for a user.
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// Gets spotify auth state for a user.
    /// </summary>
    public string State { get; }
}
