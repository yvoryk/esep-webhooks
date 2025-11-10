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
        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public string FunctionHandler(object input, ILambdaContext context)
        {
            context.Logger.LogInformation($"FunctionHandler received: {input}");

            string jsonString = input.ToString();

            // Try to parse as API Gateway proxy event first
            dynamic eventData = JsonConvert.DeserializeObject<dynamic>(jsonString);
            string body = null;

            if (eventData.body != null)
            {
                // This is an API Gateway proxy event
                body = eventData.body;
                context.Logger.LogInformation($"Extracted body from API Gateway event: {body}");
            }
            else
            {
                // Direct payload
                body = jsonString;
            }

            dynamic json = JsonConvert.DeserializeObject<dynamic>(body);
            string payload = $"{{'text':'Issue Created: {json.issue.html_url}'}}";

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
