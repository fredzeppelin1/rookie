using System;
using System.Text;
using System.Text.Json.Serialization;

namespace AndroidSideloader.Models
{
    /// <summary>
    /// Configuration model for public mirror access
    /// Downloaded from vrp-public.json
    /// </summary>
    public class PublicConfig
    {
        [JsonPropertyName("baseUri")]
        public string BaseUri { get; set; }

        private string _password;

        [JsonPropertyName("password")]
        public string Password
        {
            get => _password;
            set => _password = Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }
}