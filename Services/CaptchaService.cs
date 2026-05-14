using System.Text.Json;

namespace ECourtTracker.API.Services
{
    public class CaptchaService : ICaptchaService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public CaptchaService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<bool> VerifyAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            var secretKey = _configuration["ReCaptcha:SecretKey"];

            // In test/dev mode with Google's public test key, always return true
            if (secretKey == "6LeIxAcTAAAAAGG-vFI1TnRWxMZNFuojJ4WifJWe")
                return true;

            try
            {
                var client = _httpClientFactory.CreateClient("ReCaptcha");
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("secret", secretKey!),
                    new KeyValuePair<string, string>("response", token)
                });

                var response = await client.PostAsync(
                    "https://www.google.com/recaptcha/api/siteverify", content);

                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("success").GetBoolean();
            }
            catch
            {
                return false;
            }
        }
    }
}
