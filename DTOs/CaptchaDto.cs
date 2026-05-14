namespace ECourtTracker.API.DTOs
{
    public class CaptchaVerifyRequestDto
    {
        public string Token { get; set; } = string.Empty;
    }

    public class CaptchaVerifyResponseDto
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
