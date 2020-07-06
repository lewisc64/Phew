using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace Phew
{
    public class Bridge
    {
        private const int MaxUpdatesPerSecond = 10;

        private const int MinTimeBetweenUpdates = 150;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string _discoveryUrl = $"https://discovery.meethue.com/";

        private string _ipAddress = null;

        private HttpClient httpClient = null;

        private readonly List<DateTime> _requestTimeBuffer = new List<DateTime>();

        public string Id { get; private set; }

        public string Username { get; private set; }

        public string ApplicationName { get; set; } = "unknown";

        public string IpAddress
        {
            get
            {
                if (_ipAddress == null)
                {
                    _ipAddress = GetBridges()[Id];
                }
                return _ipAddress;
            }
        }

        public bool Registered => Username != null;

        private HttpClient HttpClient
        {
            get
            {
                if (httpClient == null)
                {
                    var handler = new HttpClientHandler()
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    };
                    httpClient = new HttpClient(handler);
                    httpClient.BaseAddress = new Uri($"https://{IpAddress}/api");
                }
                return httpClient;
            }
        }

        public Bridge(string id, string username = null)
        {
            Id = id;
            Username = username;
        }

        public void RegisterIfNotRegistered(Action waitForButtonCallback = null)
        {
            if (Registered)
            {
                return;
            }

            var identity = new BsonDocument
            {
                { "devicetype", $"phew#csharp {ApplicationName}" },
            };

            BsonDocument responseData;
            while (true)
            {
                responseData = SendApiRequest(HttpMethod.Post, string.Empty, identity, detectError: false)
                    .AsBsonArray
                    .Cast<BsonDocument>()
                    .Single();
                if (responseData.Contains("error") && responseData["error"]["type"].AsInt32 == 101)
                {
                    waitForButtonCallback?.Invoke();
                    waitForButtonCallback = null;
                    Thread.Sleep(500);
                }
                else if (responseData.Contains("success"))
                {
                    break;
                }
                else
                {
                    throw new InvalidOperationException($"Unexpected response: {responseData.ToString()}");
                }
            }

            Username = responseData["success"]["username"].AsString;
        }

        public IEnumerable<Light> GetLights()
        {
            foreach (var lightKvp in SendApiRequest(HttpMethod.Get, $"api/{Username}/lights").AsBsonDocument)
            {
                var light = new Light(this, Convert.ToInt32(lightKvp.Name));
                light.SetFromDocument(lightKvp.Value.AsBsonDocument);
                yield return light;
            }
        }

        public BsonValue SendApiRequest(HttpMethod method, string path, BsonDocument data = null, bool detectError = true)
        {
            BsonValue parsed = null;
            WaitForRateLimit(() =>
            {
                var message = new HttpRequestMessage(method, path);
                if (data != null)
                {
                    message.Content = new StringContent(data.ToString(), Encoding.UTF8, "application/json");
                }
                var response = HttpClient.SendAsync(message).Result;
                parsed = ParseApiResponse(response);

                if (detectError && parsed.IsBsonArray && parsed.AsBsonArray.FirstOrDefault()?.AsBsonDocument.Contains("error") == true)
                {
                    throw new InvalidOperationException($"Failed to {method.ToString()} to '{path}': {parsed.ToString()}");
                }
            });
            return parsed;
        }

        public static BsonValue ParseApiResponse(HttpResponseMessage response)
        {
            return BsonSerializer.Deserialize<BsonValue>(response.Content.ReadAsStringAsync().Result);
        }

        public static Dictionary<string, string> GetBridges()
        {
            var client = new HttpClient();
            var result = ParseApiResponse(client.GetAsync(_discoveryUrl).Result);
            return result.AsBsonArray.Cast<BsonDocument>().ToDictionary(x => x["id"].AsString, x => x["internalipaddress"].AsString);
        }

        private void WaitForRateLimit(Action callback)
        {
            lock (_requestTimeBuffer)
            {
                if (_requestTimeBuffer.Count >= MaxUpdatesPerSecond)
                {
                    var sleepTime = new TimeSpan(0, 0, 1) - (DateTime.UtcNow - _requestTimeBuffer.First());
                    if (sleepTime.TotalSeconds > 0)
                    {
                        Thread.Sleep(sleepTime);
                    }
                }
                do
                {
                    _requestTimeBuffer.RemoveAll(x => (DateTime.UtcNow - x).TotalSeconds >= 1);
                } while (_requestTimeBuffer.Count >= MaxUpdatesPerSecond);

                if (_requestTimeBuffer.Any())
                {
                    var diff = (int)(DateTime.UtcNow - _requestTimeBuffer.Last()).TotalMilliseconds;
                    if (diff < MinTimeBetweenUpdates)
                    {
                        Thread.Sleep(MinTimeBetweenUpdates - diff);
                    }
                }

                callback();

                _requestTimeBuffer.Add(DateTime.UtcNow);
            }
        }
    }
}
