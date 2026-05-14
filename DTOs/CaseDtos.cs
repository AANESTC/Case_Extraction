namespace ECourtTracker.API.DTOs
{
    // ─── Create / Update ──────────────────────────────────────────────────────

    public class CreateCaseDto
    {
        public string CNRNumber { get; set; } = string.Empty;
        public string CaseTitle { get; set; } = string.Empty;
        public string CaseType { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";

        public string CourtName { get; set; } = string.Empty;
        public string CourtComplex { get; set; } = string.Empty;
        public string JudgeName { get; set; } = string.Empty;

        public string Petitioner { get; set; } = string.Empty;
        public string Respondent { get; set; } = string.Empty;
        public string PetitionerAdvocate { get; set; } = string.Empty;
        public string RespondentAdvocate { get; set; } = string.Empty;

        public string FilingNumber { get; set; } = string.Empty;
        public DateTime? FilingDate { get; set; }
        public string RegistrationNumber { get; set; } = string.Empty;
        public DateTime? RegistrationDate { get; set; }

        public DateTime? NextHearingDate { get; set; }

        public string Notes { get; set; } = string.Empty;

        /// <summary>UserId to assign this case to (admin picks the user).</summary>
        public Guid? AssignedUserId { get; set; }
    }

    public class UpdateCaseDto : CreateCaseDto
    {
        // Inherits all fields from CreateCaseDto
    }

    // ─── Response DTOs ────────────────────────────────────────────────────────

    public class HearingDto
    {
        public Guid Id { get; set; }
        public DateTime HearingDate { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class CaseResponseDto
    {
        public Guid Id { get; set; }
        public string CNRNumber { get; set; } = string.Empty;
        public string CaseTitle { get; set; } = string.Empty;
        public string CaseNumber { get; set; } = string.Empty;
        public string CaseType { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public string CourtName { get; set; } = string.Empty;
        public string CourtComplex { get; set; } = string.Empty;
        public string JudgeName { get; set; } = string.Empty;

        public string Petitioner { get; set; } = string.Empty;
        public string Respondent { get; set; } = string.Empty;
        public string PetitionerAdvocate { get; set; } = string.Empty;
        public string RespondentAdvocate { get; set; } = string.Empty;

        public string FilingNumber { get; set; } = string.Empty;
        public DateTime? FilingDate { get; set; }
        public string RegistrationNumber { get; set; } = string.Empty;
        public DateTime? RegistrationDate { get; set; }

        public DateTime? NextHearingDate { get; set; }
        public string Notes { get; set; } = string.Empty;

        public Guid UserId { get; set; }
        public string AssignedUserName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<HearingDto> Hearings { get; set; } = new();
    }

    public class CaseListDto
    {
        public Guid Id { get; set; }
        public string CNRNumber { get; set; } = string.Empty;
        public string CaseTitle { get; set; } = string.Empty;
        public string CaseType { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CourtName { get; set; } = string.Empty;
        public string Petitioner { get; set; } = string.Empty;
        public string Respondent { get; set; } = string.Empty;
        public DateTime? NextHearingDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid UserId { get; set; }
        public string AssignedUserName { get; set; } = string.Empty;
    }

    // ─── Hearing Upcoming ─────────────────────────────────────────────────────

    public class UpcomingHearingDto
    {
        public Guid HearingId { get; set; }
        public Guid CaseId { get; set; }
        public string CNRNumber { get; set; } = string.Empty;
        public string CaseTitle { get; set; } = string.Empty;
        public string CourtName { get; set; } = string.Empty;
        public DateTime HearingDate { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
