using ECourtTracker.API.DTOs;

namespace ECourtTracker.API.Services
{
    public interface IPdfService
    {
        byte[] GenerateCaseReport(ECourtCaseResultDto caseDetails);
    }
}
