using ECourtTracker.API.DTOs;
using ECourtTracker.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ECourtTracker.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CasesController : ControllerBase
    {
        private readonly ICaseService _caseService;

        public CasesController(ICaseService caseService)
        {
            _caseService = caseService;
        }

        private Guid GetCurrentUserId()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : Guid.Empty;
        }

        private string GetCurrentUserRole()
            => User.FindFirstValue(ClaimTypes.Role) ?? "User";

        // ─── Admin Endpoints ──────────────────────────────────────────────────

        /// <summary>GET /api/cases — Admin: all cases</summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllCases()
        {
            var cases = await _caseService.GetAllCasesAsync();
            return Ok(cases);
        }

        /// <summary>POST /api/cases — Admin: create a new case</summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateCase([FromBody] CreateCaseDto dto)
        {
            var adminId = GetCurrentUserId();
            var (result, error) = await _caseService.CreateCaseAsync(dto, adminId);

            if (error != null)
                return BadRequest(new { message = error });

            return CreatedAtAction(nameof(GetCaseById), new { id = result!.Id }, result);
        }

        /// <summary>PUT /api/cases/{id} — Admin: update case</summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateCase(Guid id, [FromBody] UpdateCaseDto dto)
        {
            var (result, error) = await _caseService.UpdateCaseAsync(id, dto);

            if (error == "Case not found.") return NotFound(new { message = error });
            if (error != null) return BadRequest(new { message = error });

            return Ok(result);
        }

        /// <summary>DELETE /api/cases/{id} — Admin: delete case</summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteCase(Guid id)
        {
            var deleted = await _caseService.DeleteCaseAsync(id);
            if (!deleted) return NotFound(new { message = "Case not found." });
            return NoContent();
        }

        // ─── Shared Endpoints ─────────────────────────────────────────────────

        /// <summary>GET /api/cases/{id} — Get case by ID (admin or case owner)</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetCaseById(Guid id)
        {
            var caseDto = await _caseService.GetCaseByIdAsync(id);
            if (caseDto == null) return NotFound(new { message = "Case not found." });

            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();

            // Non-admin can only view their own cases
            if (role != "Admin" && caseDto.UserId != userId)
                return Forbid();

            return Ok(caseDto);
        }

        /// <summary>GET /api/cases/cnr/{cnr} — Look up by CNR (authenticated)</summary>
        [HttpGet("cnr/{cnr}")]
        public async Task<IActionResult> GetByCnr(string cnr)
        {
            var caseDto = await _caseService.GetCaseByCnrAsync(cnr);
            if (caseDto == null)
                return NotFound(new { message = $"No case found with CNR number '{cnr}'." });

            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();

            if (role != "Admin" && caseDto.UserId != userId)
                return Forbid();

            return Ok(caseDto);
        }

        /// <summary>GET /api/cases/my — User: own cases</summary>
        [HttpGet("my")]
        public async Task<IActionResult> GetMyCases()
        {
            var userId = GetCurrentUserId();
            var cases = await _caseService.GetUserCasesAsync(userId);
            return Ok(cases);
        }

        /// <summary>GET /api/cases/hearings?filter=today|tomorrow|week|pending|completed</summary>
        [HttpGet("hearings")]
        public async Task<IActionResult> GetUpcomingHearings([FromQuery] string filter = "week")
        {
            var role = GetCurrentUserRole();
            Guid? userId = role == "Admin" ? null : GetCurrentUserId();

            var hearings = await _caseService.GetUpcomingHearingsAsync(userId, filter);
            return Ok(hearings);
        }
    }
}
