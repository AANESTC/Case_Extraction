using ECourtTracker.API.DTOs;
using ECourtTracker.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ECourtTracker.API.Controllers
{
    /// <summary>
    /// Live eCourts scraper endpoints.
    ///   GET  /api/ecourt/captcha  → starts session, returns base64 CAPTCHA image + sessionId
    ///   POST /api/ecourt/search   → submits CNR + captcha, returns scraped case details
    ///   GET  /api/ecourt/ping     → health check (verifies eCourts site is reachable)
    /// </summary>
    [ApiController]
    [Route("api/ecourt")]
    public class ECourtController : ControllerBase
    {
        private readonly IECourtScraperService _scraper;
        private readonly IPdfService _pdfService;
        private readonly ILogger<ECourtController> _logger;
        private readonly ICaseService _caseService;

        public ECourtController(
            IECourtScraperService scraper, 
            IPdfService pdfService, 
            ILogger<ECourtController> logger,
            ICaseService caseService)
        {
            _scraper = scraper;
            _pdfService = pdfService;
            _logger  = logger;
            _caseService = caseService;
        }

        // ── POST /api/ecourt/download-pdf ─────────────────────────────────────
        [HttpPost("download-pdf")]
        public IActionResult DownloadPdf([FromBody] ECourtCaseResultDto caseDetails)
        {
            try
            {
                _logger.LogInformation("POST /api/ecourt/download-pdf CNR={Cnr}", caseDetails.CnrNumber);
                var pdfBytes = _pdfService.GenerateCaseReport(caseDetails);
                
                var fileName = $"CaseReport_{caseDetails.CnrNumber ?? "Unknown"}_{DateTime.Now:yyyyMMdd}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Detailed PDF generation error for CNR={Cnr}: {Message}", caseDetails.CnrNumber, ex.Message);
                return StatusCode(500, new ECourtErrorDto { Error = $"PDF generation failed: {ex.Message}. Check server logs for details." });
            }
        }

        // ── GET /api/ecourt/captcha ───────────────────────────────────────────
        /// <summary>
        /// Creates a new scraping session. Returns:
        ///   sessionId          — must be passed back in the search request
        ///   captchaImageBase64 — PNG image as base64; display with data:image/png;base64,...
        /// </summary>
        [HttpGet("captcha")]
        public async Task<IActionResult> GetCaptcha()
        {
            try
            {
                _logger.LogInformation("GET /api/ecourt/captcha — new session requested");
                var result = await _scraper.GetCaptchaAsync();

                if (string.IsNullOrEmpty(result.CaptchaImageBase64))
                    return StatusCode(502, new ECourtErrorDto
                    {
                        Error = "CAPTCHA image was empty. The eCourts website may be temporarily unavailable."
                    });

                return Ok(result);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error reaching eCourts site.");
                return StatusCode(503, new ECourtErrorDto
                {
                    Error = $"Cannot reach the eCourts website: {ex.Message}. Please try again later."
                });
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, new ECourtErrorDto
                {
                    Error = "Request to eCourts timed out. The server may be slow. Please try again."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error loading CAPTCHA.");
                return StatusCode(500, new ECourtErrorDto
                {
                    Error = $"Unexpected error: {ex.Message}"
                });
            }
        }

        // ── POST /api/ecourt/search ───────────────────────────────────────────
        [HttpPost("search")]
        [Authorize]
        public async Task<IActionResult> Search([FromBody] ECourtSearchRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
                return BadRequest(new ECourtErrorDto { Error = "SessionId is required." });
            if (string.IsNullOrWhiteSpace(request.CnrNumber))
                return BadRequest(new ECourtErrorDto { Error = "CNR number is required." });
            if (string.IsNullOrWhiteSpace(request.CaptchaText))
                return BadRequest(new ECourtErrorDto { Error = "CAPTCHA text is required." });

            try
            {
                _logger.LogInformation("POST /api/ecourt/search CNR={Cnr}", request.CnrNumber);
                var caseDetails = await _scraper.SearchCaseAsync(request);

                // Save or update case to database
                await SaveOrUpdateCaseAsync(caseDetails);

                return Ok(caseDetails);
            }
            catch (InvalidOperationException ex) when (ex.Message == "INVALID_CAPTCHA")
            {
                return BadRequest(new ECourtErrorDto
                {
                    Error = "Invalid CAPTCHA. Please refresh the CAPTCHA and try again."
                });
            }
            catch (InvalidOperationException ex) when (ex.Message == "CASE_NOT_FOUND")
            {
                return NotFound(new ECourtErrorDto
                {
                    Error = $"No case found for CNR '{request.CnrNumber}'. Please verify the number."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ECourtErrorDto { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching case CNR={Cnr}", request.CnrNumber);
                return StatusCode(500, new ECourtErrorDto
                {
                    Error = $"Unexpected error: {ex.Message}"
                });
            }
        }

        // ── GET /api/ecourt/ping ──────────────────────────────────────────────
        /// <summary>Quick health-check: verifies the eCourts site is reachable.</summary>
        [HttpGet("ping")]
        public async Task<IActionResult> Ping()
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                var resp = await client.GetAsync("https://services.ecourts.gov.in/ecourtindia_v6/?p=home/index");
                return Ok(new { reachable = resp.IsSuccessStatusCode, status = (int)resp.StatusCode });
            }
            catch (Exception ex)
            {
                return Ok(new { reachable = false, error = ex.Message });
            }
        }

        // ── POST /api/ecourt/debug ────────────────────────────────────────────
        /// <summary>
        /// Debug: returns the RAW response from eCourts for a given CNR + CAPTCHA.
        /// Use this to diagnose what the server is actually returning.
        /// </summary>
        [HttpPost("debug")]
        public async Task<IActionResult> Debug([FromBody] ECourtSearchRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
                return BadRequest(new ECourtErrorDto { Error = "SessionId is required." });

            try
            {
                var caseDetails = await _scraper.SearchCaseAsync(request);
                return Ok(new { parsed = caseDetails });
            }
            catch (InvalidOperationException ex)
            {
                return Ok(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Ok(new { exception = ex.GetType().Name, message = ex.Message });
            }
        }

        // ── Helper Methods ────────────────────────────────────────────────────
        
        private Guid GetCurrentUserId()
        {
            var id = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : Guid.Empty;
        }

        private DateTime? ParseDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return null;
            if (DateTime.TryParse(dateStr, out var d)) 
                return DateTime.SpecifyKind(d, DateTimeKind.Utc);
            return null;
        }

        private async Task SaveOrUpdateCaseAsync(ECourtCaseResultDto caseDetails)
        {
            if (string.IsNullOrWhiteSpace(caseDetails.CnrNumber)) return;

            var userId = GetCurrentUserId();
            if (userId == Guid.Empty) return;

            var dto = new UpdateCaseDto
            {
                CNRNumber = caseDetails.CnrNumber,
                CaseTitle = caseDetails.CaseTitle ?? string.Empty,
                CaseType = caseDetails.CaseType ?? string.Empty,
                Stage = caseDetails.CaseStatus ?? string.Empty,
                Status = "Pending",
                CourtName = caseDetails.CourtEstablishment ?? string.Empty,
                CourtComplex = string.Empty,
                JudgeName = caseDetails.JudgeName ?? string.Empty,
                Petitioner = caseDetails.Petitioner ?? (caseDetails.Petitioners?.FirstOrDefault() ?? string.Empty),
                Respondent = caseDetails.Respondent ?? (caseDetails.Respondents?.FirstOrDefault() ?? string.Empty),
                PetitionerAdvocate = caseDetails.AdvocateDetails ?? string.Empty,
                RespondentAdvocate = string.Empty,
                FilingNumber = caseDetails.FilingNumber ?? string.Empty,
                FilingDate = ParseDate(caseDetails.FilingDate),
                RegistrationNumber = caseDetails.RegistrationNumber ?? string.Empty,
                RegistrationDate = ParseDate(caseDetails.RegistrationDate),
                NextHearingDate = ParseDate(caseDetails.NextHearingDate)
            };

            var existingCase = await _caseService.GetCaseByCnrAsync(dto.CNRNumber);
            if (existingCase != null)
            {
                await _caseService.UpdateCaseAsync(existingCase.Id, dto);
            }
            else
            {
                var createDto = new CreateCaseDto
                {
                    CNRNumber = dto.CNRNumber,
                    CaseTitle = dto.CaseTitle,
                    CaseType = dto.CaseType,
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
                    AssignedUserId = userId
                };
                await _caseService.CreateCaseAsync(createDto, userId);
            }
        }
    }
}
