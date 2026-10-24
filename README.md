# 📄 ResumeTailor

> **AI-powered resume tailoring tool that automatically customizes your resume for each job posting — maximizing ATS keyword match scores and generating professional PDF & DOCX outputs.**

---

## ✨ Features

- 🤖 **LLM-Powered Tailoring** — Uses a locally running [Ollama](https://ollama.com) instance (`qwen2.5:14b`) to intelligently tailor your resume content to each job description.
- 🌐 **Automated Job Scraping** — Reads a list of job URLs from an Excel workbook (`jobs.xlsx`) and scrapes each listing using a headless Chromium browser (Playwright), including support for SPA-heavy sites like Workday and Greenhouse.
- 📊 **ATS Score Analysis** — Calculates an Applicant Tracking System (ATS) keyword match score for every tailored resume, showing matched keywords and keyword gaps.
- 🔑 **Deterministic Keyword Injection** — Guarantees that every technology keyword found in a job description is injected into the resume skills section — no LLM unreliability.
- 📎 **Multi-Format Output** — Generates polished **PDF** (via Playwright/Chromium) and **Word DOCX** (via HtmlToOpenXml) files for every job.
- 🗃️ **Smart Resume Caching** — Parses your `resume.pdf` once into a structured `master.json` on first run; all subsequent runs load instantly from cache.

---

## 🧱 Architecture

```
jobs.xlsx  ──►  ExcelParser
                     │
                     ▼
              ScraperService  (Playwright / headless Chromium)
                     │  job description text
                     ▼
               LlmService  (OllamaSharp → qwen2.5:14b)
                     │  TailoredPatch (JSON)
                     ▼
         JdKeywordInjector  (deterministic keyword merge)
                     │
                     ├──►  AtsScorer  (keyword match %)
                     │
                     ▼
             ResumeRenderer  (HTML + Markdown)
                     │
          ┌──────────┴──────────┐
          ▼                     ▼
   DocumentConverter      DocumentConverter
     (PDF via             (DOCX via
      Playwright)          HtmlToOpenXml)
```

---

## 🚀 Getting Started

### Prerequisites

| Requirement | Details |
|---|---|
| **.NET** | .NET 10.0 SDK or later |
| **Ollama** | Running locally at `http://localhost:11434` |
| **Ollama Model** | `qwen2.5:14b` pulled and available |
| **Playwright** | Chromium browser (installed automatically on first run) |

### 1. Pull the required Ollama model

```bash
ollama pull qwen2.5:14b
```

### 2. Prepare your inputs

Place the following files in the project root directory:

| File | Description |
|---|---|
| `resume.pdf` | Your master resume (only needed on the **first** run) |
| `jobs.xlsx` | Excel workbook with job listing URLs (one URL per row) |

> **Tip:** After the first run, `master.json` is generated and cached. Delete it and re-drop `resume.pdf` to trigger a fresh parse.

### 3. Build & Run

```bash
dotnet run
```

Output files are saved to `<build_output>/output/`:

- `Resume_1.pdf`, `Resume_1.docx`, `Resume_1.md`
- `Resume_2.pdf`, `Resume_2.docx`, `Resume_2.md`
- *(one set per job URL)*

---

## ⚙️ CLI Flags

| Flag | Description |
|---|---|
| *(no flag)* | Default mode — scrape all URLs and generate tailored resumes |
| `--preview` | Render the master resume as-is (no tailoring) to `Preview_Master.pdf/docx/md` |
| `--dump-pdf` | Extract raw text from `resume.pdf` and write it to `extracted_pdf_text.txt` — useful for debugging the PDF parser |
| `--re-render` | Re-render all existing `Resume_*.md` files in the output directory to PDF and DOCX without re-calling the LLM |

```bash
dotnet run -- --preview
dotnet run -- --dump-pdf
dotnet run -- --re-render
```

---

## 📦 NuGet Dependencies

| Package | Purpose |
|---|---|
| `OllamaSharp` | Communicates with the local Ollama LLM API |
| `Microsoft.Playwright` | Headless Chromium for job page scraping and PDF rendering |
| `HtmlToOpenXml.dll` | Converts rendered HTML to Word `.docx` |
| `Markdig` | Markdown processing |
| `ClosedXML` | Reads job URLs from `jobs.xlsx` |
| `UglyToad.PdfPig` | Extracts text from your source `resume.pdf` |

---

## 📁 Project Structure

| File | Description |
|---|---|
| `Program.cs` | Entry point — orchestrates the full pipeline |
| `LlmService.cs` | Ollama integration; ATS-optimization prompts and JSON patch parsing |
| `ScraperService.cs` | Playwright-based headless job page scraper |
| `ResumeModel.cs` | Strongly-typed data model for the structured resume |
| `ResumeParser.cs` | Extracts text from `resume.pdf` and parses it into `ResumeModel` via LLM |
| `ResumeRenderer.cs` | Converts `ResumeModel` → HTML and Markdown for rendering |
| `DocumentConverter.cs` | Generates PDF (Playwright) and DOCX (HtmlToOpenXml) from HTML |
| `AtsScorer.cs` | Keyword-based ATS match scoring against a curated tech dictionary |
| `JdKeywordInjector.cs` | Deterministic regex-based keyword & bullet injection |
| `ExcelParser.cs` | Reads job URLs from `jobs.xlsx` using ClosedXML |
| `OllamaGuard.cs` | Ensures Ollama is running before any LLM calls are made |
| `master.json` | Cached structured representation of your master resume |

---

## 🔄 How It Works

1. **First Run (PDF parsing):** If `master.json` does not exist, `resume.pdf` is parsed via `PdfPig`, and the extracted text is sent to the LLM to produce a structured JSON resume (`master.json`).

2. **Subsequent Runs:** `master.json` is loaded directly, skipping the expensive parse step.

3. **Per-Job Loop:**
   - Scrape the job listing URL with Playwright.
   - Send the job description + master resume to Ollama (`qwen2.5:14b`) for a structured "patch" in JSON.
   - Apply the patch to the master resume, then run deterministic post-processing:
     - Restore any bullets the LLM may have dropped.
     - Inject all JD tech keywords into the skills section.
   - Calculate and display the ATS match score.
   - Render to HTML → PDF (Playwright) + DOCX (HtmlToOpenXml) + Markdown.

---

## 📊 Sample Console Output

```
┌─ [1/5] https://example.com/jobs/software-engineer
│  → Scraping job description...
│  → Scraped 8,412 characters.
│  → Requesting ATS patch from LLM (qwen2.5:14b)...
│  [ATS]  ★ Match Score: 87% (Excellent)
│         ✓ Matched: C#, .NET Core, Azure, Docker, Kubernetes, REST APIs, ...
│         ⚠ Keyword Gaps: gRPC, Dapr, KEDA
│  [MD]   ✓ Saved: output\Resume_1.md
│  [PDF]  ✓ Generated: output\Resume_1.pdf
│  [DOCX] ✓ Generated: output\Resume_1.docx
│  ── Finished in 34.2s  (PDF: ✓  DOCX: ✓)
└─
```

---

## 📝 Notes

- Ollama is started automatically if not already running (via `OllamaGuard`).
- If the LLM returns a malformed patch, the tool gracefully falls back to the unmodified master resume.
- If a job listing scrape returns empty content (e.g. behind a login wall), the URL is skipped and reported in the final summary.
- The ATS scorer uses a curated tech dictionary to prioritize industry-relevant keywords over generic stop words.

---

## 📄 License

This project is for personal use. No license is currently specified.
