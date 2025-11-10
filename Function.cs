using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Amazon.Lambda.Core;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook
{
    public class Function
    {
        private static readonly HashSet<string> _processedDeliveries = new HashSet<string>();

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public object FunctionHandler(object input, ILambdaContext context)
        {
            context.Logger.LogInformation($"FunctionHandler received: {input}");

            string jsonString = input.ToString();

            // Try to parse as API Gateway proxy event first
            dynamic eventData = JsonConvert.DeserializeObject<dynamic>(jsonString);
            string body = null;
            string deliveryId = null;

            if (eventData.body != null)
            {
                // This is an API Gateway proxy event
                body = eventData.body;
                // Extract GitHub delivery ID for deduplication
                deliveryId = eventData.headers?["X-GitHub-Delivery"];
                context.Logger.LogInformation($"Extracted body from API Gateway event: {body}");
            }
            else
            {
                // Direct payload
                body = jsonString;
            }

            // Check for duplicate deliveries
            if (!string.IsNullOrEmpty(deliveryId))
            {
                lock (_processedDeliveries)
                {
                    if (_processedDeliveries.Contains(deliveryId))
                    {
                        context.Logger.LogInformation($"Duplicate delivery ignored: {deliveryId}");
                        return new
                        {
                            statusCode = 200,
                            body = "Duplicate delivery ignored",
                            headers = new Dictionary<string, string>
                            {
                                { "Content-Type", "text/plain" }
                            }
                        };
                    }
                    _processedDeliveries.Add(deliveryId);

                    // Keep only last 1000 deliveries to prevent memory leak
                    if (_processedDeliveries.Count > 1000)
                    {
                        // Remove oldest entry (HashSet doesn't guarantee order, so we'll clear and rebuild periodically)
                        _processedDeliveries.Clear();
                    }
                }
            }

            dynamic json = JsonConvert.DeserializeObject<dynamic>(body);

            // Check if issue exists in the payload
            if (json?.issue?.html_url == null)
            {
                context.Logger.LogWarning("Payload does not contain issue.html_url");
                return new
                {
                    statusCode = 400,
                    body = "Bad Request: Payload must contain issue.html_url",
                    headers = new Dictionary<string, string>
                    {
                        { "Content-Type", "text/plain" }
                    }
                };
            }

            string issueUrl = json.issue.html_url;
            string payload = $"{{\"text\":\"Issue Created: {issueUrl}\"}}";

            var client = new HttpClient();
            var webRequest = new HttpRequestMessage(HttpMethod.Post, Environment.GetEnvironmentVariable("SLACK_URL"))
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            var response = client.Send(webRequest);
            using var reader = new StreamReader(response.Content.ReadAsStream());
            var slackResponse = reader.ReadToEnd();

            // For API Gateway proxy integration, return proper response format
            return new
            {
                statusCode = 200,
                body = slackResponse,
                headers = new Dictionary<string, string>
                {
                    { "Content-Type", "text/plain" }
                }
            };
        }
    }
}
