using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Tesseract;

namespace ECourtTracker.API.Services
{
    public class OcrService : IOcrService
    {
        private readonly ILogger<OcrService> _logger;
        private readonly string _tessDataPath;

        public OcrService(ILogger<OcrService> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _tessDataPath = Path.Combine(env.ContentRootPath, "tessdata");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task<string> PredictCaptchaAsync(string imageBase64)
        {
            if (string.IsNullOrWhiteSpace(imageBase64)) return string.Empty;

            try
            {
                var imageBytes    = Convert.FromBase64String(imageBase64);
                var preprocessed  = await PreprocessImageAsync(imageBytes);
                return RunOcr(preprocessed);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR prediction failed — returning empty string.");
                return string.Empty;
            }
        }

        // ── Preprocessing Pipeline ────────────────────────────────────────────

        private static async Task<byte[]> PreprocessImageAsync(byte[] rawBytes)
        {
            using var inputStream  = new MemoryStream(rawBytes);
            using var outputStream = new MemoryStream();

            using var image = await Image.LoadAsync<Rgba32>(inputStream);

            image.Mutate(ctx => ctx
                // 1. Convert to grayscale — removes color noise
                .Grayscale()

                // 2. Scale up 3× — Tesseract accuracy improves significantly at higher resolution
                .Resize(image.Width * 3, image.Height * 3, KnownResamplers.Lanczos3)

                // 3. Boost contrast before thresholding
                .Contrast(1.6f)

                // 4. First binarization pass — converts to strict black/white
                .BinaryThreshold(0.48f)

                // 5. Light blur to smooth single-pixel noise artifacts
                .GaussianBlur(0.4f)

                // 6. Second binarization pass — sharpens character edges after blur
                .BinaryThreshold(0.54f)
            );

            await image.SaveAsPngAsync(outputStream);
            return outputStream.ToArray();
        }

        // ── Tesseract OCR ─────────────────────────────────────────────────────

        private string RunOcr(byte[] processedImageBytes)
        {
            if (!Directory.Exists(_tessDataPath))
            {
                _logger.LogError(
                    "Tesseract tessdata directory not found at '{Path}'. " +
                    "Download eng.traineddata from https://github.com/tesseract-ocr/tessdata " +
                    "and place it in {Path}.", _tessDataPath, _tessDataPath);
                return string.Empty;
            }

            using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.LstmOnly);

            // Restrict recognition to alphanumeric characters only (eCourt CAPTCHA format)
            engine.SetVariable("tessedit_char_whitelist",
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");

            // Page segmentation mode 7 = treat image as a single text line
            engine.SetVariable("tessedit_pageseg_mode", "7");

            using var pix  = Pix.LoadFromMemory(processedImageBytes);
            using var page = engine.Process(pix, PageSegMode.SingleLine);

            var raw     = page.GetText().Trim();
            // Strip all non-alphanumeric characters (spaces, newlines, punctuation)
            var cleaned = new string(raw.Where(char.IsLetterOrDigit).ToArray());

            _logger.LogInformation(
                "OCR prediction: raw='{Raw}' cleaned='{Cleaned}' confidence={Conf:F2}",
                raw, cleaned, page.GetMeanConfidence());

            return cleaned;
        }
    }
}
