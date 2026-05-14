using ECourtTracker.API.DTOs;
using ECourtTracker.API.Entities;

namespace ECourtTracker.API.Repositories
{
    public interface ICaseRepository
    {
        Task<IEnumerable<CaseListDto>> GetAllAsync();
        Task<IEnumerable<CaseListDto>> GetByUserIdAsync(Guid userId);
        Task<CaseResponseDto?> GetByIdAsync(Guid id);
        Task<CaseResponseDto?> GetByCnrAsync(string cnrNumber);
        Task<IEnumerable<UpcomingHearingDto>> GetUpcomingHearingsAsync(Guid? userId, string filter);
        Task<Case> AddAsync(Case caseEntity);
        Task<Case?> UpdateAsync(Guid id, Case caseEntity);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> CnrExistsAsync(string cnrNumber, Guid? excludeId = null);
    }
}
