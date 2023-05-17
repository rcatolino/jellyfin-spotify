using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Jellyfin.Api;
using Jellyfin.Api.Extensions;
using Jellyfin.Data.Entities;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Spotify.Models;

namespace Spotify.Controllers
{
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
        private readonly IMemoryCache _memoryCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpotifyController"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        /// <param name="memoryCache">The memory cache.</param>
        public SpotifyController(
                ISessionManager sessionManager,
                IUserManager userManager,
                ILogger<SpotifyController> logger,
                IMemoryCache memoryCache,
                IHttpClientFactory httpClientFactory)
        {
            _sessionManager = sessionManager;
            _userManager = userManager;
            _logger = logger;
            _memoryCache = memoryCache;
            _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
            logger.LogInformation("Loading LibraryController");
        }

        /// <summary>
        /// Refresh access token for spotify web playback.
        /// </summary>
        /// <response code="200">Data returned.</response>
        /// <response code="404">No clientID available for this session or session not found.</response>
        /// <returns>An <see cref="IEnumerable{UserDto}"/> containing the sessions.</returns>
        [HttpGet("RefreshToken")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SpotifyAuthDataDto>> RefreshToken()
        {
            var user = _userManager.GetUserById(HttpContext.User.GetUserId());
            if (user is null)
            {
                return NotFound("User not found");
            }

            if (user.SpotifyApiKey is null)
            {
                _logger.LogWarning("Cannot refresh spotify token because no API key or RefreshToken is availaible for this user");
                return NotFound("Missing API key or RefreshToken");
            }

            if (user.SpotifyRefreshToken is null)
            {
                // We don't have any refresh token, we must login
                return LoginRedirect(user.SpotifyApiKey, user.Id);
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
            var jsonBody = JsonDocument.Parse(body);
            if (resp.StatusCode == HttpStatusCode.BadRequest && jsonBody.RootElement.GetProperty("error").GetString() == "invalid_grant")
            {
                // The refresh token isn't valid anymore, we must login
                return LoginRedirect(user.SpotifyApiKey, user.Id);
            }
            else if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Failed to refresh spotify token, code {Code} : {Text}", resp.StatusCode, body);
                return NotFound("Spotify API error");
            }

            user.SpotifyWPToken = jsonBody.RootElement.GetProperty("access_token").GetString();
            if (user.SpotifyWPToken is null)
            {
                _logger.LogWarning("Spotify refreshed access token without error but the new access_token is missing from the server response");
                return NotFound("Spotify API error");
            }

            if (jsonBody.RootElement.TryGetProperty("refresh_token", out var newRefresh))
            {
                user.SpotifyRefreshToken = newRefresh.GetString();
            }

            return Ok(new SpotifyAuthDataDto { AccessToken = user.SpotifyWPToken });
        }

        private ActionResult<SpotifyAuthDataDto> LoginRedirect(string apiKey, Guid userId)
        {
            var rand = new Random();
            var state_bytes = new byte[16];
            rand.NextBytes(state_bytes);
            var state = Convert.ToBase64String(state_bytes);
            var clientId = apiKey.Split(':')[0];
            _logger.LogInformation("Spotify Login Prep, setting state {S} for user {U}", state, userId);
            _memoryCache.Set(state, userId, new TimeSpan(0, 5, 0)); // We need to save the state somewhere for the verification in SpotifyAuthCallback
            var redirect = HttpUtility.UrlEncode($"http://localhost:8096/Spotify/AuthCallback");
            var scopes = HttpUtility.UrlEncode("streaming user-read-email user-read-private user-modify-playback-state");
            var url = $"client_id={clientId}&response_type=code&redirect_uri={redirect}&state={HttpUtility.UrlEncode(state)}&scope={scopes}";
            return Ok(new SpotifyAuthDataDto { RedirectURL = $"https://accounts.spotify.com/authorize?{url}" });
        }

        /// <summary>
        /// Sets an API app ID/Key for spotify web playback.
        /// </summary>
        /// <param name="apiParams">Json object containing the ID and the key in the apiId et apiKey fields.</param>
        /// <response code="200">API Key validation result.</response>
        /// <response code="404">Session/User not found.</response>
        /// <returns>A boolean indicating if the key is valid.</returns>
        [HttpPost("ApiId")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<bool>> SetApiKey([FromBody, Required] JsonDocument apiParams)
        {
            var user = _userManager.GetUserById(HttpContext.User.GetUserId());
            if (user is null || user.Id.Equals(Guid.Empty))
            {
                return NotFound("User not found");
            }

            var apiId = apiParams.RootElement.GetProperty("apiId").GetString();
            var apiKey = apiParams.RootElement.GetProperty("apiKey").GetString();
            var spotifyKey = $"{apiId}:{apiKey}";

            // Try this key before saving it
            string authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(spotifyKey));
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            requestMessage.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");
            requestMessage.Headers.Add("Authorization", "Basic " + authToken);
            HttpResponseMessage resp = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Spotify API Key validation failed with code {Code} : {Text}", resp.StatusCode, body);
                return false;
            }

            user.SpotifyApiKey = spotifyKey;
            return true;
        }

        /// <summary>
        /// Gets the current user spotify API Key Id.
        /// </summary>
        /// <response code="200">Data returned.</response>
        /// <response code="404">No API Id available for this session or session not found.</response>
        /// <returns>A string containing the API key ID.</returns>
        [HttpGet("ApiId")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<string> GetApiId()
        {
            var user = _userManager.GetUserById(HttpContext.User.GetUserId());
            if (user is null || user.Id.Equals(Guid.Empty))
            {
                return NotFound("User not found");
            }

            if (user.SpotifyApiKey is string key)
            {
                return user.SpotifyApiKey.Split(':')[0];
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets an access token for spotify web playback.
        /// </summary>
        /// <response code="200">Data returned.</response>
        /// <response code="404">No clientID available for this session or session not found.</response>
        /// <returns>A <see cref="SpotifyAuthDataDto"/> containing the sessions.</returns>
        [HttpGet("AccessToken")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<SpotifyAuthDataDto> GetAccessToken()
        {
            var user = _userManager.GetUserById(HttpContext.User.GetUserId());
            if (user is null || user.Id.Equals(Guid.Empty))
            {
                return NotFound("User not found");
            }

            if (user.SpotifyWPToken is string token && token.Length != 0)
            {
                return Ok(new SpotifyAuthDataDto { AccessToken = token });
            }

            if (user.SpotifyApiKey is string key)
            {
                return LoginRedirect(key, user.Id);
            }

            return NotFound("No API key available for user");
        }

        /// <summary>
        /// Callback from spotify during oauth.
        /// </summary>
        /// <param name="code">The authorization code.</param>
        /// <param name="state">Oauth auth state.</param>
        /// <param name="error">Error from spotify.</param>
        /// <response code="302">Redirect to spotify auth page.</response>
        /// <response code="404">Session/User not found or bad state.</response>
        /// <returns>An <see cref="IEnumerable{UserDto}"/> containing the sessions.</returns>
        [HttpGet("AuthCallback")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> SpotifyAuthCallback(
                [FromQuery] string? code,
                [FromQuery, Required] string state,
                [FromQuery] string? error)
        {
            Guid? userId = _memoryCache.Get<Guid>(state);
            if (userId is null || userId.Equals(Guid.Empty))
            {
                _logger.LogInformation("Spotify OAuth callback, state check failed for {S}. UserId is {U}", state, userId);
                return NotFound("State check failed");
            }

            _logger.LogInformation("Spotify OAuth callback, state check OK for user {Uid}", userId);
            var user = _userManager.GetUserById(userId ?? Guid.Empty);
            if (user is null)
            {
                return NotFound("User not found");
            }

            if (error is not null)
            {
                _logger.LogInformation("Error during spotify callback : {E}", error);
                return NotFound("Spotify OAuth error");
            }

            if (code is null)
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
                { "code", code },
                { "redirect_uri", $"http://localhost:8096/Spotify/AuthCallback" },
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
            _ = UpdateUserCountry(user); // Don't wait for the country to be updated to continue
            return Redirect("/web/index.html");
        }

        private async Task UpdateUserCountry(User user)
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
            requestMessage.Headers.Add("Authorization", "Bearer " + user.SpotifyWPToken);
            var resp = await _httpClient.SendAsync(requestMessage).ConfigureAwait(true);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Failed to get user's spotify account, code {Code} : {Text}", resp.StatusCode, body);
                return;
            }

            user.SpotifyMarket = JsonDocument.Parse(body).RootElement.GetProperty("country").GetString();
        }
    }
}
