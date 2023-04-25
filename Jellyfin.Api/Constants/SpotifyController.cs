using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyController"/> class.
    /// </summary>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    public SpotifyController(ISessionManager sessionManager, IUserManager userManager, ILogger<SpotifyController> logger)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets the data needed for a spotify authorization request (clientId and state).
    /// </summary>
    /// <response code="200">Data returned returned.</response>
    /// <response code="404">No clientID available for this session or session not found.</response>
    /// <returns>An <see cref="IEnumerable{UserDto}"/> containing the sessions.</returns>
    [HttpGet("AuthorizationData")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpotifyAuthDataDto>> GetData()
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

        if (user.SpotifyApiKey is string key)
        {
            var rand = new Random();
            var state_bytes = new byte[16];
            rand.NextBytes(state_bytes);
            var state = Convert.ToBase64String(state_bytes);
            var resp = new SpotifyAuthDataDto(key.Split(':')[0], state);
            session.SpotifyAuthState = state;
            return Ok(resp);
        }

        return NotFound("Client ID not found");
    }
}
