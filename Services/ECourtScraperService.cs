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
        private readonly IOcrService _ocrService;
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private readonly SemaphoreSlim _browserLock = new(1, 1);
        private readonly ConcurrentDictionary<string, (IPage Page, IBrowserContext Context, DateTime CreatedAt)> _sessions = new();

        public ECourtScraperService(ILogger<ECourtScraperService> logger, IOcrService ocrService)
        {
            _logger = logger;
            _ocrService = ocrService;
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

                var captchaBase64   = await CaptureCaptchaAsync(page);
                var predictedText   = await _ocrService.PredictCaptchaAsync(captchaBase64);
                var sessionId       = Guid.NewGuid().ToString("N");
                _sessions[sessionId] = (page, context, DateTime.UtcNow);

                _logger.LogInformation(
                    "CAPTCHA captured. Session={Id} | OCR prediction='{Text}'",
                    sessionId, predictedText);

                return new ECourtCaptchaResponseDto
                {
                    SessionId            = sessionId,
                    CaptchaImageBase64   = captchaBase64,
                    PredictedCaptchaText = predictedText
                };
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

            r.CaseType            = Label(doc, "Case Type");
            r.CaseNumber          = Label(doc, "Case Number") ?? cnr;
            r.FilingNumber        = Label(doc, "Filing Number");
            r.FilingDate          = Label(doc, "Filing Date", "Date of Filing");
            r.RegistrationDate    = Label(doc, "Registration Date", "Date of Registration");
            r.RegistrationNumber  = Label(doc, "Registration Number", "Reg. Number", "Reg No.");
            r.FirstHearingDate    = Label(doc, "First Hearing Date", "First Hearing");
            r.NextHearingDate     = Label(doc, "Next Hearing Date", "Next Date", "Next Hearing");
            r.BusinessOnDate      = Label(doc, "Business on Date", "Business");
            r.DecisionDate        = Label(doc, "Decision Date", "Date of Decision", "Disposal Date", "Date of Disposal");
            r.CaseStatus          = Label(doc, "Case Status", "Status", "Stage of Case");
            r.CourtEstablishment  = Label(doc, "Court Establishment", "Court Name", "Court");
            r.CourtNumber         = Label(doc, "Court Number", "Court No.");
            r.JudgeName           = Label(doc, "Judge", "Before Judge", "Coram", "Presiding Officer");

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
            r.Processes      = ParseProcesses(doc);
            r.Acts           = ParseKeywordList(doc, "Act", "Section", "IPC", "CrPC", "u/s");
            r.Orders         = ParseKeywordList(doc, "Order", "Judgement", "Judgment", "Decree");

            return r;
        }

        private static void ParseParties(HtmlDocument doc, ECourtCaseResultDto r)
        {
            // ── Strategy 1: dedicated container elements (div/span/p with class or id) ──
            TryParsePartyContainers(doc, r);

            // ── Strategy 2: table with explicit petitioner / respondent column headers ──
            if (r.Petitioners.Count == 0 && r.Respondents.Count == 0)
                TryParsePartyTable(doc, r);

            // ── Strategy 3: sequential <td> scan (handles label + value in same row) ──
            if (r.Petitioners.Count == 0 && r.Respondents.Count == 0)
                TryParsePartyTdScan(doc, r);

            // ── Strategy 4: raw text regex fallback ──
            if (r.Petitioners.Count == 0 && r.Respondents.Count == 0)
                TryParsePartyTextFallback(doc, r);

            // ── Deduplicate and cap ──
            r.Petitioners        = r.Petitioners.Select(CleanName).Where(s => s.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
            r.Respondents        = r.Respondents.Select(CleanName).Where(s => s.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
            r.PetitionerAdvocates = r.PetitionerAdvocates.Select(CleanName).Where(s => s.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            r.RespondentAdvocates = r.RespondentAdvocates.Select(CleanName).Where(s => s.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        }

        private static string CleanName(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s{2,}", " ");

        // Strategy 1 — look for container elements whose id/class/text hint at party sections
        private static void TryParsePartyContainers(HtmlDocument doc, ECourtCaseResultDto r)
        {
            // eCourts v6 wraps parties in divs like: <div class="Petitioner_Advocate_table"> ... </div>
            // or <div id="pet_data"> etc.
            string[] petSelectors = {
                "//*[contains(@class,'pet') or contains(@id,'pet')]",
                "//*[contains(@class,'Petitioner') or contains(@id,'Petitioner')]",
                "//*[contains(@class,'petitioner')]",
            };
            string[] respSelectors = {
                "//*[contains(@class,'res') or contains(@id,'res')]",
                "//*[contains(@class,'Respondent') or contains(@id,'Respondent')]",
                "//*[contains(@class,'respondent')]",
            };

            foreach (var sel in petSelectors)
            {
                var nodes = doc.DocumentNode.SelectNodes(sel);
                if (nodes == null) continue;
                foreach (var node in nodes)
                {
                    var txt = node.InnerText.Trim();
                    if (txt.Length < 2 || txt.Length > 1000) continue;
                    var low = txt.ToLower();
                    if (low.Contains("advocate") || low.Contains("adv."))
                        AddSplitNames(txt, r.PetitionerAdvocates);
                    else
                        AddSplitNames(txt, r.Petitioners);
                }
                if (r.Petitioners.Count > 0) break;
            }
            foreach (var sel in respSelectors)
            {
                var nodes = doc.DocumentNode.SelectNodes(sel);
                if (nodes == null) continue;
                foreach (var node in nodes)
                {
                    var txt = node.InnerText.Trim();
                    if (txt.Length < 2 || txt.Length > 1000) continue;
                    var low = txt.ToLower();
                    if (low.Contains("advocate") || low.Contains("adv."))
                        AddSplitNames(txt, r.RespondentAdvocates);
                    else
                        AddSplitNames(txt, r.Respondents);
                }
                if (r.Respondents.Count > 0) break;
            }
        }

        // Strategy 2 — table whose first row has petitioner / respondent column headers
        private static void TryParsePartyTable(HtmlDocument doc, ECourtCaseResultDto r)
        {
            foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
            {
                var txt = table.InnerText.ToLower();
                if (!txt.Contains("petitioner") && !txt.Contains("complainant")) continue;

                var rows = table.SelectNodes(".//tr");
                if (rows == null || rows.Count < 2) continue;

                // Find column indices by scanning first-row headers
                var headerCells = rows[0].SelectNodes(".//th|.//td");
                if (headerCells == null) continue;

                int petCol = -1, respCol = -1, petAdvCol = -1, respAdvCol = -1;
                for (int c = 0; c < headerCells.Count; c++)
                {
                    var h = headerCells[c].InnerText.ToLower().Trim();
                    if (h.Contains("petitioner") || h.Contains("complainant") || h.Contains("applicant"))
                    { if (!h.Contains("advocate")) petCol = c; else petAdvCol = c; }
                    else if (h.Contains("respondent") || h.Contains("accused") || h.Contains("opposite"))
                    { if (!h.Contains("advocate")) respCol = c; else respAdvCol = c; }
                    else if (h.Contains("advocate") && petCol >= 0) petAdvCol = c;
                    else if (h.Contains("advocate") && respCol >= 0) respAdvCol = c;
                }

                if (petCol < 0 && respCol < 0) continue; // not a party table

                for (int i = 1; i < rows.Count; i++)
                {
                    var cells = rows[i].SelectNodes(".//td");
                    if (cells == null) continue;
                    if (petCol >= 0 && petCol < cells.Count)
                        AddSplitNames(HtmlEntity.DeEntitize(cells[petCol].InnerText.Trim()), r.Petitioners);
                    if (respCol >= 0 && respCol < cells.Count)
                        AddSplitNames(HtmlEntity.DeEntitize(cells[respCol].InnerText.Trim()), r.Respondents);
                    if (petAdvCol >= 0 && petAdvCol < cells.Count)
                        AddSplitNames(HtmlEntity.DeEntitize(cells[petAdvCol].InnerText.Trim()), r.PetitionerAdvocates);
                    if (respAdvCol >= 0 && respAdvCol < cells.Count)
                        AddSplitNames(HtmlEntity.DeEntitize(cells[respAdvCol].InnerText.Trim()), r.RespondentAdvocates);
                }
                if (r.Petitioners.Count > 0 || r.Respondents.Count > 0) break;
            }
        }

        // Strategy 3 — sequential <td> scan with smarter same-cell name extraction
        private static void TryParsePartyTdScan(HtmlDocument doc, ECourtCaseResultDto r)
        {
            var allTds = doc.DocumentNode.SelectNodes("//td") ?? Enumerable.Empty<HtmlNode>();
            bool inPetitioner = false, inRespondent = false;
            bool inPetAdv = false, inRespAdv = false;

            foreach (var td in allTds)
            {
                var raw = HtmlEntity.DeEntitize(td.InnerText.Trim());
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var low = raw.ToLower();

                // Detect label cells
                bool isPetLabel  = (low.Contains("petitioner") || low.Contains("complainant") || low.Contains("applicant"))
                                   && !low.Contains("respondent");
                bool isRespLabel = low.Contains("respondent") || low.Contains("opposite party") || low.Contains("accused");
                bool isAdvLabel  = low.Contains("advocate") || low.Contains("adv.");

                if (isPetLabel && isAdvLabel) { inPetAdv = true; inRespAdv = false; inPetitioner = false; inRespondent = false;
                    // Name may follow the colon in same cell
                    var after = ExtractAfterColon(raw); if (after.Length > 1) r.PetitionerAdvocates.Add(after); continue; }
                if (isRespLabel && isAdvLabel) { inRespAdv = true; inPetAdv = false; inPetitioner = false; inRespondent = false;
                    var after = ExtractAfterColon(raw); if (after.Length > 1) r.RespondentAdvocates.Add(after); continue; }
                if (isAdvLabel && inPetitioner) { inPetAdv = true; inPetitioner = false;
                    var after = ExtractAfterColon(raw); if (after.Length > 1) r.PetitionerAdvocates.Add(after); else AddSplitNames(raw, r.PetitionerAdvocates); continue; }
                if (isAdvLabel && inRespondent) { inRespAdv = true; inRespondent = false;
                    var after = ExtractAfterColon(raw); if (after.Length > 1) r.RespondentAdvocates.Add(after); else AddSplitNames(raw, r.RespondentAdvocates); continue; }

                if (isPetLabel) {
                    inPetitioner = true; inRespondent = false; inPetAdv = false; inRespAdv = false;
                    // Name may be embedded after the label: "Petitioner: JOHN DOE"
                    var after = ExtractAfterColon(raw); if (after.Length > 1) AddSplitNames(after, r.Petitioners); continue; }
                if (isRespLabel) {
                    inRespondent = true; inPetitioner = false; inPetAdv = false; inRespAdv = false;
                    var after = ExtractAfterColon(raw); if (after.Length > 1) AddSplitNames(after, r.Respondents); continue; }

                // Data cells — skip very long (full HTML chunks) or very short strings
                if (raw.Length < 2 || raw.Length > 300) continue;
                // Skip cells that look like headers / section titles
                if (low.StartsWith("sl") || low.StartsWith("s.no") || low.StartsWith("sr.") || low == "name") continue;
                // Skip obvious non-name content
                if (low.Contains("filing") || low.Contains("registration") || low.Contains("next") ||
                    low.Contains("hearing") || low.Contains("stage") || low.Contains("court")) continue;

                if (inPetitioner && !raw.StartsWith("Vs", StringComparison.OrdinalIgnoreCase))
                    AddSplitNames(raw, r.Petitioners);
                else if (inRespondent)
                    AddSplitNames(raw, r.Respondents);
                else if (inPetAdv)
                    AddSplitNames(raw, r.PetitionerAdvocates);
                else if (inRespAdv)
                    AddSplitNames(raw, r.RespondentAdvocates);
            }
        }

        // Strategy 4 — plain-text regex for "Petitioner(s) : NAME" patterns
        private static void TryParsePartyTextFallback(HtmlDocument doc, ECourtCaseResultDto r)
        {
            var fullText = doc.DocumentNode.InnerText;
            var petMatch = System.Text.RegularExpressions.Regex.Match(fullText,
                @"Petitioner[s]?\s*[:\-]\s*(.{3,200}?)(?=Respondent|Advocate|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            if (petMatch.Success) AddSplitNames(petMatch.Groups[1].Value.Trim(), r.Petitioners);

            var respMatch = System.Text.RegularExpressions.Regex.Match(fullText,
                @"Respondent[s]?\s*[:\-]\s*(.{3,200}?)(?=Petitioner|Advocate|Acts|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            if (respMatch.Success) AddSplitNames(respMatch.Groups[1].Value.Trim(), r.Respondents);

            var petAdvMatch = System.Text.RegularExpressions.Regex.Match(fullText,
                @"(?:Petitioner[^\n]*Advocate|Advocate for Petitioner)\s*[:\-]\s*(.{3,200}?)(?:\n|Respondent|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (petAdvMatch.Success) r.PetitionerAdvocates.Add(petAdvMatch.Groups[1].Value.Trim());

            var respAdvMatch = System.Text.RegularExpressions.Regex.Match(fullText,
                @"(?:Respondent[^\n]*Advocate|Advocate for Respondent)\s*[:\-]\s*(.{3,200}?)(?:\n|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (respAdvMatch.Success) r.RespondentAdvocates.Add(respAdvMatch.Groups[1].Value.Trim());
        }

        // Splits a block of text into individual names (handles numbered lists, newlines, semicolons)
        private static void AddSplitNames(string text, List<string> target)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            // Remove leading/trailing label words if present
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"^(Petitioner[s]?|Respondent[s]?|Complainant|Accused|Applicant|Opposite Party)\s*[:\-]?\s*",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            // Split on: numbered list items, newlines, pipes, semicolons
            var parts = System.Text.RegularExpressions.Regex.Split(text, @"\d+\s*[.)]\s*|\n|\r\n|\r|\|\s*|;\s*")
                .Select(s => s.Trim())
                .Where(s => s.Length > 1 && s.Length < 200
                    && !s.Equals("Vs", StringComparison.OrdinalIgnoreCase)
                    && !s.Equals("V/s", StringComparison.OrdinalIgnoreCase)
                    && !s.All(char.IsDigit));
            target.AddRange(parts);
        }

        // Extracts the portion after a colon or dash: "Petitioner: JOHN DOE" → "JOHN DOE"
        private static string ExtractAfterColon(string text)
        {
            var idx = text.IndexOfAny(new[] { ':', '-' });
            if (idx < 0 || idx >= text.Length - 1) return string.Empty;
            return text[(idx + 1)..].Trim();
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

        // Returns true when a string looks like a date (DD-MM-YYYY, DD/MM/YYYY, YYYY-MM-DD, etc.)
        private static bool LooksLikeDate(string v) =>
            System.Text.RegularExpressions.Regex.IsMatch(v.Trim(),
                @"^\d{1,2}[-/]\d{1,2}[-/]\d{2,4}$|^\d{4}[-/]\d{1,2}[-/]\d{1,2}$");

        private static List<ProcessDto> ParseProcesses(HtmlDocument doc)
        {
            var list = new List<ProcessDto>();
            foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
            {
                var tableTxt = table.InnerText.ToLower();
                if (!tableTxt.Contains("process") && !tableTxt.Contains("summon") && !tableTxt.Contains("warrant")) continue;

                var rows = table.SelectNodes(".//tr");
                if (rows == null || rows.Count < 2) continue;

                // Build header list from first row
                var hdrs = rows[0].SelectNodes(".//th|.//td")
                               ?.Select(h => h.InnerText.Trim().ToLower()).ToList()
                           ?? new List<string>();

                // Resolve column indices from headers with rich aliases for eCourts v6
                int idCol = -1, titleCol = -1, dateCol = -1;
                for (int c = 0; c < hdrs.Count; c++)
                {
                    var h = hdrs[c];
                    if ((h.Contains("process no") || h.Contains("process id") || h.Contains("proc no") ||
                         h.Contains("proc. no") || (h.Contains("no") && !h.Contains("date"))) && idCol < 0)
                        idCol = c;
                    else if (h.Contains("type") || h.Contains("title") || h.Contains("nature") || h.Contains("description"))
                        titleCol = c;
                    else if (h.Contains("date") || h.Contains("issue") || h.Contains("issued") ||
                             h.Contains("return") || h.Contains("dispatch"))
                        dateCol = c;
                }

                for (int i = 1; i < rows.Count; i++)
                {
                    var cells = rows[i].SelectNodes(".//td");
                    if (cells == null || cells.Count < 2) continue;

                    var dto = new ProcessDto();

                    if (idCol >= 0 || titleCol >= 0 || dateCol >= 0)
                    {
                        // Header-guided mapping
                        if (idCol    >= 0 && idCol    < cells.Count) dto.ProcessId = HtmlEntity.DeEntitize(cells[idCol].InnerText.Trim());
                        if (titleCol >= 0 && titleCol < cells.Count) dto.Title     = HtmlEntity.DeEntitize(cells[titleCol].InnerText.Trim());
                        if (dateCol  >= 0 && dateCol  < cells.Count) dto.Date      = HtmlEntity.DeEntitize(cells[dateCol].InnerText.Trim());
                    }
                    else
                    {
                        // Positional fallback — detect dates by value content, not just position
                        for (int j = 0; j < cells.Count; j++)
                        {
                            var v = HtmlEntity.DeEntitize(cells[j].InnerText.Trim());
                            if (string.IsNullOrWhiteSpace(v)) continue;

                            if (j == 0)
                            {
                                // First column is always the process identifier
                                dto.ProcessId = v;
                            }
                            else if (LooksLikeDate(v))
                            {
                                // Any value that looks like a date → Issued Date (prefer first date found)
                                if (dto.Date == null) dto.Date = v;
                            }
                            else
                            {
                                // Non-date text after col 0 → Process Type / Title
                                if (dto.Title == null) dto.Title = v;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(dto.ProcessId))
                        list.Add(dto);
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
