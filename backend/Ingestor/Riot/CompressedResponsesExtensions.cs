using System.Net;

namespace Ingestor.Riot;

/// <summary>
/// Makes an HTTP client ask for compressed responses and decompress them transparently.
/// </summary>
public static class CompressedResponsesExtensions
{
    /// <summary>
    /// Turns on automatic decompression on the client's primary handler, which is what makes
    /// .NET send <c>Accept-Encoding</c> at all (#1601).
    /// </summary>
    /// <remarks>
    /// Without it the Riot API answers with raw JSON: a match timeline is ~790 KB raw against
    /// ~68 KB gzipped, and the ingestor downloads one per ingested match. The existing default
    /// handler is adjusted in place rather than replaced, so nothing else about it changes.
    /// </remarks>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <returns>The same <paramref name="builder"/> so that calls can be chained.</returns>
    public static IHttpClientBuilder AcceptCompressedResponses(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler((handler, _) =>
        {
            switch (handler)
            {
                case HttpClientHandler clientHandler:
                    clientHandler.AutomaticDecompression = DecompressionMethods.All;
                    break;
                case SocketsHttpHandler socketsHandler:
                    socketsHandler.AutomaticDecompression = DecompressionMethods.All;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Cannot enable response decompression on a {handler.GetType().Name} primary handler.");
            }
        });
}
