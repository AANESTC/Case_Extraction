using ECourtTracker.API.DTOs;
using ECourtTracker.API.Services;
using Microsoft.AspNetCore.Mvc;

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
        private readonly ILogger<ECourtController> _logger;

        public ECourtController(IECourtScraperService scraper, ILogger<ECourtController> logger)
        {
            _scraper = scraper;
            _logger  = logger;
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
    }
}
