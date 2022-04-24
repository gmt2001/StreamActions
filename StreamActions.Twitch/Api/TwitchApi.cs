﻿/*
 * This file is part of StreamActions.
 * Copyright © 2019-2022 StreamActions Team (streamactions.github.io)
 *
 * StreamActions is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * StreamActions is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with StreamActions.  If not, see <https://www.gnu.org/licenses/>.
 */

using Microsoft.Extensions.Logging;
using StreamActions.Common;
using StreamActions.Common.Logger;
using StreamActions.Twitch.Api.Common;
using StreamActions.Twitch.Api.OAuth;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StreamActions.Twitch.Api
{
    /// <summary>
    /// Handles validation of OAuth tokens and performs HTTP calls for the API.
    /// </summary>
    public static class TwitchApi
    {
        #region Public Events

        /// <summary>
        /// Fires when a <see cref="TwitchSession"/> has its OAuth token automatically refreshed as a result of a 401 response.
        /// </summary>
        public static event EventHandler<TokenRefreshedEventArgs>? OnTokenRefreshed;

        #endregion Public Events

        #region Public Properties

        /// <summary>
        /// The currently initialized Client Id.
        /// </summary>
        public static string? ClientId { get; private set; }

        /// <summary>
        /// The <see cref="JsonSerializerOptions"/> that should be used for all serialization and deserialization.
        /// </summary>
        public static JsonSerializerOptions SerializerOptions => new()
        {
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        #endregion Public Properties

        #region Public Methods

        /// <summary>
        /// Initializes the HTTP client.
        /// </summary>
        /// <param name="clientId">A valid Twitch App Client Id.</param>
        /// <param name="clientSecret">A valid Twitch App Client Secret.</param>
        /// <param name="baseAddress">The base uri to Helix.</param>
        /// <exception cref="ArgumentNullException"><paramref name="clientId"/> is null or whitespace.</exception>
        public static void Init(string clientId, string? clientSecret = null, string baseAddress = "https://api.twitch.tv/helix/")
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _logger.ArgumentNull(nameof(TwitchApi), nameof(Init), nameof(clientId));
                throw new ArgumentNullException(nameof(clientId));
            }

            ClientId = clientId;
            ClientSecret = clientSecret;

            _ = _httpClient.DefaultRequestHeaders.Remove("Client-Id");
            _ = _httpClient.DefaultRequestHeaders.Remove("User-Agent");

            _httpClient.DefaultRequestHeaders.Add("Client-Id", clientId);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "StreamActions/TwitchAPI/" + typeof(TwitchApi).Assembly.GetName()?.Version?.ToString() ?? "0.0.0.0");
            _httpClient.BaseAddress = new Uri(baseAddress);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        #endregion Public Methods

        #region Internal Properties

        /// <summary>
        /// The currently initialized Client Secret.
        /// </summary>
        internal static string? ClientSecret { get; private set; }

        #endregion Internal Properties

        #region Internal Methods

        /// <summary>
        /// Submits a HTTP request to the specified Uri and returns the response.
        /// </summary>
        /// <param name="method">The <see cref="HttpMethod"/> of the request.</param>
        /// <param name="uri">The uri to request. Relative uris resolve against the Helix base.</param>
        /// <param name="session">The <see cref="TwitchSession"/> to authorize the request.</param>
        /// <param name="content">The body of the request, for methods that require it.</param>
        /// <returns>A <see cref="HttpResponseMessage"/> containing the response data.</returns>
        /// <exception cref="InvalidOperationException">Did not call <see cref="Init(string)"/> with a valid Client Id or <paramref name="session"/> does not have an OAuth token.</exception>
        /// <remarks>
        /// <para>
        /// Non-JSON responses are converted to the standard <c>{status,error,message}</c> format.
        /// </para>
        /// <para>
        /// If a 401 is returned, one attempt will be made to refresh the OAuth token and try again.
        /// The <paramref name="session"/> object is updated and <see cref="OnTokenRefreshed"/> is called on refresh success.
        /// </para>
        /// </remarks>
        internal static async Task<HttpResponseMessage> PerformHttpRequest(HttpMethod method, Uri uri, TwitchSession session, HttpContent? content = null)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Client-Id"))
            {
                _logger.InvalidOperation(nameof(TwitchApi), nameof(PerformHttpRequest), "Must call Init first");
                throw new InvalidOperationException("Must call TwitchAPI.Init.");
            }

            if (string.IsNullOrWhiteSpace(session.Token?.OAuth))
            {
                _logger.InvalidOperation(nameof(TwitchApi), nameof(PerformHttpRequest), "OAuth token was null, blank, or whitespace");
                throw new InvalidOperationException("Invalid OAuth token in session.");
            }

            bool retry = false;
        performhttprequest_start:
            try
            {
                await session.RateLimiter.WaitForRateLimit(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                using HttpRequestMessage request = new(method, uri) { Version = _httpClient.DefaultRequestVersion, VersionPolicy = _httpClient.DefaultVersionPolicy };
                request.Content = content;

                if (session.Token.OAuth != "__NEW")
                {
                    request.Headers.Add("Authorization", "Bearer " + session.Token.OAuth);
                }

                HttpResponseMessage response = await _httpClient.SendAsync(request, CancellationToken.None).ConfigureAwait(false);

                if (!retry && response.StatusCode == System.Net.HttpStatusCode.Unauthorized && session.Token.OAuth != "__NEW" && !string.IsNullOrWhiteSpace(session.Token.Refresh))
                {
                    Token? refresh = await Token.RefreshOAuth(session).ConfigureAwait(false);

                    if (refresh is not null && refresh.IsSuccessStatusCode)
                    {
                        session.Token = new() { OAuth = refresh.AccessToken, Refresh = refresh.RefreshToken, Expires = refresh.Expires, Scopes = refresh.Scopes };
                        _ = OnTokenRefreshed?.InvokeAsync(null, new(session));
                        retry = true;
                        goto performhttprequest_start;
                    }
                }

                if (response.Content.Headers?.ContentType?.MediaType != "application/json")
                {
                    string rcontent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!Regex.IsMatch(rcontent, @"\s*{.*}\s*"))
                    {
                        response.Content = new StringContent(JsonSerializer.Serialize(new TwitchResponse { Status = response.StatusCode, Message = rcontent }, SerializerOptions));
                    }
                }

                session.RateLimiter.ParseHeaders(response.Headers);

                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                return new(0) { ReasonPhrase = ex.GetType().Name, Content = new StringContent(ex.Message) };
            }
        }

        #endregion Internal Methods

        #region Private Fields

        /// <summary>
        /// The <see cref="HttpClient"/> that is used for all requests.
        /// </summary>
        private static readonly HttpClient _httpClient = new();

        /// <summary>
        /// The <see cref="ILogger"/> for logging.
        /// </summary>
        private static readonly ILogger _logger = Logger.GetLogger(typeof(TwitchApi));

        #endregion Private Fields
    }
}
