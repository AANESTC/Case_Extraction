namespace ECourtTracker.API.Services
{
    public interface IOcrService
    {
        /// <summary>
        /// Accepts a PNG image as a base64 string.
        /// Returns the predicted CAPTCHA text, or empty string on failure.
        /// </summary>
        Task<string> PredictCaptchaAsync(string imageBase64);
    }
}
