using ECourtTracker.API.Data;
using ECourtTracker.API.DTOs;
using ECourtTracker.API.Entities;
using ECourtTracker.API.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ECourtTracker.API.Services
{
    public class CaseService : ICaseService
    {
        private readonly ICaseRepository _caseRepo;
        private readonly ApplicationDbContext _context;

        public CaseService(ICaseRepository caseRepo, ApplicationDbContext context)
        {
            _caseRepo = caseRepo;
            _context = context;
        }

        public Task<IEnumerable<CaseListDto>> GetAllCasesAsync()
            => _caseRepo.GetAllAsync();

        public Task<IEnumerable<CaseListDto>> GetUserCasesAsync(Guid userId)
            => _caseRepo.GetByUserIdAsync(userId);

        public Task<CaseResponseDto?> GetCaseByIdAsync(Guid id)
            => _caseRepo.GetByIdAsync(id);

        public Task<CaseResponseDto?> GetCaseByCnrAsync(string cnrNumber)
            => _caseRepo.GetByCnrAsync(cnrNumber);

        public Task<IEnumerable<UpcomingHearingDto>> GetUpcomingHearingsAsync(Guid? userId, string filter)
            => _caseRepo.GetUpcomingHearingsAsync(userId, filter);

        public async Task<(CaseResponseDto? Result, string? Error)> CreateCaseAsync(CreateCaseDto dto, Guid adminId)
        {
            // Check for duplicate CNR
            if (await _caseRepo.CnrExistsAsync(dto.CNRNumber))
                return (null, $"A case with CNR Number '{dto.CNRNumber}' already exists.");

            // Resolve assigned user (use admin if no specific user chosen)
            Guid targetUserId = dto.AssignedUserId ?? adminId;
            var userExists = await _context.Users.AnyAsync(u => u.Id == targetUserId);
            if (!userExists)
                return (null, "Assigned user not found.");

            var caseEntity = new Case
            {
                CNRNumber = dto.CNRNumber,
                CaseTitle = dto.CaseTitle,
                CaseType = dto.CaseType,
                CaseNumber = dto.CaseNumber,
                Stage = dto.Stage,
                Status = dto.Status,
                CourtName = dto.CourtName,
                CourtComplex = dto.CourtComplex,
                JudgeName = dto.JudgeName,
                Petitioner = dto.Petitioner,
                Respondent = dto.Respondent,
                PetitionerAdvocate = dto.PetitionerAdvocate,
                RespondentAdvocate = dto.RespondentAdvocate,
                FilingNumber = dto.FilingNumber,
                FilingDate = dto.FilingDate,
                RegistrationNumber = dto.RegistrationNumber,
                RegistrationDate = dto.RegistrationDate,
                NextHearingDate = dto.NextHearingDate,
                Notes = dto.Notes,
                ScrapedDetailsJson = dto.ScrapedDetailsJson,
                UserId = targetUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Add initial hearing entry if NextHearingDate is provided
            if (dto.NextHearingDate.HasValue)
            {
                caseEntity.Hearings.Add(new Hearing
                {
                    HearingDate = dto.NextHearingDate.Value,
                    Purpose = "Initial Hearing",
                    Status = "Pending",
                    Notes = string.Empty
                });
            }

            var saved = await _caseRepo.AddAsync(caseEntity);
            var response = await _caseRepo.GetByIdAsync(saved.Id);
            return (response, null);
        }

        public async Task<(CaseResponseDto? Result, string? Error)> UpdateCaseAsync(Guid id, UpdateCaseDto dto)
        {
            var existing = await _context.Cases.FindAsync(id);
            if (existing == null)
                return (null, "Case not found.");

            // Check duplicate CNR (excluding self)
            if (await _caseRepo.CnrExistsAsync(dto.CNRNumber, id))
                return (null, $"Another case with CNR Number '{dto.CNRNumber}' already exists.");

            var updated = new Case
            {
                CNRNumber = dto.CNRNumber,
                CaseTitle = dto.CaseTitle,
                CaseType = dto.CaseType,
                CaseNumber = dto.CaseNumber,
                Stage = dto.Stage,
                Status = dto.Status,
                CourtName = dto.CourtName,
                CourtComplex = dto.CourtComplex,
                JudgeName = dto.JudgeName,
                Petitioner = dto.Petitioner,
                Respondent = dto.Respondent,
                PetitionerAdvocate = dto.PetitionerAdvocate,
                RespondentAdvocate = dto.RespondentAdvocate,
                FilingNumber = dto.FilingNumber,
                FilingDate = dto.FilingDate,
                RegistrationNumber = dto.RegistrationNumber,
                RegistrationDate = dto.RegistrationDate,
                NextHearingDate = dto.NextHearingDate,
                Notes = dto.Notes,
                ScrapedDetailsJson = dto.ScrapedDetailsJson
            };

            await _caseRepo.UpdateAsync(id, updated);
            var response = await _caseRepo.GetByIdAsync(id);
            return (response, null);
        }

        public Task<bool> DeleteCaseAsync(Guid id) => _caseRepo.DeleteAsync(id);
    }
}
