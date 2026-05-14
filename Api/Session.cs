using System;
using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Net;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
using KoenZomers.Ring.Api.Entities;

namespace KoenZomers.Ring.Api
{
    public class Session
    {
        #region Properties

        /// <summary>
        /// Username to use to connect to the Ring API. Set by providing it in the constructor.
        /// </summary>
        public string Username { get; private set; }

        /// <summary>
        /// Password to use to connect to the Ring API. Set by providing it in the constructor.
        /// </summary>
        public string Password { get; private set; }

        /// <summary>
        /// The device hardware id.
        /// </summary>
        public string HardwareId { get; private set; }

        /// <summary>
        /// Uri on which OAuth tokens can be requested from Ring
        /// </summary>
        public Uri RingApiOAuthUrl => new Uri("https://oauth.ring.com/oauth/token");

        /// <summary>
        /// Base Uri with which all Ring API requests start
        /// </summary>
        public Uri RingApiBaseUrl => new Uri("https://api.ring.com/clients_api/");

        /// <summary>
        /// Base Uri used by the current Ring snapshot service.
        /// </summary>
        public Uri RingSnapshotBaseUrl => new Uri("https://app-snaps.ring.com/snapshots/");

        /// <summary>
        /// Boolean indicating if the current session is authenticated
        /// </summary>
        public bool IsAuthenticated => OAuthToken != null;

        /// <summary>
        /// Authentication Token that will be used to communicate with the Ring API
        /// </summary>
        public string AuthenticationToken
        {
            get { return OAuthToken?.AccessToken; }
        }

        /// <summary>
        /// OAuth Token for communicating with the Ring API
        /// </summary>
        public OAutToken OAuthToken { get; private set; }

        #endregion

        #region Fields

        /// <summary>
        /// HttpUtility instance to make HTTP requests
        /// </summary>
        private readonly IHttpUtility _httpUtility;

        #endregion

        #region Constructors

        /// <summary>
        /// Initiates a new session to the Ring API
        /// </summary>
        public Session(string username, string password, string hardwareId, IHttpUtility httpUtility = null)
        {
            Username = username;
            Password = password;
            HardwareId = hardwareId;
            _httpUtility = httpUtility ?? new HttpUtility();
        }

        /// <summary>
        /// Initiates a new session without username/password. Only to be used with the static method to create a session based on a RefreshToken.
        /// </summary>
        private Session()
        {
            _httpUtility = new HttpUtility();
        }

        /// <summary>
        /// Initiates a new session with a custom HTTP utility (for testing/mocking).
        /// </summary>
        private Session(IHttpUtility httpUtility)
        {
            _httpUtility = httpUtility ?? new HttpUtility();
        }

        #endregion

        #region Static methods

        /// <summary>
        /// Creates a new session to the Ring API using a RefreshToken received from a previous session
        /// </summary>
        /// <param name="refreshToken">RefreshToken received from the prior authentication</param>
        /// <param name="hardwareId">This device's hardware id.</param>
        /// <returns>Authenticated session based on the RefreshToken or NULL if the session could not be authenticated</returns>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public static async Task<Session> GetSessionByRefreshToken(string refreshToken, string hardwareId, IHttpUtility httpUtility = null, CancellationToken cancellationToken = default)
        {
            var session = new Session(httpUtility);
            session.HardwareId = hardwareId;
            await session.RefreshSession(refreshToken, cancellationToken);
            return session;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Authenticates to the Ring API
        /// </summary>
        /// <param name="operatingSystem">Operating system from which this API is accessed. Defaults to 'windows'. Required field.</param>
        /// <param name="hardwareId">Hardware identifier of the device for which this API is accessed. Defaults to 'unspecified'. Required field.</param>
        /// <param name="appBrand">Device brand for which this API is accessed. Defaults to 'ring'. Optional field.</param>
        /// <param name="deviceModel">Device model for which this API is accessed. Defaults to 'unspecified'. Optional field.</param>
        /// <param name="deviceName">Name of the device from which this API is being used. Defaults to 'unspecified'. Optional field.</param>
        /// <param name="resolution">Screen resolution on which this API is being used. Defaults to '800x600'. Optional field.</param>
        /// <param name="appVersion">Version of the app from which this API is being used. Defaults to '1.3.810'. Optional field.</param>
        /// <param name="appInstallationDate">Date and time at which the app was installed from which this API is being used. By default not specified. Optional field.</param>
        /// <param name="manufacturer">Name of the manufacturer of the product for which this API is being accessed. Defaults to 'unspecified'. Optional field.</param>
        /// <param name="deviceType">Type of device from which this API is being used. Defaults to 'tablet'. Optional field.</param>
        /// <param name="architecture">Architecture of the system from which this API is being used. Defaults to 'x64'. Optional field.</param>
        /// <param name="language">Language of the app from which this API is being used. Defaults to 'en'. Optional field.</param>
        /// <param name="twoFactorAuthCode">The two factor authentication code retrieved through a text message to authenticate to two factor authentication enabled accounts. Leave this NULL at first to retrieve the text message. Then use this method again specifying the proper number received in the text message to finalize authentication.</param>
        /// <returns>Session object if the authentication was successful</returns>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task Authenticate(string operatingSystem = "windows",
                                                            string appBrand = "ring",
                                                            string deviceModel = "unspecified",
                                                            string deviceName = "unspecified",
                                                            string resolution = "800x600",
                                                            string appVersion = "2.1.8",
                                                            DateTime? appInstallationDate = null,
                                                            string manufacturer = "unspecified",
                                                            string deviceType = "tablet",
                                                            string architecture = "x64",
                                                            string language = "en",
                                                            string twoFactorAuthCode = null,
                                                            CancellationToken cancellationToken = default)
        {
            // Check for mandatory parameters
            if (string.IsNullOrEmpty(operatingSystem))
            {
                throw new ArgumentNullException("operatingSystem", "Operating system is mandatory");
            }

            // Construct the Form POST fields to send along with the authentication request
            var oAuthformFields = new Dictionary<string, string>
            {
                { "grant_type", "password" },
                { "username", Username },
                { "password", Password },
                { "client_id", "ring_official_android" },
                { "scope", "client" }
            };

            // If a two factor auth code has been provided, add the code through the HTTP POST header
            var headerFields = new NameValueCollection
            {
                { "2fa-support", "true" },
                { "2fa-code", twoFactorAuthCode },
                { "hardware_id", HardwareId }
            };

            // Make the Form POST request to request an OAuth Token
            var oAuthResponse = await _httpUtility.PostJson(RingApiOAuthUrl,
                                                            oAuthformFields,
                                                            headerFields,
                                                            cancellationToken);


            // Deserialize the JSON result into a typed object
            OAuthToken = JsonSerializer.Deserialize<OAutToken>(oAuthResponse);


            string json = $"{{ \"device\": {{ \"metadata\" : {{ \"api_version\" : 11, \"device_model\": \"ring-client-api\"  }}, \"hardware_id\" : \"{HardwareId}\", \"os\" : \"android\"  }}}}";
            _ = await _httpUtility.SendRequest(new Uri(RingApiBaseUrl, "session"), httpMethod: System.Net.Http.HttpMethod.Post, json, OAuthToken.AccessToken, HardwareId, cancellationToken);
        }

        /// <summary>
        /// Authenticates to the Ring API using the refresh token in the current session
        /// </summary>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task RefreshSession(CancellationToken cancellationToken = default) => await RefreshSession(OAuthToken.RefreshToken, cancellationToken);

        /// <summary>
        /// Authenticates to the Ring API using the provided refresh token
        /// </summary>
        /// <param name="refreshToken">RefreshToken to set up a new authenticated session</param>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task RefreshSession(string refreshToken, CancellationToken cancellationToken = default)
        {
            // Check for mandatory parameters
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new ArgumentNullException(nameof(refreshToken), "refreshToken is mandatory");
            }

            // Construct the Form POST fields to send along with the authentication request
            var oAuthformFields = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "client_id", "ring_official_android" },
                { "scope", "client" }
            };

            // mandatory headers.
            var headerFields = new NameValueCollection()
            {
                { "2fa-support", "true" },
                { "2fa-code", "" },
                { "hardware_id", HardwareId }
            };

            // Make the Form POST request to request an OAuth Token
            var oAuthResponse = await _httpUtility.PostJson(RingApiOAuthUrl,
                                                            oAuthformFields,
                                                            headerFields,
                                                            cancellationToken);

            // Deserialize the JSON result into a typed object
            OAuthToken = JsonSerializer.Deserialize<OAutToken>(oAuthResponse);
        }

        /// <summary>
        /// Ensure that the current session is authenticated and check if the access token is still valid. If not, it will try to renew the session using the refresh token.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when the refresh token is null or empty.</exception>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.SessionNotAuthenticatedException">Thrown when there's no OAuth token, or the OAuth token has expired and there is no valid refresh token.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        /// 
        public async Task EnsureSessionValid(CancellationToken cancellationToken = default)
        {
            // Ensure the session is authenticated
            if (!IsAuthenticated)
            {
                // Session is not authenticated
                throw new Exceptions.SessionNotAuthenticatedException();
            }

            // Ensure the access token in the session is still valid
            if (OAuthToken.ExpiresAt < DateTime.UtcNow)
            {
                // Access token is no longer valid, check if we have a refresh token available to refresh the session
                if (string.IsNullOrEmpty(OAuthToken.RefreshToken))
                {
                    // No refresh token available so can't renew the session
                    throw new Exceptions.SessionNotAuthenticatedException();
                }

                Console.WriteLine("Refreshing session, Token expired at {0}", OAuthToken.ExpiresAt);

                // Refresh token available, try refreshing the session
                await RefreshSession(cancellationToken);
            }

            // All good
        }

        /// <summary>
        /// Returns all devices registered with Ring under the current account being used
        /// </summary>
        /// <returns>Devices registered with Ring under the current account</returns>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.SessionNotAuthenticatedException">Thrown when there's no OAuth token, or the OAuth token has expired and there is no valid refresh token.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task<Devices> GetRingDevices(CancellationToken cancellationToken = default)
        {
            await EnsureSessionValid(cancellationToken);

            var response = await _httpUtility.GetContents(new Uri(RingApiBaseUrl, $"ring_devices"), AuthenticationToken, HardwareId, cancellationToken);

            var devices = JsonSerializer.Deserialize<Devices>(response);
            return devices;
        }

        /// <summary>
        /// Returns a stream with the recording of the provided Ding Id of a doorbot
        /// </summary>
        /// <param name="dingId">Id of the doorbot history event to retrieve the recording for</param>
        /// <returns>Stream containing contents of the recording</returns>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.DownloadFailedException">Thrown when a download URL could not be created.</exception>
        /// <exception cref="Exceptions.SessionNotAuthenticatedException">Thrown when there's no OAuth token, or the OAuth token has expired and there is no valid refresh token.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task<Stream> GetDoorbotHistoryRecording(string dingId, CancellationToken cancellationToken = default)
        {
            var downloadUri = await GetDoorbotHistoryRecordingUri(dingId, cancellationToken);
            return await _httpUtility.DownloadFile(downloadUri, hardwareId: HardwareId, cancellationToken: cancellationToken);
        }

        private async Task<Uri> GetDoorbotHistoryRecordingUri(string dingId, CancellationToken cancellationToken = default)
        {
            await EnsureSessionValid(cancellationToken);

            // Construct the URL where to request downloading of a recording
            var downloadRequestUri = new Uri(RingApiBaseUrl, $"dings/{dingId}/share/download?disable_redirect=true");

            Entities.DownloadRecording downloadResult = null;
            for (var downloadAttempt = 1; downloadAttempt < 60; downloadAttempt++)
            {
                // Request to download the recording
                var response = await _httpUtility.GetContents(downloadRequestUri, AuthenticationToken, HardwareId, cancellationToken);

                // Parse the result
                downloadResult = JsonSerializer.Deserialize<DownloadRecording>(response);

                // If the Ring API returns an empty URL property, it means its still preparing the download on the server side. Just keep requesting the recording until it returns an URL.
                if (!string.IsNullOrWhiteSpace(downloadResult.Url))
                {
                    // URL returned is not empty, start the download from the returned URL
                    break;
                }

                // Wait one second before requesting the recording again
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            // Ensure we ended with a valid URL to download the recording from
            if (downloadResult == null || string.IsNullOrWhiteSpace(downloadResult.Url) || !Uri.TryCreate(downloadResult.Url, UriKind.Absolute, out Uri downloadUri))
            {
                throw new Exceptions.DownloadFailedException(downloadResult?.Url ?? "(no URL was created)");
            }

            return downloadUri;
        }

        /// <summary>
        /// Saves the recording of the provided Ding Id of a doorbot to the provided location
        /// </summary>
        /// <param name="dingId">Id of the doorbot history event to retrieve the recording for</param>
        /// <param name="saveAs">Full path including the filename where to save the recording</param>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.DownloadFailedException">Thrown when a download URL could not be created.</exception>
        /// <exception cref="Exceptions.SessionNotAuthenticatedException">Thrown when there's no OAuth token, or the OAuth token has expired and there is no valid refresh token.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task GetDoorbotHistoryRecording(string dingId, string saveAs, CancellationToken cancellationToken = default)
        {
            var downloadUri = await GetDoorbotHistoryRecordingUri(dingId, cancellationToken);
            await _httpUtility.DownloadFileToPath(downloadUri, saveAs, AuthenticationToken, HardwareId, cancellationToken);
        }

        /// <summary>
        /// Returns the latest available snapshot from the provided doorbot
        /// </summary>
        /// <param name="doorbotId">ID of the doorbot to retrieve the latest available snapshot from</param>
        /// <returns>Stream with the latest snapshot from the doorbot</returns>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.DownloadFailedException">Thrown when a download URL could not be created.</exception>
        /// <exception cref="Exceptions.SessionNotAuthenticatedException">Thrown when there's no OAuth token, or the OAuth token has expired and there is no valid refresh token.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task<Stream> GetLatestSnapshot(int doorbotId, bool forceRefresh = false, long? afterMilliseconds = null, CancellationToken cancellationToken = default)
        {
            await EnsureSessionValid(cancellationToken);

            var downloadSnapshotUri = GetLatestSnapshotUri(doorbotId, forceRefresh, afterMilliseconds);

            var stream = await _httpUtility.DownloadFile(downloadSnapshotUri, AuthenticationToken, HardwareId, cancellationToken);
            return stream;
        }

        private Uri GetLatestSnapshotUri(int doorbotId, bool forceRefresh = false, long? afterMilliseconds = null)
        {
            var queryParts = new List<string>();
            if (afterMilliseconds.HasValue)
            {
                queryParts.Add($"after-ms={afterMilliseconds.Value}");
            }
            if (forceRefresh)
            {
                queryParts.Add("extras=force");
            }

            var queryString = queryParts.Count > 0 ? $"?{string.Join("&", queryParts)}" : string.Empty;

            // Current Ring clients use the app-snaps endpoint for image retrieval.
            var downloadSnapshotUri = new Uri(RingSnapshotBaseUrl, $"next/{doorbotId}{queryString}");

            return downloadSnapshotUri;
        }

        /// <summary>
        /// Downloads the latest snapshot directly to disk
        /// </summary>
        /// <param name="doorbotId">ID of the doorbot to retrieve the latest available snapshot from</param>
        /// <param name="saveAs">Full path including the filename where to save the snapshot</param>
        public async Task DownloadLatestSnapshot(int doorbotId, string saveAs, bool forceRefresh = false, long? afterMilliseconds = null, CancellationToken cancellationToken = default)
        {
            var downloadSnapshotUri = GetLatestSnapshotUri(doorbotId, forceRefresh, afterMilliseconds);
            await _httpUtility.DownloadFileToPath(downloadSnapshotUri, saveAs, AuthenticationToken, HardwareId, cancellationToken);
        }

        /// <summary>
        /// Requests the Ring API to get a fresh snapshot from the provided doorbot
        /// </summary>
        /// <param name="doorbotId">ID of the doorbot to request a fresh snapshot from</param>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.DownloadFailedException">Thrown when a download URL could not be created.</exception>
        /// <exception cref="Exceptions.SessionNotAuthenticatedException">Thrown when there's no OAuth token, or the OAuth token has expired and there is no valid refresh token.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        /// <exception cref="Exceptions.UnexpectedOutcomeException">Thrown if the actual HTTP response is different from what was expected</exception>
        public async Task UpdateSnapshot(int doorbotId, CancellationToken cancellationToken = default)
        {
            await EnsureSessionValid(cancellationToken);

            // Construct the URL which will trigger the Ring API to refresh the snapshots
            var updateSnapshotUri = new Uri(RingApiBaseUrl, "snapshots/update_all");

            // Construct the body of the message
            var bodyContent = string.Concat(@"{ ""doorbot_ids"": [", doorbotId, @"], ""refresh"": true }");

            // Send the request
            await _httpUtility.SendRequestWithExpectedStatusOutcome(updateSnapshotUri, System.Net.Http.HttpMethod.Put, System.Net.HttpStatusCode.NoContent, bodyContent, AuthenticationToken, HardwareId, cancellationToken);
        }

        /// <summary>
        /// Request the date and time when the last snapshot was taken from the provided doorbot
        /// </summary>
        /// <param name="doorbotId">ID of the doorbot to request when the last snapshot was taken from</param>
        /// <returns>Entity with information regarding the last taken snapshot</returns>
        /// <exception cref="Exceptions.AuthenticationFailedException">Thrown when the refresh token is invalid.</exception>
        /// <exception cref="Exceptions.SessionNotAuthenticatedException">Thrown when there's no OAuth token, or the OAuth token has expired and there is no valid refresh token.</exception>
        /// <exception cref="Exceptions.ThrottledException">Thrown when the web server indicates too many requests have been made (HTTP 429).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationIncorrectException">Thrown when the web server indicates the two-factor code was incorrect (HTTP 400).</exception>
        /// <exception cref="Exceptions.TwoFactorAuthenticationRequiredException">Thrown when the web server indicates two-factor authentication is required (HTTP 412).</exception>
        public async Task<DoorbotTimestamps> GetDoorbotSnapshotTimestamp(int doorbotId, CancellationToken cancellationToken = default)
        {
            await EnsureSessionValid(cancellationToken);

            // Construct the URL which will request the timestamps of the latest snapshots
            var updateSnapshotUri = new Uri(RingApiBaseUrl, "snapshots/timestamps");

            // Construct the body of the message
            var bodyContent = string.Concat(@"{ ""doorbot_ids"": [", doorbotId, @"]}");

            // Send the request
            var doorbotTimestamps = await _httpUtility.SendRequest<DoorbotTimestamps>(updateSnapshotUri, System.Net.Http.HttpMethod.Post, bodyContent, AuthenticationToken, HardwareId, cancellationToken);
            return doorbotTimestamps;
        }

        /// <summary>
        /// Searches historical video events for a device.
        /// </summary>
        public async Task<VideoSearchResponse> SearchVideoHistory(int doorbotId, DateTime start, DateTime end, CancellationToken cancellationToken = default)
        {
            await EnsureSessionValid(cancellationToken);

            var dateFrom = new DateTimeOffset(start.ToUniversalTime()).ToUnixTimeMilliseconds();
            var dateTo = new DateTimeOffset(end.ToUniversalTime()).ToUnixTimeMilliseconds();
            var uri = new Uri(RingApiBaseUrl, $"video_search/history?doorbot_id={doorbotId}&date_from={dateFrom}&date_to={dateTo}&order=asc&api_version=11&includes%5B%5D=pva");
            var response = await _httpUtility.GetContents(uri, AuthenticationToken, HardwareId, cancellationToken);
            return JsonSerializer.Deserialize<VideoSearchResponse>(response);
        }

        #endregion
    }
}
