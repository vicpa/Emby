using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.HttpServer;
using MediaBrowser.Model.Services;

namespace Emby.Server.Implementations.Services
{
    public static class ResponseHelper
    {
        public static async Task WriteToResponse(IResponse response, IRequest request, object result, CancellationToken cancellationToken)
        {
            if (result == null)
            {
                if (response.StatusCode == (int)HttpStatusCode.OK)
                {
                    response.StatusCode = (int)HttpStatusCode.NoContent;
                }

                response.SetContentLength(0);
                return;
            }

            var httpResult = result as IHttpResult;
            if (httpResult != null)
            {
                httpResult.RequestContext = request;
                request.ResponseContentType = httpResult.ContentType ?? request.ResponseContentType;
            }

            var defaultContentType = request.ResponseContentType;

            if (httpResult != null)
            {
                if (httpResult.RequestContext == null)
                    httpResult.RequestContext = request;

                response.StatusCode = httpResult.Status;
                response.StatusDescription = httpResult.StatusCode.ToString();
                //if (string.IsNullOrEmpty(httpResult.ContentType))
                //{
                //    httpResult.ContentType = defaultContentType;
                //}
                //response.ContentType = httpResult.ContentType;

                if (httpResult.Cookies != null)
                {
                    var httpRes = response as IHttpResponse;
                    if (httpRes != null)
                    {
                        foreach (var cookie in httpResult.Cookies)
                        {
                            httpRes.SetCookie(cookie);
                        }
                    }
                }
            }

            var responseOptions = result as IHasHeaders;
            if (responseOptions != null)
            {
                foreach (var responseHeaders in responseOptions.Headers)
                {
                    if (string.Equals(responseHeaders.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        response.SetContentLength(long.Parse(responseHeaders.Value));
                        continue;
                    }

                    response.AddHeader(responseHeaders.Key, responseHeaders.Value);
                }
            }

            //ContentType='text/html' is the default for a HttpResponse
            //Do not override if another has been set
            if (response.ContentType == null || response.ContentType == "text/html")
            {
                response.ContentType = defaultContentType;
            }

            if (new HashSet<string> { "application/json", }.Contains(response.ContentType))
            {
                response.ContentType += "; charset=utf-8";
            }

            var asyncStreamWriter = result as IAsyncStreamWriter;
            if (asyncStreamWriter != null)
            {
                await asyncStreamWriter.WriteToAsync(response.OutputStream, cancellationToken).ConfigureAwait(false);
                return;
            }

            var streamWriter = result as IStreamWriter;
            if (streamWriter != null)
            {
                streamWriter.WriteTo(response.OutputStream);
                return;
            }

            var fileWriter = result as FileWriter;
            if (fileWriter != null)
            {
                await fileWriter.WriteToAsync(response, cancellationToken).ConfigureAwait(false);
                return;
            }

            var stream = result as Stream;
            if (stream != null)
            {
                using (stream)
                {
                    await stream.CopyToAsync(response.OutputStream).ConfigureAwait(false);
                    return;
                }
            }

            var bytes = result as byte[];
            if (bytes != null)
            {
                response.ContentType = "application/octet-stream";
                response.SetContentLength(bytes.Length);

                if (bytes.Length > 0)
                {
                    await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            var responseText = result as string;
            if (responseText != null)
            {
                bytes = Encoding.UTF8.GetBytes(responseText);
                response.SetContentLength(bytes.Length);
                if (bytes.Length > 0)
                {
                    await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            await WriteObject(request, result, response).ConfigureAwait(false);
        }

        public static async Task WriteObject(IRequest request, object result, IResponse response)
        {
            var contentType = request.ResponseContentType;
            var serializer = RequestHelper.GetResponseWriter(HttpListenerHost.Instance, contentType);

            using (var ms = new MemoryStream())
            {
                serializer(result, ms);

                ms.Position = 0;

                var contentLength = ms.Length;

                response.SetContentLength(contentLength);

                if (contentLength > 0)
                {
                    await ms.CopyToAsync(response.OutputStream).ConfigureAwait(false);
                }
            }

            //serializer(result, outputStream);
        }
    }
}
