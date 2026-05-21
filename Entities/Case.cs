using System.ComponentModel.DataAnnotations;

namespace ECourtTracker.API.Entities
{
    public class Case
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        // Core identifiers
        public string CNRNumber { get; set; } = string.Empty;
        public string CaseTitle { get; set; } = string.Empty;
        public string CaseNumber { get; set; } = string.Empty;
        public string CaseType { get; set; } = string.Empty; // Civil, Criminal, Writ, Appeal, Family, Consumer

        // Stage & Status
        public string Stage { get; set; } = string.Empty;    // Evidence, Arguments, Pending, Judgment, Hearing
        public string Status { get; set; } = "Pending";      // Pending, Completed, Closed, Dismissed

        // Court details
        public string CourtName { get; set; } = string.Empty;
        public string CourtComplex { get; set; } = string.Empty;
        public string JudgeName { get; set; } = string.Empty;

        // Parties
        public string Petitioner { get; set; } = string.Empty;
        public string Respondent { get; set; } = string.Empty;
        public string PetitionerAdvocate { get; set; } = string.Empty;
        public string RespondentAdvocate { get; set; } = string.Empty;

        // Filing & registration
        public string FilingNumber { get; set; } = string.Empty;
        public DateTime? FilingDate { get; set; }
        public string RegistrationNumber { get; set; } = string.Empty;
        public DateTime? RegistrationDate { get; set; }

        // Scheduling
        public DateTime? NextHearingDate { get; set; }

        // Notes
        public string Notes { get; set; } = string.Empty;

        // Raw scraped JSON containing full case details from the eCourts portal
        public string ScrapedDetailsJson { get; set; } = string.Empty;

        // Ownership
        public Guid UserId { get; set; }
        public User User { get; set; } = null!;

        // Navigation
        public ICollection<Hearing> Hearings { get; set; } = new List<Hearing>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
