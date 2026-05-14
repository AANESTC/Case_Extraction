using System.Collections.Concurrent;
using ECourtTracker.API.DTOs;
using HtmlAgilityPack;
using Microsoft.Playwright;

namespace ECourtTracker.API.Services
{
    public class ECourtScraperService : IECourtScraperService, IAsyncDisposable
    {
        private const string ECourtUrl = "https://services.ecourts.gov.in/ecourtindia_v6/?p=home/index";

        private readonly ILogger<ECourtScraperService> _logger;
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private readonly SemaphoreSlim _browserLock = new(1, 1);
        private readonly ConcurrentDictionary<string, (IPage Page, IBrowserContext Context, DateTime CreatedAt)> _sessions = new();

        public ECourtScraperService(ILogger<ECourtScraperService> logger)
        {
            _logger = logger;
            _ = CleanupLoopAsync();
        }

        // ── Browser ───────────────────────────────────────────────────────────

        // ── Known browser paths (Windows) ──────────────────────────────────
        private static readonly string[] EdgePaths =
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        };
        private static readonly string[] ChromePaths =
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
        };

        private static string? FindBrowser() =>
            EdgePaths.Concat(ChromePaths).FirstOrDefault(File.Exists);

        private async Task<IBrowser> GetBrowserAsync()
        {
            await _browserLock.WaitAsync();
            try
            {
                if (_playwright == null) _playwright = await Playwright.CreateAsync();

                if (_browser == null || !_browser.IsConnected)
                {
                    var systemBrowser = FindBrowser();
                    var args = new[] { "--no-sandbox", "--disable-setuid-sandbox",
                                       "--disable-dev-shm-usage", "--disable-gpu" };

                    if (systemBrowser != null)
                    {
                        _logger.LogInformation("Using system browser: {Path}", systemBrowser);
                        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                        {
                            ExecutablePath = systemBrowser,
                            Headless       = true,
                            Args           = args
                        });
                    }
                    else
                    {
                        // Fallback: Playwright's own Chromium (needs 'playwright install chromium')
                        _logger.LogInformation("No system browser found — using Playwright Chromium.");
                        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                        {
                            Headless = true,
                            Args     = args
                        });
                    }
                    _logger.LogInformation("Browser launched successfully.");
                }
                return _browser;
            }
            finally { _browserLock.Release(); }
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task<ECourtCaptchaResponseDto> GetCaptchaAsync()
        {
            var browser = await GetBrowserAsync();
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                IgnoreHTTPSErrors = true
            });
            var page = await context.NewPageAsync();

            try
            {
                _logger.LogInformation("Loading eCourts home…");
                await page.GotoAsync(ECourtUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30_000 });

                await page.WaitForSelectorAsync("img#captcha_image, img[src*='captcha']",
                    new PageWaitForSelectorOptions { Timeout = 20_000 });

                var captchaBase64 = await CaptureCaptchaAsync(page);
                var sessionId = Guid.NewGuid().ToString("N");
                _sessions[sessionId] = (page, context, DateTime.UtcNow);

                _logger.LogInformation("CAPTCHA captured. Session={Id}", sessionId);
                return new ECourtCaptchaResponseDto { SessionId = sessionId, CaptchaImageBase64 = captchaBase64 };
            }
            catch
            {
                await page.CloseAsync();
                await context.CloseAsync();
                throw;
            }
        }

        public async Task<ECourtCaseResultDto> SearchCaseAsync(ECourtSearchRequestDto request)
        {
            if (!_sessions.TryRemove(request.SessionId, out var session))
                throw new InvalidOperationException("Session expired. Please refresh CAPTCHA.");

            var (page, context, _) = session;
            try
            {
                _logger.LogInformation("Submitting CNR={Cnr}", request.CnrNumber);

                // Fill CNR
                await FillAsync(page, request.CnrNumber,
                    "input#cino", "input[name='cino']", "input[placeholder*='CNR']", "input[name='cnr_number']");

                // Fill CAPTCHA
                await FillAsync(page, request.CaptchaText,
                    "input#fcaptcha_code", "input[name='captcha_code']",
                    "input[name='captcha']", "input[placeholder*='aptcha']");

                // Submit
                await ClickAsync(page,
                    "button[type='submit']", "input[type='submit']",
                    "button:has-text('Search')", "button:has-text('Get Data')");

                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 20_000 });
                await Task.Delay(1000);

                var html = await page.ContentAsync();
                _logger.LogInformation("Page HTML received ({Len} chars)", html.Length);
                return ParseCaseDetails(html, request.CnrNumber);
            }
            finally
            {
                try { await page.CloseAsync(); } catch { }
                try { await context.CloseAsync(); } catch { }
            }
        }

        // ── Playwright Helpers ────────────────────────────────────────────────

        private static async Task<string> CaptureCaptchaAsync(IPage page)
        {
            string[] selectors = { "img#captcha_image", "img[src*='captcha']", "img[id*='captcha']", "#captcha_div img" };
            foreach (var sel in selectors)
            {
                try
                {
                    var el = await page.QuerySelectorAsync(sel);
                    if (el != null)
                    {
                        var bytes = await el.ScreenshotAsync(new ElementHandleScreenshotOptions { Type = ScreenshotType.Png });
                        return Convert.ToBase64String(bytes);
                    }
                }
                catch { }
            }
            var pg = await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png, FullPage = false });
            return Convert.ToBase64String(pg);
        }

        private static async Task FillAsync(IPage page, string value, params string[] selectors)
        {
            foreach (var sel in selectors)
            {
                try
                {
                    var el = await page.QuerySelectorAsync(sel);
                    if (el != null) { await el.FillAsync(value); return; }
                }
                catch { }
            }
            throw new InvalidOperationException($"Input not found. Tried: {string.Join(", ", selectors)}");
        }

        private static async Task ClickAsync(IPage page, params string[] selectors)
        {
            foreach (var sel in selectors)
            {
                try
                {
                    var el = await page.QuerySelectorAsync(sel);
                    if (el != null) { await el.ClickAsync(); return; }
                }
                catch { }
            }
            throw new InvalidOperationException("Submit button not found.");
        }

        // ── Parsing ───────────────────────────────────────────────────────────

        private ECourtCaseResultDto ParseCaseDetails(string html, string cnr)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var text = doc.DocumentNode.InnerText;

            if (text.Contains("Invalid Captcha", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Wrong Captcha", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("captcha is wrong", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("INVALID_CAPTCHA");

            if (text.Contains("No record found", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CASE_NOT_FOUND");

            var r = new ECourtCaseResultDto { CnrNumber = cnr };

            r.CaseType          = Label(doc, "Case Type");
            r.CaseNumber        = Label(doc, "Case Number") ?? cnr;
            r.FilingNumber      = Label(doc, "Filing Number");
            r.FilingDate        = Label(doc, "Filing Date", "Date of Filing");
            r.RegistrationDate  = Label(doc, "Registration Date", "Date of Registration");
            r.FirstHearingDate  = Label(doc, "First Hearing Date", "First Hearing");
            r.NextHearingDate   = Label(doc, "Next Hearing Date", "Next Date", "Next Hearing");
            r.BusinessOnDate    = Label(doc, "Business on Date", "Business");
            r.CaseStatus        = Label(doc, "Case Status", "Status", "Stage of Case");
            r.CourtEstablishment= Label(doc, "Court Establishment", "Court Name", "Court");
            r.CourtNumber       = Label(doc, "Court Number", "Court No.");
            r.JudgeName         = Label(doc, "Judge", "Before Judge", "Coram", "Presiding Officer");

            // Parties
            ParseParties(doc, r);

            // Legacy single fields
            r.Petitioner     = r.Petitioners.FirstOrDefault();
            r.Respondent     = r.Respondents.FirstOrDefault();
            r.AdvocateDetails= r.PetitionerAdvocates.FirstOrDefault();

            r.CaseTitle = r.Petitioners.Count > 0 && r.Respondents.Count > 0
                ? $"{r.Petitioners[0]} vs {r.Respondents[0]}"
                : Label(doc, "Case Title") ?? cnr;

            r.HearingHistory = ParseHearingHistory(doc);
            r.CaseTransfers  = ParseCaseTransfers(doc);
            r.IAStatus       = ParseIAStatus(doc);
            r.Acts           = ParseKeywordList(doc, "Act", "Section", "IPC", "CrPC", "u/s");
            r.Orders         = ParseKeywordList(doc, "Order", "Judgement", "Judgment", "Decree");

            return r;
        }

        private static void ParseParties(HtmlDocument doc, ECourtCaseResultDto r)
        {
            // Look for petitioner/respondent sections
            var allTds = doc.DocumentNode.SelectNodes("//td") ?? Enumerable.Empty<HtmlNode>();

            bool inPetitioner = false, inRespondent = false;
            foreach (var td in allTds)
            {
                var txt = td.InnerText.Trim();
                var low = txt.ToLower();

                if (low.Contains("petitioner") && low.Length < 40) { inPetitioner = true; inRespondent = false; continue; }
                if (low.Contains("respondent") || low.Contains("opposite party") || low.Contains("accused"))
                { inRespondent = true; inPetitioner = false; continue; }
                if (low.Contains("advocate") && inPetitioner) { r.PetitionerAdvocates.Add(txt); continue; }
                if (low.Contains("advocate") && inRespondent) { r.RespondentAdvocates.Add(txt); continue; }

                if (inPetitioner && txt.Length > 2 && txt.Length < 200 && !txt.StartsWith("Vs", StringComparison.OrdinalIgnoreCase))
                {
                    // Split numbered lists like "1. Name\n2. Name"
                    var names = System.Text.RegularExpressions.Regex.Split(txt, @"\n|\r|\d+\.\s*")
                        .Select(s => s.Trim()).Where(s => s.Length > 1);
                    r.Petitioners.AddRange(names);
                }
                else if (inRespondent && txt.Length > 2 && txt.Length < 200)
                {
                    var names = System.Text.RegularExpressions.Regex.Split(txt, @"\n|\r|\d+\.\s*")
                        .Select(s => s.Trim()).Where(s => s.Length > 1);
                    r.Respondents.AddRange(names);
                }
            }

            // Deduplicate
            r.Petitioners = r.Petitioners.Distinct().Take(20).ToList();
            r.Respondents = r.Respondents.Distinct().Take(30).ToList();
        }

        private static List<CaseTransferDto> ParseCaseTransfers(HtmlDocument doc)
        {
            var list = new List<CaseTransferDto>();
            foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
            {
                var txt = table.InnerText.ToLower();
                if (!txt.Contains("transfer") && !txt.Contains("from court")) continue;
                var rows = table.SelectNodes(".//tr");
                if (rows == null || rows.Count < 2) continue;
                var hdrs = rows[0].SelectNodes(".//th|.//td")?.Select(h => h.InnerText.Trim().ToLower()).ToList() ?? new();
                for (int i = 1; i < rows.Count; i++)
                {
                    var cells = rows[i].SelectNodes(".//td");
                    if (cells == null || cells.Count < 2) continue;
                    var dto = new CaseTransferDto();
                    for (int j = 0; j < cells.Count; j++)
                    {
                        var v = HtmlEntity.DeEntitize(cells[j].InnerText.Trim());
                        var h = j < hdrs.Count ? hdrs[j] : "";
                        if (h.Contains("reg") || j == 0) dto.RegistrationNumber = v;
                        else if (h.Contains("date") || j == 1) dto.TransferDate = v;
                        else if (h.Contains("from") || j == 2) dto.FromCourt = v;
                        else if (h.Contains("to") || j == 3) dto.ToCourt = v;
                    }
                    if (!string.IsNullOrWhiteSpace(dto.RegistrationNumber)) list.Add(dto);
                }
                if (list.Count > 0) break;
            }
            return list;
        }

        private static List<IAStatusDto> ParseIAStatus(HtmlDocument doc)
        {
            var list = new List<IAStatusDto>();
            foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
            {
                var txt = table.InnerText.ToLower();
                if (!txt.Contains("ia") && !txt.Contains("interlocutory")) continue;
                var rows = table.SelectNodes(".//tr");
                if (rows == null || rows.Count < 2) continue;
                var hdrs = rows[0].SelectNodes(".//th|.//td")?.Select(h => h.InnerText.Trim().ToLower()).ToList() ?? new();
                for (int i = 1; i < rows.Count; i++)
                {
                    var cells = rows[i].SelectNodes(".//td");
                    if (cells == null || cells.Count < 2) continue;
                    var dto = new IAStatusDto();
                    for (int j = 0; j < cells.Count; j++)
                    {
                        var v = HtmlEntity.DeEntitize(cells[j].InnerText.Trim());
                        var h = j < hdrs.Count ? hdrs[j] : "";
                        if (h.Contains("ia") || j == 0) dto.IANumber = v;
                        else if (h.Contains("party") || j == 1) dto.PartyName = v;
                        else if ((h.Contains("fil") && h.Contains("date")) || j == 2) dto.FilingDate = v;
                        else if (h.Contains("next") || j == 3) dto.NextDate = v;
                        else if (h.Contains("status") || j == 4) dto.Status = v;
                    }
                    if (!string.IsNullOrWhiteSpace(dto.IANumber)) list.Add(dto);
                }
                if (list.Count > 0) break;
            }
            return list;
        }

        private static List<HearingHistoryDto> ParseHearingHistory(HtmlDocument doc)
        {
            var list = new List<HearingHistoryDto>();
            foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
            {
                var txt = table.InnerText.ToLower();
                if (!txt.Contains("hearing") && !txt.Contains("purpose") && !txt.Contains("business on")) continue;
                var rows = table.SelectNodes(".//tr");
                if (rows == null || rows.Count < 2) continue;
                var hdrs = rows[0].SelectNodes(".//th|.//td")?.Select(h => h.InnerText.Trim().ToLower()).ToList() ?? new();
                for (int i = 1; i < rows.Count; i++)
                {
                    var cells = rows[i].SelectNodes(".//td");
                    if (cells == null || cells.Count == 0) continue;
                    var dto = new HearingHistoryDto();
                    for (int j = 0; j < cells.Count; j++)
                    {
                        var v = HtmlEntity.DeEntitize(cells[j].InnerText.Trim());
                        var h = j < hdrs.Count ? hdrs[j] : "";
                        if ((h.Contains("date") && !h.Contains("next")) || j == 0) dto.Date = v;
                        else if (h.Contains("purpose") || h.Contains("business") || j == 1) dto.Purpose = v;
                        else if (h.Contains("judge") || j == 2) dto.Judge = v;
                        else if (h.Contains("next") || j == 3) dto.NextHearingDate = v;
                    }
                    if (!string.IsNullOrWhiteSpace(dto.Date)) list.Add(dto);
                }
                if (list.Count > 0) break;
            }
            return list;
        }

        private static string? Label(HtmlDocument doc, params string[] labels)
        {
            var nodes = doc.DocumentNode.SelectNodes("//td|//th|//label|//span|//b|//strong") ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in nodes)
            {
                var t = node.InnerText.Trim();
                foreach (var label in labels)
                {
                    if (!t.Equals(label, StringComparison.OrdinalIgnoreCase) &&
                        !t.Equals(label + ":", StringComparison.OrdinalIgnoreCase)) continue;
                    // Next sibling
                    var n = node.NextSibling;
                    while (n != null && n.NodeType != HtmlNodeType.Element) n = n.NextSibling;
                    if (n != null) { var v = HtmlEntity.DeEntitize(n.InnerText.Trim()); if (v.Length > 0 && v.Length < 500) return v; }
                    // Parent's next sibling
                    if (node.ParentNode != null)
                    {
                        var p = node.ParentNode.NextSibling;
                        while (p != null && p.NodeType != HtmlNodeType.Element) p = p.NextSibling;
                        if (p != null) { var v = HtmlEntity.DeEntitize(p.InnerText.Trim()); if (v.Length > 0 && v.Length < 500) return v; }
                    }
                }
            }
            return null;
        }

        private static List<string> ParseKeywordList(HtmlDocument doc, params string[] keywords)
        {
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in doc.DocumentNode.SelectNodes("//td|//li|//div") ?? Enumerable.Empty<HtmlNode>())
            {
                var t = node.InnerText.Trim();
                if (t.Length < 3 || t.Length > 500) continue;
                if (!keywords.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                items.Add(HtmlEntity.DeEntitize(t));
                if (items.Count >= 20) break;
            }
            return items.ToList();
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private async Task CleanupLoopAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(2));
                var cutoff = DateTime.UtcNow.AddMinutes(-10);
                foreach (var kv in _sessions.Where(s => s.Value.CreatedAt < cutoff).ToList())
                {
                    if (_sessions.TryRemove(kv.Key, out var s))
                    {
                        try { await s.Page.CloseAsync(); } catch { }
                        try { await s.Context.CloseAsync(); } catch { }
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var kv in _sessions)
            {
                try { await kv.Value.Page.CloseAsync(); } catch { }
                try { await kv.Value.Context.CloseAsync(); } catch { }
            }
            _sessions.Clear();
            if (_browser != null) await _browser.CloseAsync();
            _playwright?.Dispose();
        }
    }
}
