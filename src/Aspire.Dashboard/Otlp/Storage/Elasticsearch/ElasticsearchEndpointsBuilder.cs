// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Api;
using Aspire.Dashboard.Utils;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

internal static class ElasticsearchEndpointsBuilder
{
    public static void MapElasticsearchApi(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        if (!ElasticsearchConfigNames.IsEnabled(configuration))
        {
            endpoints.MapPostNotFound("/api/elasticsearch/{*path}").SkipStatusCodePages();
            return;
        }

        endpoints.MapPost("/api/elasticsearch/setup", async (
            ElasticsearchDataStreamSetup setup,
            ILogger<ElasticsearchDataStreamSetup> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await setup.EnsureDataStreamAsync(cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Manual Elasticsearch setup failed.");

                return Results.Problem(
                    title: "Elasticsearch setup failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization(ApiAuthenticationHandler.PolicyName)
        .SkipStatusCodePages();
    }
}
