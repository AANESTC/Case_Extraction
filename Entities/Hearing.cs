using System.ComponentModel.DataAnnotations;

namespace ECourtTracker.API.Entities
{
    public class Hearing
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CaseId { get; set; }
        public Case Case { get; set; } = null!;
        public DateTime HearingDate { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Pending, Completed
        public string Notes { get; set; } = string.Empty;
    }
}
