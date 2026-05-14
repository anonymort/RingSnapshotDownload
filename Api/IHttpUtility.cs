using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KoenZomers.Ring.Api
{
    /// <summary>
    /// Abstraction for HTTP utility calls to enable cancellation and testability.
    /// </summary>
    public interface IHttpUtility
    {
        Task<string> GetContents(Uri url, string bearerToken = null, string hardwareId = null, CancellationToken cancellationToken = default);

        Task<string> PostJson(Uri url, Dictionary<string, string> formFields, NameValueCollection headerFields, CancellationToken cancellationToken = default);

        Task<Stream> DownloadFile(Uri url, string bearerToken = null, string hardwareId = null, CancellationToken cancellationToken = default);

        Task DownloadFileToPath(Uri url, string saveAs, string bearerToken = null, string hardwareId = null, CancellationToken cancellationToken = default);

        Task SendRequestWithExpectedStatusOutcome(Uri url, HttpMethod httpMethod, HttpStatusCode? expectedStatusCode, string bodyContent = null, string bearerToken = null, string hardwareId = null, CancellationToken cancellationToken = default);

        Task<T> SendRequest<T>(Uri url, HttpMethod httpMethod, string bodyContent, string bearerToken = null, string hardwareId = null, CancellationToken cancellationToken = default);

        Task<string> SendRequest(Uri url, HttpMethod httpMethod, string bodyContent, string bearerToken = null, string hardwareId = null, CancellationToken cancellationToken = default);
    }
}
