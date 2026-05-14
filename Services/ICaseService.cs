using ECourtTracker.API.DTOs;
using ECourtTracker.API.Entities;

namespace ECourtTracker.API.Services
{
    public interface ICaseService
    {
        Task<IEnumerable<CaseListDto>> GetAllCasesAsync();
        Task<IEnumerable<CaseListDto>> GetUserCasesAsync(Guid userId);
        Task<CaseResponseDto?> GetCaseByIdAsync(Guid id);
        Task<CaseResponseDto?> GetCaseByCnrAsync(string cnrNumber);
        Task<IEnumerable<UpcomingHearingDto>> GetUpcomingHearingsAsync(Guid? userId, string filter);
        Task<(CaseResponseDto? Result, string? Error)> CreateCaseAsync(CreateCaseDto dto, Guid adminId);
        Task<(CaseResponseDto? Result, string? Error)> UpdateCaseAsync(Guid id, UpdateCaseDto dto);
        Task<bool> DeleteCaseAsync(Guid id);
    }
}
