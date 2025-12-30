using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using DSD_Outbound.Models;
using Newtonsoft.Json;
using RestSharp;

namespace DSD_Outbound.Services
{
    public class ApiService
    {
        async Task<string> GetAPIDataAsync(string url, string accessToken, string tableName, Func<Task<TokenInfo>> refreshTokenFunc, CancellationToken cancellationToken = default)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(300);

            int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    Console.WriteLine($"Attempt {attempt} to call API for {tableName}");

                    var response = await client.GetAsync(url, cancellationToken);

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        Console.WriteLine("Access token expired. Refreshing...");
                        var newTokenInfo = await GetATokenAsync(); // Call your GetATokenAsync
                        accessToken = newTokenInfo.access_token;      // Update token
                        continue; // Retry immediately with new token
                    }

                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Request timed out. Retrying...");
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"HTTP error: {ex.Message}");
                    break; // Stop retrying for non-timeout errors
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken); // Exponential backoff
            }

            return null; // Indicate failure
        }
        async Task<TokenInfo> GetATokenAsync()
        {
            var client = new RestClient();
            //var request = new RestRequest(accessInfo.Url, Method.Post);

            //request.AlwaysMultipartFormData = true;
            //request.AddParameter("grant_type", accessInfo.Grant_Type);
            //request.AddParameter("client_id", accessInfo.Client_ID);
            //request.AddParameter("client_secret", accessInfo.Client_Secret);
            //request.AddParameter("scope", accessInfo.Scope);

            //var response = await client.ExecuteAsync(request);
            //if (!response.IsSuccessful)
            //{
            //    throw new Exception($"Token request failed: {response.StatusCode} - {response.Content}");
            //}

            //return JsonConvert.DeserializeObject<TokenInfo>(response.Content);
            return null;
        }
    }

}
