using ECourtTracker.API.Data;
using ECourtTracker.API.DTOs;
using ECourtTracker.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECourtTracker.API.Repositories
{
    public class CaseRepository : ICaseRepository
    {
        private readonly ApplicationDbContext _context;

        public CaseRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─── Mappers ─────────────────────────────────────────────────────────

        private static CaseListDto ToListDto(Case c, string? userName = null) => new()
        {
            Id = c.Id,
            CNRNumber = c.CNRNumber,
            CaseTitle = c.CaseTitle,
            CaseType = c.CaseType,
            CaseNumber = c.CaseNumber,
            Stage = c.Stage,
            Status = c.Status,
            CourtName = c.CourtName,
            Petitioner = c.Petitioner,
            Respondent = c.Respondent,
            NextHearingDate = c.NextHearingDate,
            CreatedAt = c.CreatedAt,
            UserId = c.UserId,
            AssignedUserName = userName ?? c.User?.FullName ?? string.Empty
        };

        private static CaseResponseDto ToResponseDto(Case c) => new()
        {
            Id = c.Id,
            CNRNumber = c.CNRNumber,
            CaseTitle = c.CaseTitle,
            CaseNumber = c.CaseNumber,
            CaseType = c.CaseType,
            Stage = c.Stage,
            Status = c.Status,
            CourtName = c.CourtName,
            CourtComplex = c.CourtComplex,
            JudgeName = c.JudgeName,
            Petitioner = c.Petitioner,
            Respondent = c.Respondent,
            PetitionerAdvocate = c.PetitionerAdvocate,
            RespondentAdvocate = c.RespondentAdvocate,
            FilingNumber = c.FilingNumber,
            FilingDate = c.FilingDate,
            RegistrationNumber = c.RegistrationNumber,
            RegistrationDate = c.RegistrationDate,
            NextHearingDate = c.NextHearingDate,
            Notes = c.Notes,
            ScrapedDetailsJson = c.ScrapedDetailsJson,
            UserId = c.UserId,
            AssignedUserName = c.User?.FullName ?? string.Empty,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            Hearings = c.Hearings
                .OrderByDescending(h => h.HearingDate)
                .Select(h => new HearingDto
                {
                    Id = h.Id,
                    HearingDate = h.HearingDate,
                    Purpose = h.Purpose,
                    Status = h.Status,
                    Notes = h.Notes
                }).ToList()
        };

        // ─── Queries ─────────────────────────────────────────────────────────

        public async Task<IEnumerable<CaseListDto>> GetAllAsync()
        {
            return await _context.Cases
                .Include(c => c.User)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => ToListDto(c, c.User.FullName))
                .ToListAsync();
        }

        public async Task<IEnumerable<CaseListDto>> GetByUserIdAsync(Guid userId)
        {
            return await _context.Cases
                .Include(c => c.User)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => ToListDto(c, c.User.FullName))
                .ToListAsync();
        }

        public async Task<CaseResponseDto?> GetByIdAsync(Guid id)
        {
            var c = await _context.Cases
                .Include(c => c.User)
                .Include(c => c.Hearings)
                .FirstOrDefaultAsync(c => c.Id == id);

            return c == null ? null : ToResponseDto(c);
        }

        public async Task<CaseResponseDto?> GetByCnrAsync(string cnrNumber)
        {
            var c = await _context.Cases
                .Include(c => c.User)
                .Include(c => c.Hearings)
                .FirstOrDefaultAsync(c => c.CNRNumber == cnrNumber);

            return c == null ? null : ToResponseDto(c);
        }

        public async Task<IEnumerable<UpcomingHearingDto>> GetUpcomingHearingsAsync(Guid? userId, string filter)
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);
            var weekEnd = today.AddDays(7);

            var query = _context.Hearings
                .Include(h => h.Case)
                .AsQueryable();

            // Scope to user if provided
            if (userId.HasValue)
                query = query.Where(h => h.Case.UserId == userId.Value);

            query = filter.ToLower() switch
            {
                "today" => query.Where(h => h.HearingDate.Date == today),
                "tomorrow" => query.Where(h => h.HearingDate.Date == tomorrow),
                "week" => query.Where(h => h.HearingDate.Date >= today && h.HearingDate.Date <= weekEnd),
                "pending" => query.Where(h => h.Status == "Pending"),
                "completed" => query.Where(h => h.Status == "Completed"),
                _ => query.Where(h => h.HearingDate.Date >= today)
            };

            return await query
                .OrderBy(h => h.HearingDate)
                .Select(h => new UpcomingHearingDto
                {
                    HearingId = h.Id,
                    CaseId = h.CaseId,
                    CNRNumber = h.Case.CNRNumber,
                    CaseTitle = h.Case.CaseTitle,
                    CourtName = h.Case.CourtName,
                    HearingDate = h.HearingDate,
                    Purpose = h.Purpose,
                    Status = h.Status,
                    Notes = h.Notes
                })
                .ToListAsync();
        }

        public async Task<bool> CnrExistsAsync(string cnrNumber, Guid? excludeId = null)
        {
            var query = _context.Cases.Where(c => c.CNRNumber == cnrNumber);
            if (excludeId.HasValue)
                query = query.Where(c => c.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        // ─── Mutations ────────────────────────────────────────────────────────

        public async Task<Case> AddAsync(Case caseEntity)
        {
            _context.Cases.Add(caseEntity);
            await _context.SaveChangesAsync();
            return caseEntity;
        }

        public async Task<Case?> UpdateAsync(Guid id, Case updated)
        {
            var existing = await _context.Cases.FindAsync(id);
            if (existing == null) return null;

            existing.CNRNumber = updated.CNRNumber;
            existing.CaseTitle = updated.CaseTitle;
            existing.CaseNumber = updated.CaseNumber;
            existing.CaseType = updated.CaseType;
            existing.Stage = updated.Stage;
            existing.Status = updated.Status;
            existing.CourtName = updated.CourtName;
            existing.CourtComplex = updated.CourtComplex;
            existing.JudgeName = updated.JudgeName;
            existing.Petitioner = updated.Petitioner;
            existing.Respondent = updated.Respondent;
            existing.PetitionerAdvocate = updated.PetitionerAdvocate;
            existing.RespondentAdvocate = updated.RespondentAdvocate;
            existing.FilingNumber = updated.FilingNumber;
            existing.FilingDate = updated.FilingDate;
            existing.RegistrationNumber = updated.RegistrationNumber;
            existing.RegistrationDate = updated.RegistrationDate;
            existing.NextHearingDate = updated.NextHearingDate;
            existing.Notes = updated.Notes;
            existing.ScrapedDetailsJson = updated.ScrapedDetailsJson;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var c = await _context.Cases.FindAsync(id);
            if (c == null) return false;

            _context.Cases.Remove(c);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
