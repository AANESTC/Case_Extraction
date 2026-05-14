using ECourtTracker.API.DTOs;
using ECourtTracker.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace ECourtTracker.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CaptchaController : ControllerBase
    {
        private readonly ICaptchaService _captchaService;

        public CaptchaController(ICaptchaService captchaService)
        {
            _captchaService = captchaService;
        }

        /// <summary>POST /api/captcha/verify — Verify a reCAPTCHA token</summary>
        [HttpPost("verify")]
        public async Task<IActionResult> Verify([FromBody] CaptchaVerifyRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Token))
                return BadRequest(new CaptchaVerifyResponseDto
                {
                    Success = false,
                    ErrorMessage = "Captcha token is required."
                });

            var isValid = await _captchaService.VerifyAsync(dto.Token);

            if (!isValid)
                return BadRequest(new CaptchaVerifyResponseDto
                {
                    Success = false,
                    ErrorMessage = "Captcha verification failed. Please try again."
                });

            return Ok(new CaptchaVerifyResponseDto { Success = true });
        }
    }
}
