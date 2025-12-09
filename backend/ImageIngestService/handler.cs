using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using System.Net;
using System.Text.Json;
using System.Collections.Generic;
using System;
using ImageIngestLambda.Functions;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ImageIngestLambda
{
    public class Function
    {
        private readonly GetImageFunction _getImageFunction = new();
        private readonly PostImageFunction _postImageFunction = new();
        private static readonly Dictionary<string, string> CorsHeaders = new()
        {
            ["Access-Control-Allow-Origin"] = "*",
            ["Access-Control-Allow-Headers"] = "Content-Type,Authorization,X-Amz-Date,X-Api-Key,X-Amz-Security-Token",
            ["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS"
        };

        // The core method that AWS Lambda will call.
        public APIGatewayProxyResponse FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            if (request == null)
            {
                context?.Logger?.LogLine("Received null APIGatewayProxyRequest.");

                return AddCors(new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = JsonSerializer.Serialize(new { Error = "Request payload is required." }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                });
            }

            var httpMethod = request.HttpMethod ?? request.RequestContext?.HttpMethod;
            if (string.IsNullOrWhiteSpace(httpMethod))
            {
                context?.Logger?.LogLine("Missing HttpMethod on request.");

                return AddCors(new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = JsonSerializer.Serialize(new { Error = "HttpMethod is required on the request." }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                });
            }

            // Log the HTTP method for debugging
            context?.Logger?.LogLine($"Processing request with HTTP Method: {httpMethod}");

            if (string.Equals(httpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return AddCors(new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                });
            }

            APIGatewayProxyResponse response;

            // Check the HTTP method
            switch (httpMethod.ToUpperInvariant())
            {
                case "GET":
                    response = _getImageFunction.Handle(request, context);
                    break;

                case "POST":
                    response = _postImageFunction.Handle(request, context);
                    break;

                default:
                    // --- Unsupported Method ---
                    response = new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.MethodNotAllowed,
                        Body = JsonSerializer.Serialize(new { Error = $"Unsupported HTTP Method: {request.HttpMethod}" }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                    };
                    break;
            }

            return AddCors(response);
        }

        private static APIGatewayProxyResponse AddCors(APIGatewayProxyResponse response)
        {
            response.Headers ??= new Dictionary<string, string>();

            foreach (var header in CorsHeaders)
            {
                response.Headers[header.Key] = header.Value;
            }

            return response;
        }
    }
}
