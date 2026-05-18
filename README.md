# 🕷️ Web Scraper Module — `web_scrap`

> **Part of the [E-Court Tracker](../Ecourt%20Tracker/README.md) platform.**  
> A Playwright-powered live scraper that fetches real case data directly from the official eCourts portal ([services.ecourts.gov.in](https://services.ecourts.gov.in)).

---

## 📌 Overview

The `web_scrap` module provides a **Playwright-based backend scraping service** that:

1. Navigates to the official eCourts website inside a headless Chromium browser.
2. Captures the site's **image CAPTCHA** and sends it to the frontend as a Base64 PNG.
3. Accepts the user-typed CAPTCHA text along with a **CNR number**, submits the form, and scrapes the full case details from the resulting HTML.
4. Returns richly structured case data via REST API endpoints.

This is an **additive, standalone module** — it does not replace the existing database-backed case lookup; it provides a **live court data** alternative.

---

## 🏗️ Architecture

```
User (Browser)
    │
    │  1. GET /api/ecourt/captcha
    ▼
React Frontend ──────────────────────────────► ASP.NET Core Backend
    │                                                │
    │  ◄── { sessionId, captchaImageBase64 }         │
    │                                                │  Playwright opens ecourts.gov.in
    │  2. User types CAPTCHA + CNR                   │  Captures CAPTCHA screenshot
    │                                                │  Stores IPage in ConcurrentDictionary
    │  3. POST /api/ecourt/search                    │
    └────────────────────────────────────────────►   │
                                                     │  Reuses stored session
                                                     │  Fills form, submits, scrapes HTML
                                                     │  HtmlAgilityPack parses result
    ◄── ECourtCaseResultDto ─────────────────────────┘
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| **Singleton service** | Browser sessions must survive across multiple HTTP requests (captcha → search). |
| **Isolated browser contexts** | Each user gets their own cookies/storage, preventing session bleed. |
| **`ConcurrentDictionary` for sessions** | Thread-safe storage for concurrent users. |
| **Automatic session cleanup** | A background loop disposes sessions older than 10 minutes to prevent memory leaks. |
| **Multi-selector fallbacks** | eCourts HTML is inconsistent; every DOM interaction tries multiple CSS selectors. |
| **HtmlAgilityPack parsing** | Robust HTML parsing that handles malformed markup from the eCourts portal. |

---

## 📁 Module Files

```
web_scrap/
├── ECourtScraperService.cs        ← Core Playwright scraping service
├── implementation_plan.md.resolved ← Original architecture plan & decisions
└── README.md                      ← This file
```

### Related files in the main project

```
ECourtTracker.API/
├── Services/
│   ├── ECourtScraperService.cs    ← (deployed here from this module)
│   └── IECourtScraperService.cs   ← DI interface
├── Controllers/
│   └── ECourtController.cs        ← REST endpoints
└── DTOs/
    └── ECourtDtos.cs              ← Request/Response DTOs

ecourt-tracker-ui/src/
├── pages/user/ECourtSearch.tsx    ← Live search UI page
└── services/ecourt.service.ts     ← Axios API calls
```

---

## ⚙️ Prerequisites & Setup

### 1. Install NuGet Packages

```bash
cd "Ecourt Tracker/ECourtTracker.API"
dotnet add package Microsoft.Playwright
dotnet add package HtmlAgilityPack
dotnet restore
```

### 2. Install Playwright Browsers

> ⚠️ **This is required on every machine / server that runs the backend.**

```bash
# After building the project
npx playwright install chromium

# Or using the Playwright .NET CLI tool
pwsh "Ecourt Tracker/ECourtTracker.API/bin/Debug/net10.0/playwright.ps1" install chromium
```

### 3. Register the Service (Program.cs)

```csharp
// Register as Singleton — browser sessions must outlive HTTP requests
builder.Services.AddSingleton<IECourtScraperService, ECourtScraperService>();
```

### 4. Run the Full Stack

```bash
# Terminal 1 — Backend
cd "Ecourt Tracker/ECourtTracker.API"
dotnet run

# Terminal 2 — Frontend
cd "Ecourt Tracker/ecourt-tracker-ui"
npm install
npm run dev
```

Or use the root-level batch files:

```bat
run-all.bat          ← starts both backend and frontend
run-backend.bat      ← backend only
run-frontend.bat     ← frontend only
```

---

## 🌐 REST API Endpoints

### `GET /api/ecourt/captcha`

Launches a headless Chromium session, navigates to the eCourts portal, and captures the image CAPTCHA.

**Response:**
```json
{
  "sessionId": "a1b2c3d4e5f6...",
  "captchaImageBase64": "iVBORw0KGgoAAAANSUhEUgAA..."
}
```

| Field | Description |
|---|---|
| `sessionId` | GUID token — **must be included** in the subsequent search request. |
| `captchaImageBase64` | PNG image encoded as Base64. Display with `<img src="data:image/png;base64,{value}">`. |

---

### `POST /api/ecourt/search`

Submits the CNR number and user-typed CAPTCHA to the eCourts website and returns parsed case data.

**Request body:**
```json
{
  "sessionId": "a1b2c3d4e5f6...",
  "cnrNumber": "MHAU010012342023",
  "captchaText": "AB3X9"
}
```

**Response — `ECourtCaseResultDto`:**
```json
{
  "caseTitle": "John Doe vs State of Maharashtra",
  "caseType": "Criminal Appeal",
  "caseNumber": "MHAU010012342023",
  "filingNumber": "12345/2023",
  "filingDate": "12-01-2023",
  "petitioner": "John Doe",
  "respondent": "State of Maharashtra",
  "advocateDetails": "Adv. A. Sharma",
  "judgeName": "Hon'ble Justice XYZ",
  "courtNumber": "Court No. 3",
  "caseStatus": "Pending",
  "nextHearingDate": "25-06-2026",
  "businessOnDate": "Motion Hearing",
  "hearingHistory": [
    {
      "date": "10-03-2026",
      "purpose": "Evidence",
      "judge": "Justice XYZ",
      "nextHearingDate": "25-06-2026"
    }
  ],
  "orders": ["Interim stay granted on 10-03-2026"],
  "acts": ["IPC Section 302", "CrPC Section 374"]
}
```

**Error responses:**

| HTTP Code | Error Message | Meaning |
|---|---|---|
| `400` | `INVALID_CAPTCHA` | User typed the CAPTCHA incorrectly. |
| `404` | `CASE_NOT_FOUND` | No case exists for the given CNR. |
| `400` | Session expired | `sessionId` is older than 10 minutes or already used. |

---

## 🔍 How the Scraper Works

### Step 1 — CAPTCHA Capture (`GetCaptchaAsync`)

```
Browser launch (Headless Chromium)
    └─► Navigate to https://services.ecourts.gov.in/ecourtindia_v6/?p=home/index
        └─► Wait for CAPTCHA image selector:
            img#captcha_image | img[src*='captcha'] | img[id*='captcha']
            └─► Screenshot the element → Base64 PNG
                └─► Store (IPage, IBrowserContext) in ConcurrentDictionary[sessionId]
```

### Step 2 — Form Submission (`SearchCaseAsync`)

```
Retrieve stored session by sessionId
    └─► Fill CNR field:
        input#cino | input[name='cino'] | input[placeholder*='CNR']
        └─► Fill CAPTCHA field:
            input#fcaptcha_code | input[name='captcha_code'] | input[placeholder*='captcha']
            └─► Click submit:
                button[type='submit'] | button:has-text('Search') | input[type='submit']
                └─► Wait for NetworkIdle + 1s buffer
                    └─► page.ContentAsync() → HtmlAgilityPack parsing
                        └─► Return ECourtCaseResultDto
```

### Step 3 — HTML Parsing (`ParseCaseDetails`)

The parser uses **three layered strategies** to extract field values:

1. **`<td>Label</td><td>Value</td>`** — standard table layout
2. **`<th>Label</th><td>Value</td>`** — header-based table layout  
3. **`<span>Label</span><span>Value</span>`** — span/div-based layout

Hearing history is parsed from any `<table>` containing `hearing`, `date`, or `purpose` keywords.

---

## 🔒 Session Lifecycle

```
Session created (captcha)  ──►  Used once (search)  ──►  Disposed
      │                                                        ▲
      │                                                        │
      └─────────── OR ──── 10 min timeout ────────────────────┘
                           (background cleanup loop)
```

- Sessions are **single-use** — once consumed by a search, they are removed.
- A background task runs every **2 minutes** and disposes any session older than **10 minutes**.
- All `IPage` and `IBrowserContext` objects are properly closed to prevent memory leaks.
- The Chromium browser instance is **shared** (singleton) but each session has its own isolated context.

---

## 🧪 Manual Testing Guide

1. Start the backend (`dotnet run`).
2. Navigate to `http://localhost:5173/user/ecourt-search`.
3. Click **"Load CAPTCHA"** — a CAPTCHA image should appear.
4. Enter a valid CNR number (e.g., `MHAU010012342023`).
5. Type the CAPTCHA characters shown in the image.
6. Click **"Search"** — case details should render in the page.
7. If CAPTCHA is wrong, click **"Refresh CAPTCHA"** to get a new one.

### Test with curl / Postman

```bash
# Step 1: Get CAPTCHA
curl -X GET http://localhost:5000/api/ecourt/captcha

# Step 2: Submit search (replace values from step 1 response)
curl -X POST http://localhost:5000/api/ecourt/search \
  -H "Content-Type: application/json" \
  -d '{
    "sessionId": "<sessionId from step 1>",
    "cnrNumber": "MHAU010012342023",
    "captchaText": "AB3X9"
  }'
```

---

## 🐞 Troubleshooting

| Problem | Solution |
|---|---|
| `Playwright.CreateAsync()` fails | Run `npx playwright install chromium` and ensure .NET build output is accessible. |
| CAPTCHA image is blank / full-page screenshot | The eCourts site changed its CAPTCHA selector. Add the new selector to `captchaSelectors[]` in `CaptureCaptchaImageAsync`. |
| `Could not find input field` exception | The eCourts form DOM changed. Add the new selector to `FillFieldAsync` call in `SearchCaseAsync`. |
| Session expired errors | Users are waiting too long between captcha and search. Increase `SessionTimeoutMinutes` constant. |
| All fields in result are `null` | eCourts HTML layout changed. Debug by logging `page.ContentAsync()` and updating `ExtractByLabel` selectors. |
| High memory usage | Ensure `CleanupExpiredSessionsAsync` is running. Check for unclosed browser contexts. |

---

## 📦 Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Playwright` | Latest stable | Headless Chromium browser automation |
| `HtmlAgilityPack` | Latest stable | Parsing scraped HTML from eCourts portal |

---

## 🔗 Related Modules

- **[E-Court Tracker (Main)](../Ecourt%20Tracker/README.md)** — Full-stack application this scraper is part of.
- **`ECourtTracker.API/Services/`** — Where `ECourtScraperService.cs` is deployed.
- **`ecourt-tracker-ui/src/pages/user/ECourtSearch.tsx`** — Frontend page that consumes these APIs.

---

## 📜 License

Part of the E-Court Tracker project. For internal/educational use only.  
Live scraping of [services.ecourts.gov.in](https://services.ecourts.gov.in) should comply with the website's terms of service.
