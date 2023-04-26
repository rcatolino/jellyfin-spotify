using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Spotify controller.
/// </summary>
[Route("Spotify")]
public class SpotifyController : BaseJellyfinApiController
{
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyController"/> class.
    /// </summary>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
    public SpotifyController(
            ISessionManager sessionManager,
            IUserManager userManager,
            ILogger<SpotifyController> logger,
            IHttpClientFactory httpClientFactory)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
    }

    /// <summary>
    /// Refresh access token for spotify web playback.
    /// </summary>
    /// <response code="200">Data returned.</response>
    /// <response code="404">No clientID available for this session or session not found.</response>
    /// <returns>An <see cref="IEnumerable{UserDto}"/> containing the sessions.</returns>
    [HttpGet("AccessToken")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpotifyAuthDataDto>> RefreshToken()
    {
        var session = await RequestHelpers.GetSession(_sessionManager, _userManager, HttpContext).ConfigureAwait(false);
        if (session is null)
        {
            return NotFound("Session not found");
        }

        var user = _userManager.GetUserById(session.UserId);
        if (user is null)
        {
            return NotFound("User not found");
        }

        if (user.SpotifyApiKey is null || user.SpotifyRefreshToken is null)
        {
            _logger.LogWarning("Cannot refresh spotify token because no API key or RefreshToken is availaible for this user");
            return NotFound("Missing API key or RefreshToken");
        }

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        var form = new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "refresh_token", user.SpotifyRefreshToken },
        };
        string authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(user.SpotifyApiKey));
        requestMessage.Content = new FormUrlEncodedContent(form);
        requestMessage.Headers.Add("Authorization", "Basic " + authToken);
        var resp = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning("Failed to refresh spotify token, code {Code} : {Text}", resp.StatusCode, body);
            return NotFound("Spotify API error");
        }

        var jsonBody = JsonDocument.Parse(body);
        user.SpotifyWPToken = jsonBody.RootElement.GetProperty("access_token").GetString();
        if (user.SpotifyWPToken is null)
        {
            _logger.LogWarning("Spotify refreshed access token without error but the new access_token is missing the server response");
            return NotFound("Spotify API error");
        }

        var newRefresh = jsonBody.RootElement.GetProperty("refresh_token").GetString();
        if (newRefresh is not null)
        {
            user.SpotifyWPToken = newRefresh;
        }

        return Ok(new SpotifyAuthDataDto(user.SpotifyWPToken));
    }

     /// <summary>
    /// Gets an access token for spotify web playback.
    /// </summary>
    /// <response code="200">Data returned.</response>
    /// <response code="302">Redirect to spotify auth page.</response>
    /// <response code="404">No clientID available for this session or session not found.</response>
    /// <returns>An <see cref="IEnumerable{UserDto}"/> containing the sessions.</returns>
    [HttpGet("AccessToken")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpotifyAuthDataDto>> GetAccessToken()
    {
        var session = await RequestHelpers.GetSession(_sessionManager, _userManager, HttpContext).ConfigureAwait(false);
        _logger.LogInformation("Get spotify authorization data for session {Id}", session.Id);
        if (session is null)
        {
            return NotFound("Session not found");
        }

        var user = _userManager.GetUserById(session.UserId);
        if (user is null)
        {
            return NotFound("User not found");
        }

        if (user.SpotifyWPToken is string token && token.Length != 0)
        {
            return Ok(new SpotifyAuthDataDto(token));
        }

        if (user.SpotifyApiKey is string key)
        {
            // We don't have any access token, but we have an API key, perform oauth authorization
            var rand = new Random();
            var state_bytes = new byte[16];
            rand.NextBytes(state_bytes);
            var state = Convert.ToBase64String(state_bytes);
            var clientId = key.Split(':')[0];
            session.SpotifyAuthState = state; // We need to save the state somewhere for the verification in SpotifyAuthCallback
            var redirect = $"http://localhost:8096/Spotify/AuthCallback/{session.Id}"; // TODO: fix
            var url = $"client_id={clientId}&response_type=code&redirect_uri={redirect}&state={state}&scope=streaming";
            return Redirect($"https://accounts.spotify.com/authorize?{HttpUtility.UrlEncode(url)}");
        }

        return NotFound("No API key available for user");
    }

    /// <summary>
    /// Callback from spotify during oauth.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="authCode">The authorization code.</param>
    /// <param name="state">Oauth auth state.</param>
    /// <param name="error">Error from spotify.</param>
    /// <response code="302">Redirect to spotify auth page.</response>
    /// <response code="404">Session/User not found or bad state.</response>
    /// <returns>An <see cref="IEnumerable{UserDto}"/> containing the sessions.</returns>
    [HttpGet("AuthCallback/{UserId}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SpotifyAuthCallback(
            [FromRoute, Required] string sessionId,
            [FromQuery] string? authCode,
            [FromQuery, Required] string state,
            [FromQuery] string? error)
    {
        var session = _sessionManager.Sessions.FirstOrDefault(i => string.Equals(i.Id, sessionId, StringComparison.Ordinal));
        if (session is null)
        {
            return NotFound("Session not found");
        }

        var user = _userManager.GetUserById(session.UserId);
        if (user is null)
        {
            return NotFound("User not found");
        }

        if (session.SpotifyAuthState != state)
        {
            _logger.LogWarning("Error during spotify callback, exptected state {State} differs from the one received {StateR}", session.SpotifyAuthState, state);
            return NotFound("Invalid state");
        }

        if (error is not null)
        {
            _logger.LogInformation("Error during spotify callback : {E}", error);
            return NotFound("Spotify OAuth error");
        }

        if (authCode is null)
        {
            _logger.LogInformation("Error during spotify callback, missing auth code");
            return NotFound("Spotify OAuth error : no auth code returned");
        }

        // We have a valid auth code ! Exchange it for an access token (and a refresh token)
        if (user.SpotifyApiKey is null)
        {
            // How would we even get to this point ?
            _logger.LogWarning("Cannot complete spotify auth because no API key is availaible for this user");
            return NotFound("Missing API key");
        }

        string authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(user.SpotifyApiKey));
        var form = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", authCode },
            { "redirect_uri", $"http://localhost:8096/Spotify/AuthCallback/{session.Id}" },
        };
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        requestMessage.Content = new FormUrlEncodedContent(form);
        requestMessage.Headers.Add("Authorization", "Basic " + authToken);
        var resp = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning("Failed to get spotify token, code {Code} : {Text}", resp.StatusCode, body);
            return NotFound("Spotify API error");
        }

        var jsonBody = JsonDocument.Parse(body);
        user.SpotifyWPToken = jsonBody.RootElement.GetProperty("access_token").GetString();
        user.SpotifyRefreshToken = jsonBody.RootElement.GetProperty("refresh_token").GetString();
        return Redirect("/web/index.html");
    }
}
