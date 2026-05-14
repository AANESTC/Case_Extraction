namespace ECourtTracker.API.Services
{
    public interface ICaptchaService
    {
        Task<bool> VerifyAsync(string token);
    }
}
