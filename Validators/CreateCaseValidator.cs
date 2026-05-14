using ECourtTracker.API.DTOs;
using FluentValidation;

namespace ECourtTracker.API.Validators
{
    public class CreateCaseValidator : AbstractValidator<CreateCaseDto>
    {
        public CreateCaseValidator()
        {
            RuleFor(x => x.CNRNumber)
                .NotEmpty().WithMessage("CNR Number is required.")
                .Matches(@"^[A-Z]{4}\d{2}-\d{6}-\d{4}$")
                .WithMessage("CNR Number must follow the format: XXXX00-000000-0000 (e.g., MHAU01-001234-2026).");

            RuleFor(x => x.CaseTitle)
                .MaximumLength(300).WithMessage("Case title cannot exceed 300 characters.");

            RuleFor(x => x.CaseType)
                .NotEmpty().WithMessage("Case type is required.")
                .Must(t => new[] { "Civil", "Criminal", "Writ", "Appeal", "Family", "Consumer" }.Contains(t))
                .WithMessage("Invalid case type. Must be Civil, Criminal, Writ, Appeal, Family, or Consumer.");

            RuleFor(x => x.Stage)
                .Must(s => string.IsNullOrEmpty(s) || new[] { "Evidence", "Arguments", "Pending", "Judgment", "Hearing" }.Contains(s))
                .WithMessage("Invalid stage value.");

            RuleFor(x => x.Status)
                .Must(s => new[] { "Pending", "Completed", "Closed", "Dismissed" }.Contains(s))
                .WithMessage("Status must be Pending, Completed, Closed, or Dismissed.");

            RuleFor(x => x.CourtName)
                .NotEmpty().WithMessage("Court name is required.")
                .MaximumLength(200);

            RuleFor(x => x.Petitioner)
                .NotEmpty().WithMessage("Petitioner name is required.")
                .MaximumLength(200);

            RuleFor(x => x.Respondent)
                .NotEmpty().WithMessage("Respondent name is required.")
                .MaximumLength(200);

            RuleFor(x => x.RegistrationDate)
                .GreaterThanOrEqualTo(x => x.FilingDate)
                .When(x => x.FilingDate.HasValue && x.RegistrationDate.HasValue)
                .WithMessage("Registration date must be on or after the filing date.");

            RuleFor(x => x.Notes)
                .MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.");
        }
    }

    public class UpdateCaseValidator : AbstractValidator<UpdateCaseDto>
    {
        public UpdateCaseValidator()
        {
            // Reuse same rules via composition
            Include(new CreateCaseValidator());
        }
    }
}
