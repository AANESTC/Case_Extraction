using ECourtTracker.API.DTOs;

namespace ECourtTracker.API.Services
{
    public interface IECourtScraperService : IAsyncDisposable
    {
        Task<ECourtCaptchaResponseDto> GetCaptchaAsync();
        Task<ECourtCaseResultDto> SearchCaseAsync(ECourtSearchRequestDto request);
    }
}
