namespace ECourtTracker.API.DTOs
{
    // ── Captcha ──────────────────────────────────────────────────────────────
    public class ECourtCaptchaResponseDto
    {
        public string SessionId { get; set; } = string.Empty;
        public string CaptchaImageBase64 { get; set; } = string.Empty;
    }

    // ── Search Request ────────────────────────────────────────────────────────
    public class ECourtSearchRequestDto
    {
        public string SessionId { get; set; } = string.Empty;
        public string CnrNumber { get; set; } = string.Empty;
        public string CaptchaText { get; set; } = string.Empty;
    }

    // ── Case Result ───────────────────────────────────────────────────────────
    public class ECourtCaseResultDto
    {
        public string? CnrNumber        { get; set; }
        public string? CaseTitle        { get; set; }
        public string? CaseType         { get; set; }
        public string? CaseNumber       { get; set; }
        public string? FilingNumber     { get; set; }
        public string? FilingDate       { get; set; }
        public string? RegistrationDate { get; set; }
        public string? FirstHearingDate { get; set; }
        public string? NextHearingDate  { get; set; }
        public string? BusinessOnDate   { get; set; }
        public string? CaseStatus       { get; set; }
        public string? CourtEstablishment { get; set; }
        public string? CourtNumber      { get; set; }
        public string? JudgeName        { get; set; }

        // Parties
        public List<string> Petitioners     { get; set; } = new();
        public List<string> Respondents     { get; set; } = new();
        public List<string> PetitionerAdvocates { get; set; } = new();
        public List<string> RespondentAdvocates { get; set; } = new();

        // Legacy single-value fields (used for fallback display)
        public string? Petitioner       { get; set; }
        public string? Respondent       { get; set; }
        public string? AdvocateDetails  { get; set; }

        public List<HearingHistoryDto>     HearingHistory  { get; set; } = new();
        public List<CaseTransferDto>       CaseTransfers   { get; set; } = new();
        public List<IAStatusDto>           IAStatus        { get; set; } = new();
        public List<string>                Orders          { get; set; } = new();
        public List<string>                Acts            { get; set; } = new();
    }

    public class HearingHistoryDto
    {
        public string? Date           { get; set; }
        public string? Purpose        { get; set; }
        public string? Judge          { get; set; }
        public string? BusinessOnDate { get; set; }
        public string? NextHearingDate{ get; set; }
    }

    public class CaseTransferDto
    {
        public string? RegistrationNumber { get; set; }
        public string? TransferDate       { get; set; }
        public string? FromCourt          { get; set; }
        public string? ToCourt            { get; set; }
    }

    public class IAStatusDto
    {
        public string? IANumber    { get; set; }
        public string? PartyName   { get; set; }
        public string? FilingDate  { get; set; }
        public string? NextDate    { get; set; }
        public string? Status      { get; set; }
    }

    // ── Generic Error ─────────────────────────────────────────────────────────
    public class ECourtErrorDto
    {
        public string Error { get; set; } = string.Empty;
    }
}
