using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OllamaSharp;

namespace ResumeTailor;

/// <summary>
/// Wraps OllamaSharp to send an ATS-optimization prompt to a locally running
/// Ollama instance. Formats output as strict, raw LaTeX starting with
/// \documentclass and ending with \end{document}.
/// </summary>
public class LlmService
{
    private const string ModelName     = "qwen2.5:14b";
    private const string OllamaBaseUrl = "http://localhost:11434";

    private const string LatexSystemPrompt = """
        You are an expert ATS resume optimizer and professional LaTeX typesetter.

        YOUR TASK:
        Analyze the Target Job Description and tailor the candidate's Master Resume (provided in raw LaTeX) to maximize ATS alignment and keyword matching.

        STRICT REQUIREMENTS:
        1. Maximize Technical Skills:
           - Keep as many relevant technical skills from the Master Resume as possible.
           - Inject any matching technical keywords, programming languages, frameworks, libraries, databases, and cloud tools from the Target Job Description into the Technical Skills section.

        2. 5-6 Bullets Per Role:
           - For every job listed in the Professional Experience section, generate exactly 5 to 6 highly relevant, achievement-oriented bullet points that align with the job description.
           - Do not reduce the number of bullet points below 5 for any role under any circumstance.
           - Each bullet point must begin with a strong action verb, emphasize measurable impact, and integrate relevant keywords from the job description.

        3. Professional Summary:
           - Rewrite the summary to align directly with the target job title, core requirements, and domain.

        4. Experience Years:
           - The candidate has strictly "5+ years" of professional experience (started in Jan 2020).
           - You must ALWAYS use "5+ years" or "5 years". NEVER write 6, 6+, 7, or any higher number.

        5. Strict LaTeX Output:
           - Output ONLY valid raw LaTeX code.
           - Do not include markdown code blocks (```), explanations, notes, or conversational filler.
           - The output must begin exactly with \documentclass and end exactly with \end{document}.
        """;

    /// <summary>
    /// Analyzes the job description against the master LaTeX resume and generates
    /// a customized, ATS-tailored resume in raw LaTeX.
    /// Output strictly begins with \documentclass and ends with \end{document}.
    /// </summary>
    /// <param name="jobDescription">The target job listing text.</param>
    /// <param name="masterResumeTex">The raw LaTeX content of the master resume.</param>
    /// <returns>A string containing only valid, raw LaTeX code.</returns>
    public async Task<string> TailorResumeAsync(string jobDescription, string masterResumeTex)
    {
        string userPrompt =
            $$"""
            === TARGET JOB DESCRIPTION ===
            {{jobDescription}}

            === MASTER RESUME (LATEX) ===
            {{masterResumeTex}}

            INSTRUCTIONS:
            Tailor the Master Resume for the Target Job Description following these rules:
            1. Maximize Technical Skills: Retain all relevant master skills and inject matching keywords from the job description.
            2. 5-6 Bullets Per Role: For every job in Professional Experience, generate exactly 5 to 6 bullet points (never fewer than 5).
            3. Experience Duration: The candidate strictly has "5+ years" of professional experience (never 6 or 6+).
            4. Strict LaTeX Output: Output ONLY raw LaTeX code. Begin exactly with \documentclass and end with \end{document}. No markdown fences (no ```).
            """;

        var client = new OllamaApiClient(new Uri(OllamaBaseUrl), ModelName);
        var chat   = new Chat(client, LatexSystemPrompt);

        var sb = new StringBuilder();
        await foreach (var token in chat.SendAsync(userPrompt))
        {
            if (token is not null)
                sb.Append(token);
        }

        string rawResponse = sb.ToString().Trim();
        return CleanLatexOutput(rawResponse);
    }

    private const string JsonSystemPrompt = """
        You are an expert ATS resume optimizer and JSON generator.

        YOUR TASK:
        Analyze the Target Job Description and Candidate Resume Data, then return a tailored JSON patch to maximize ATS alignment and keyword matching.

        STRICT REQUIREMENTS:
        1. Professional Summary:
           - Generate 5 to 6 concise, tailored bullet points targeting the job title, core requirements, and domain.
           - CRITICAL: The candidate has strictly "5+ years" of professional experience (started career in Jan 2020).
           - The summary MUST explicitly state "5+ years" (or "5 years") of professional experience (e.g. in the very first bullet point: "[Role/Job Title] with 5+ years of professional experience...").
           - Every resume summary MUST mention 5+ years of experience. NEVER omit the years of experience. NEVER write 6, 6+, 7, or higher.

        2. Experience Bullets — PRESERVE AND AUGMENT (DO NOT REPLACE):
           - For EVERY job in the experience section you are given the candidate's EXISTING bullet points.
           - YOU MUST KEEP EVERY EXISTING BULLET POINT. Do not remove, shorten, or paraphrase existing bullets.
           - If the existing bullets are already 5 or more, enrich them by weaving in JD keywords and technologies inline — do not remove any.
           - If there are fewer than 5 existing bullets, ADD brand-new achievement-oriented bullets (starting with a strong action verb, emphasizing measurable impact) that integrate keywords, tools, and technologies from the job description, until you reach exactly 5 to 6 bullets total.
           - Inject ALL relevant technologies, frameworks, programming languages, databases, cloud tools, and keywords found in the job description into the bullets.
           - Keep experience duration strictly aligned with "5+ years" of experience.

        3. Technical Skills — INJECT ALL JD KEYWORDS:
           - Retain every skill already in the master resume.
           - Extract EVERY technical keyword, programming language, framework, library, database, cloud service, tool, and methodology from the Job Description and add it to the most appropriate skill category.
           - Do not miss any technology or keyword mentioned in the JD.

        4. Strict JSON Output:
           - Return ONLY the valid JSON object matching the requested schema.
           - Do not include markdown code fences (```), explanations, notes, or conversational text.
        """;

    // ── Section-aware system prompts (one per section) ───────────────────────

    private const string ParseHeaderSystemPrompt = """
        You are a resume parser. Extract ONLY the header info from the resume text.
        Return a JSON object with exactly these keys (use empty string if absent):
        { "Name": "", "Email": "", "Phone": "", "LinkedIn": "", "Location": "" }
        Return ONLY the JSON object. No markdown, no explanation.
        """;

    private const string ParseSummarySystemPrompt = """
        You are a resume parser. The text below is the SUMMARY/OBJECTIVE section of a resume.
        Extract ONLY the bullet points or sentences from this section as a JSON array of strings.
        Do NOT include bullet symbols (•, -, *) in the strings.
        Return ONLY: ["bullet 1", "bullet 2", ...]
        If there is nothing, return: []
        """;

    private const string ParseSkillsSystemPrompt = """
        You are a resume parser. The text below is the TECHNICAL SKILLS / SKILLS section of a resume.
        Group the skills by category and return a JSON array:
        [ { "Category": "Category Name", "Items": ["skill1", "skill2"] }, ... ]
        Each item in Items should be a single skill or technology, split on commas if needed.
        Return ONLY the JSON array. No markdown, no explanation.
        """;

    private const string ParseExperienceSystemPrompt = """
        You are a resume parser. The text below is the PROFESSIONAL EXPERIENCE / WORK HISTORY section.
        Extract EVERY job entry and return a JSON array:
        [
          {
            "Title": "Job Title",
            "Company": "Company Name",
            "DateRange": "Mon YYYY – Mon YYYY or Present",
            "Location": "City, State or Remote",
            "Environment": "comma-separated tech stack if listed (empty string otherwise)",
            "Bullets": ["bullet point 1", "bullet point 2", ...]
          }
        ]
        IMPORTANT:
        - Include ALL jobs — do not skip any.
        - Bullets are the achievement/responsibility lines under each job.
        - Strip leading bullet symbols (•, -, *) from Bullets strings.
        - If a field is missing, use an empty string.
        Return ONLY the JSON array. No markdown, no explanation.
        """;

    private const string ParseEducationSystemPrompt = """
        You are a resume parser. The text below is the EDUCATION section of a resume.
        Extract every degree/school and return a JSON array:
        [ { "Degree": "", "School": "", "DateRange": "", "Notes": "" } ]
        Return ONLY the JSON array. No markdown, no explanation.
        """;

    private const string ParseProjectsSystemPrompt = """
        You are a resume parser. The text below is the PROJECTS section of a resume.
        Extract every project and return a JSON array:
        [ { "Name": "", "Technologies": "", "Bullets": ["bullet 1", ...] } ]
        Strip leading bullet symbols from Bullets. Return ONLY the JSON array.
        """;

    private const string ParseCertsSystemPrompt = """
        You are a resume parser. The text below contains certifications, honors, or awards.
        Return a flat JSON array of strings: ["cert 1", "cert 2"]
        Return ONLY the JSON array. No markdown, no explanation.
        """;

    /// <summary>
    /// Parses raw resume text into a <see cref="ResumeModel"/> using a section-aware,
    /// multi-call strategy. The raw text is first split into named sections by C# regex,
    /// then each section is sent to the LLM separately — avoiding context-window overflow
    /// and confusion about which text belongs to which field.
    /// </summary>
    public async Task<ResumeModel?> ParseResumeAsync(string rawResumeText)
    {
        // ── Step 1: Split the raw text into named sections ────────────────────
        var sections = SplitIntoSections(rawResumeText);

        PrintSectionMap(sections);

        // ── Step 2: Parse header (name + contact) ─────────────────────────────
        string headerText = sections.TryGetValue("HEADER", out var h) ? h : rawResumeText[..Math.Min(300, rawResumeText.Length)];
        var headerJson    = await CallLlmAsync(ParseHeaderSystemPrompt, headerText);
        var (name, contact) = ParseHeader(headerJson, headerText);

        // ── Step 3: Parse each section independently ──────────────────────────
        var summary = await ParseArrayAsync<List<string>>(
            ParseSummarySystemPrompt,
            sections.GetValueOrDefault("SUMMARY", ""),
            "summary",
            ParseStringArray);
        if (summary.Count == 0 && sections.TryGetValue("SUMMARY", out var sText))
            summary = FallbackParseSummary(sText);

        var skills = await ParseArrayAsync<List<SkillGroup>>(
            ParseSkillsSystemPrompt,
            sections.GetValueOrDefault("SKILLS", ""),
            "skills",
            ParseSkillGroups);
        if (skills.Count == 0 && sections.TryGetValue("SKILLS", out var skText))
            skills = FallbackParseSkills(skText);

        var experience = await ParseArrayAsync<List<ExperienceEntry>>(
            ParseExperienceSystemPrompt,
            sections.GetValueOrDefault("EXPERIENCE", ""),
            "experience",
            ParseExperienceEntries);
        if (experience.Count == 0 && sections.TryGetValue("EXPERIENCE", out var expText))
            experience = FallbackParseExperience(expText);

        var education = await ParseArrayAsync<List<EducationEntry>>(
            ParseEducationSystemPrompt,
            sections.GetValueOrDefault("EDUCATION", ""),
            "education",
            ParseEducationEntries);
        if (education.Count == 0 && sections.TryGetValue("EDUCATION", out var eduText))
            education = FallbackParseEducation(eduText);

        var projects = await ParseArrayAsync<List<ProjectEntry>>(
            ParseProjectsSystemPrompt,
            sections.GetValueOrDefault("PROJECTS", ""),
            "projects",
            ParseProjectEntries);
        if (projects.Count == 0 && sections.TryGetValue("PROJECTS", out var prText))
            projects = FallbackParseProjects(prText);

        var certs = await ParseArrayAsync<List<string>>(
            ParseCertsSystemPrompt,
            sections.GetValueOrDefault("CERTIFICATIONS", ""),
            "certs",
            ParseStringArray);
        if (certs.Count == 0 && sections.TryGetValue("CERTIFICATIONS", out var cText))
            certs = FallbackParseList(cText);

        return new ResumeModel(
            Name:           name,
            Contact:        contact,
            Summary:        summary,
            Experience:     experience,
            Education:      education,
            Skills:         skills,
            Projects:       projects,
            Certifications: certs,
            Honors:         []);
    }

    // ── Section splitting ─────────────────────────────────────────────────────

    /// <summary>
    /// Splits raw resume text into named sections based on common resume section header patterns.
    /// Handles all-caps headers, colons (TECHNICAL SKILLS:), Title Case headers, and underline-style headers.
    /// Also separates unlabelled summary paragraphs from the contact header.
    /// </summary>
    public static Dictionary<string, string> SplitIntoSections(string text)
    {
        var sectionPatterns = new (string Key, string Pattern)[]
        {
            ("SUMMARY",          @"\b(SUMMARY|PROFESSIONAL\s+SUMMARY|OBJECTIVE|CAREER\s+OBJECTIVE|PROFILE|ABOUT\s+ME|EXECUTIVE\s+SUMMARY)\b"),
            ("SKILLS",           @"\b(TECHNICAL\s+SKILLS|SKILLS|CORE\s+COMPETENCIES|TECHNOLOGIES|KEY\s+SKILLS|EXPERTISE|TECHNICAL\s+EXPERTISE|AREAS\s+OF\s+EXPERTISE)\b"),
            ("EXPERIENCE",       @"\b(PROFESSIONAL\s+EXPERIENCE|WORK\s+EXPERIENCE|EXPERIENCE|EMPLOYMENT\s+HISTORY|WORK\s+HISTORY|PROFESSIONAL\s+BACKGROUND|RELEVANT\s+EXPERIENCE)\b"),
            ("EDUCATION",        @"\b(EDUCATION|ACADEMIC\s+BACKGROUND|EDUCATIONAL\s+BACKGROUND|QUALIFICATIONS)\b"),
            ("PROJECTS",         @"\b(PROJECTS|ACADEMIC\s+PROJECTS|PERSONAL\s+PROJECTS|SIDE\s+PROJECTS|KEY\s+PROJECTS|NOTABLE\s+PROJECTS|TECHNICAL\s+PROJECTS)\b"),
            ("CERTIFICATIONS",   @"\b(CERTIFICATIONS|CERTIFICATES|LICENSES|HONORS|AWARDS|ACHIEVEMENTS|ACCOMPLISHMENTS)\b"),
        };

        var lines   = text.Split('\n');
        var result  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string?      currentKey = "HEADER";
        var          currentLines = new List<string>();

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            // Strip leading/trailing punctuation and markdown symbols (colons, bullets, dashes, hashes)
            string candidate = Regex.Replace(trimmed, @"^[\s•\-\*#_~]+|[\s:\-\—\–_#~]+$", "").Trim();
            string? matched = null;

            if (candidate.Length > 0 && candidate.Length < 60)
            {
                foreach (var (key, pattern) in sectionPatterns)
                {
                    if (Regex.IsMatch(candidate, $"^{pattern}$", RegexOptions.IgnoreCase))
                    {
                        matched = key;
                        break;
                    }
                }
            }

            if (matched is not null)
            {
                if (currentKey is not null)
                    result[currentKey] = string.Join("\n", currentLines).Trim();

                currentKey   = matched;
                currentLines = [];
            }
            else
            {
                currentLines.Add(line);
            }
        }

        if (currentKey is not null)
            result[currentKey] = string.Join("\n", currentLines).Trim();

        // If SUMMARY was not explicitly tagged by a heading, split off any descriptive paragraphs from HEADER
        if (!result.ContainsKey("SUMMARY") || string.IsNullOrWhiteSpace(result["SUMMARY"]))
        {
            if (result.TryGetValue("HEADER", out var headerText) && !string.IsNullOrWhiteSpace(headerText))
            {
                var hLines = headerText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                var contactLines = new List<string>();
                var summaryLines = new List<string>();

                foreach (var hl in hLines)
                {
                    bool isContact = hl.Contains('@') ||
                                     Regex.IsMatch(hl, @"\+?\d[\d\s\-\(\)]{7,}") ||
                                     hl.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) ||
                                     hl.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                                     hl.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                                     hl.Contains("https://", StringComparison.OrdinalIgnoreCase);

                    // First 1-2 short lines without contact symbols are candidate Name and Title
                    bool isHeaderTitle = !isContact && summaryLines.Count == 0 && hl.Split(' ').Length <= 4 &&
                                         !Regex.IsMatch(hl, @"\b(?:years|experience|building|skilled|engineer\s+with|developer\s+with)\b", RegexOptions.IgnoreCase);

                    if (isContact || isHeaderTitle)
                    {
                        contactLines.Add(hl);
                    }
                    else
                    {
                        summaryLines.Add(hl);
                    }
                }

                if (summaryLines.Count > 0)
                {
                    result["HEADER"] = string.Join("\n", contactLines).Trim();
                    result["SUMMARY"] = string.Join("\n", summaryLines).Trim();
                }
            }
        }

        return result;
    }

    private static void PrintSectionMap(Dictionary<string, string> sections)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("[PARSE] Detected sections: " +
            string.Join(", ", sections.Keys.Select(k => $"{k}({sections[k].Length}ch)")));
        Console.ResetColor();
    }

    // ── LLM call helpers ──────────────────────────────────────────────────────

    private async Task<string> CallLlmAsync(string systemPrompt, string userContent)
    {
        if (string.IsNullOrWhiteSpace(userContent))
            return "{}";

        var client = new OllamaApiClient(new Uri(OllamaBaseUrl), ModelName);
        var chat   = new Chat(client, systemPrompt);
        var sb     = new StringBuilder();

        await foreach (var token in chat.SendAsync(userContent))
            if (token is not null) sb.Append(token);

        return StripCodeFences(sb.ToString().Trim());
    }

    private async Task<T> ParseArrayAsync<T>(
        string systemPrompt,
        string sectionText,
        string sectionName,
        Func<string, T> parser)
        where T : new()
    {
        if (string.IsNullOrWhiteSpace(sectionText))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[PARSE] {sectionName}: section not found, skipping.");
            Console.ResetColor();
            return new T();
        }

        string raw = await CallLlmAsync(systemPrompt, sectionText);
        try
        {
            T result = parser(raw);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[PARSE] {sectionName}: parsed OK.");
            Console.ResetColor();
            return result;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[PARSE] ⚠ {sectionName} parse error: {ex.Message}");
            Console.ResetColor();
            return new T();
        }
    }

    // ── Per-section parsers ───────────────────────────────────────────────────

    private static string GetProp(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var val))
                return val.GetString() ?? "";
        }
        foreach (var prop in el.EnumerateObject())
        {
            if (names.Any(n => n.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
                return prop.Value.GetString() ?? "";
        }
        return "";
    }

    private static List<string> GetStringArray(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.Array)
            {
                return val.EnumerateArray()
                    .Select(x => x.GetString()?.Trim().TrimStart('•', '-', '*', ' ') ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }
        }
        foreach (var prop in el.EnumerateObject())
        {
            if (names.Any(n => n.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                return prop.Value.EnumerateArray()
                    .Select(x => x.GetString()?.Trim().TrimStart('•', '-', '*', ' ') ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }
        }
        return [];
    }

    private static (string Name, ContactInfo Contact) ParseHeader(string json, string rawHeaderText)
    {
        string name = "", email = "", phone = "", linkedin = "", location = "";
        try
        {
            int s = json.IndexOf('{'), e = json.LastIndexOf('}');
            if (s >= 0 && e >= s)
            {
                using var doc  = JsonDocument.Parse(json[s..(e + 1)]);
                var root = doc.RootElement;
                name     = GetProp(root, "Name", "name", "full_name");
                email    = GetProp(root, "Email", "email");
                phone    = GetProp(root, "Phone", "phone", "phone_number");
                linkedin = GetProp(root, "LinkedIn", "linkedin", "linkedin_url");
                location = GetProp(root, "Location", "location", "address", "city");
            }
        }
        catch { }

        // Fallbacks from raw header text if LLM missed anything
        if (string.IsNullOrWhiteSpace(name))
        {
            var firstLine = rawHeaderText.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            if (!firstLine.Contains('@') && firstLine.Length < 40)
                name = firstLine;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            var m = Regex.Match(rawHeaderText, @"[\w\.\-]+@[\w\-]+\.[\w]+");
            if (m.Success) email = m.Value;
        }
        if (string.IsNullOrWhiteSpace(phone))
        {
            var m = Regex.Match(rawHeaderText, @"\+?\d[\d\s\-\(\)]{7,}\d");
            if (m.Success) phone = m.Value;
        }
        if (string.IsNullOrWhiteSpace(linkedin))
        {
            var m = Regex.Match(rawHeaderText, @"https?://(?:www\.)?linkedin\.com/in/[\w\-]+", RegexOptions.IgnoreCase);
            if (m.Success) linkedin = m.Value;
        }

        return (name, new ContactInfo(email, phone, linkedin, location));
    }

    private static List<string> ParseStringArray(string json)
    {
        int s = json.IndexOf('['), e = json.LastIndexOf(']');
        if (s < 0 || e < s) return [];
        using var doc = JsonDocument.Parse(json[s..(e + 1)]);
        return doc.RootElement.EnumerateArray()
            .Select(el => el.GetString()?.Trim().TrimStart('•', '-', '*', ' ') ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static List<SkillGroup> ParseSkillGroups(string json)
    {
        int s = json.IndexOf('['), e = json.LastIndexOf(']');
        if (s < 0 || e < s) return [];
        using var doc = JsonDocument.Parse(json[s..(e + 1)]);
        var groups = new List<SkillGroup>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string cat = GetProp(el, "Category", "category", "name", "group");
            var items  = GetStringArray(el, "Items", "items", "skills", "values");
            if (items.Count == 0 && el.TryGetProperty("Items", out var strVal) && strVal.ValueKind == JsonValueKind.String)
            {
                items = strVal.GetString()?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).ToList() ?? [];
            }
            if (!string.IsNullOrWhiteSpace(cat) && items.Count > 0)
                groups.Add(new SkillGroup(cat, items));
        }
        return groups;
    }

    private static List<ExperienceEntry> ParseExperienceEntries(string json)
    {
        json = SanitizeJson(json);
        int s = json.IndexOf('[');
        if (s < 0) return [];
        int e = json.LastIndexOf(']');
        if (e < s)
        {
            int lastBrace = json.LastIndexOf('}');
            if (lastBrace > s)
            {
                json = json[s..(lastBrace + 1)] + "]";
                s = 0;
                e = json.Length - 1;
            }
            else return [];
        }
        else
        {
            json = json[s..(e + 1)];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<ExperienceEntry>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string title   = GetProp(el, "Title", "title", "job_title", "role");
                string company = GetProp(el, "Company", "company", "company_name", "employer");
                string date    = GetProp(el, "DateRange", "dateRange", "dates", "date", "period");
                string loc     = GetProp(el, "Location", "location", "city");
                string env     = GetProp(el, "Environment", "environment", "tech_stack");
                var bullets    = GetStringArray(el, "Bullets", "bullets", "bullet_points", "responsibilities", "points");

                if (!string.IsNullOrWhiteSpace(company) || !string.IsNullOrWhiteSpace(title))
                    list.Add(new ExperienceEntry(title, company, date, loc, env, bullets));
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    private static List<EducationEntry> ParseEducationEntries(string json)
    {
        int s = json.IndexOf('['), e = json.LastIndexOf(']');
        if (s < 0 || e < s) return [];
        using var doc = JsonDocument.Parse(json[s..(e + 1)]);
        return doc.RootElement.EnumerateArray().Select(el => new EducationEntry(
            GetProp(el, "Degree", "degree", "qualification"),
            GetProp(el, "School", "school", "university", "institution", "college"),
            GetProp(el, "DateRange", "dateRange", "dates", "date", "year", "period"),
            GetProp(el, "Notes", "notes", "gpa")
        )).ToList();
    }

    private static List<ProjectEntry> ParseProjectEntries(string json)
    {
        int s = json.IndexOf('['), e = json.LastIndexOf(']');
        if (s < 0 || e < s) return [];
        using var doc = JsonDocument.Parse(json[s..(e + 1)]);
        var list = new List<ProjectEntry>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string name  = GetProp(el, "Name", "name", "title", "project_name");
            string tech  = GetProp(el, "Technologies", "technologies", "tech_stack", "tools");
            var bullets  = GetStringArray(el, "Bullets", "bullets", "bullet_points", "points");
            if (!string.IsNullOrWhiteSpace(name))
                list.Add(new ProjectEntry(name, tech, bullets));
        }
        return list;
    }

    // ── Deterministic C# Fallback Parsers ─────────────────────────────────────

    public static List<string> FallbackParseSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var bullets = new List<string>();
        var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('•', '-', '*', ' ').Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            // If the line is a multi-sentence paragraph, split into sentences
            if (trimmed.Length > 120 && !line.TrimStart().StartsWith("•") && !line.TrimStart().StartsWith("-"))
            {
                var sentences = Regex.Split(trimmed, @"(?<=[.!?])\s+(?=[A-Z])");
                foreach (var s in sentences)
                    if (!string.IsNullOrWhiteSpace(s)) bullets.Add(s.Trim());
            }
            else
            {
                bullets.Add(trimmed);
            }
        }
        return bullets;
    }

    public static List<SkillGroup> FallbackParseSkills(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var list = new List<SkillGroup>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('•', '-', '*', ' ').Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            int colon = trimmed.IndexOf(':');
            if (colon > 0)
            {
                string cat = trimmed[..colon].Trim();
                string rest = trimmed[(colon + 1)..].Trim().TrimEnd('.');
                var items = rest.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (!string.IsNullOrWhiteSpace(cat) && items.Count > 0)
                    list.Add(new SkillGroup(cat, items));
            }
        }
        return list;
    }

    public static List<string> FallbackParseList(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('•', '-', '*', ' ').Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !Regex.IsMatch(l, @"^certifications?:?$", RegexOptions.IgnoreCase))
            .ToList();
    }

    public static List<EducationEntry> FallbackParseEducation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var list = new List<EducationEntry>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (Regex.IsMatch(line, @"\b(Master|Bachelor|Ph\.?D|B\.?S|M\.?S|Associate|Diploma)\b", RegexOptions.IgnoreCase))
            {
                string dateMatch = "";
                var dm = Regex.Match(line, @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\d{4})\b.*", RegexOptions.IgnoreCase);
                string degree = line;
                if (dm.Success)
                {
                    dateMatch = dm.Value.Trim();
                    degree = line[..dm.Index].Trim().TrimEnd('–', '-', '|', ',');
                }
                string school = (i + 1 < lines.Count) ? lines[++i] : "";
                list.Add(new EducationEntry(degree, school, dateMatch, ""));
            }
        }
        return list;
    }

    public static List<ProjectEntry> FallbackParseProjects(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var list = new List<ProjectEntry>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        ProjectEntry? current = null;
        var currentBullets = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("•") || line.StartsWith("-") || line.StartsWith("*"))
            {
                currentBullets.Add(line.TrimStart('•', '-', '*', ' ').Trim());
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                if (current is not null)
                {
                    list.Add(current with { Bullets = new List<string>(currentBullets) });
                    currentBullets.Clear();
                }
                current = new ProjectEntry(line, "", []);
            }
        }
        if (current is not null)
        {
            list.Add(current with { Bullets = currentBullets });
        }
        return list;
    }

    public static string SanitizeJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        // Normalize unicode curly quotes and dashes to standard ASCII
        string clean = raw.Replace('“', '"')
                          .Replace('”', '"')
                          .Replace('‘', '\'')
                          .Replace('’', '\'')
                          .Replace('\u201C', '"')
                          .Replace('\u201D', '"')
                          .Replace('\u2018', '\'')
                          .Replace('\u2019', '\'');

        // Fix missing comma between consecutive lines (e.g. "value"\n"key": or ]\n"key": or }\n"key":)
        clean = Regex.Replace(clean, @"(?<=[""}\]]|\btrue\b|\bfalse\b|\bnull\b|\d)\s*\n\s*(?=[""{\[])", ",\n");

        // Fix comma instead of colon after property name e.g. "bullets", [ -> "bullets": [
        clean = Regex.Replace(clean, @"(?<=""[\w_]+"")\s*,\s*(?=[\[{\d""])", ": ");

        // Fix trailing comma before closing brace or bracket e.g. , } -> }
        clean = Regex.Replace(clean, @",\s*([}\]])", "$1");

        return clean;
    }

    public static List<ExperienceEntry> FallbackParseExperience(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var list = new List<ExperienceEntry>();
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        // Pattern for date ranges e.g. "May 2025 – Present", "Sep 2021 – Dec 2023", "Aug 2021 - Jul 2024"
        var datePattern = new Regex(@"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?|\d{4})\b\s*[\-–—\s]+\s*(?:Present|\d{4}|(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+\d{4})", RegexOptions.IgnoreCase);

        string currentTitle = "";
        string currentCompany = "";
        string currentDate = "";
        string currentLocation = "";
        string currentEnv = "";
        var currentBullets = new List<string>();

        void SaveCurrentRole()
        {
            if (!string.IsNullOrWhiteSpace(currentCompany) || !string.IsNullOrWhiteSpace(currentTitle))
            {
                list.Add(new ExperienceEntry(currentTitle, currentCompany, currentDate, currentLocation, currentEnv, new List<string>(currentBullets)));
                currentBullets.Clear();
                currentTitle = "";
                currentCompany = "";
                currentDate = "";
                currentLocation = "";
                currentEnv = "";
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];

            if (line.StartsWith("•") || line.StartsWith("-") || line.StartsWith("*"))
            {
                currentBullets.Add(line.TrimStart('•', '-', '*', ' ').Trim());
                continue;
            }

            if (line.StartsWith("Environment:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Environment :", StringComparison.OrdinalIgnoreCase))
            {
                currentEnv = line[(line.IndexOf(':') + 1)..].Trim();
                continue;
            }

            var dateMatch = datePattern.Match(line);
            if (dateMatch.Success)
            {
                SaveCurrentRole();

                currentDate = dateMatch.Value.Trim();
                string nonDate = line.Remove(dateMatch.Index, dateMatch.Length).Trim().TrimEnd(',', '-', '–', '|');

                if (nonDate.Contains('|'))
                {
                    var parts = nonDate.Split('|');
                    currentTitle = parts[0].Trim();
                    currentCompany = parts[1].Trim();
                }
                else
                {
                    if (nonDate.Contains(','))
                    {
                        var lastComma = nonDate.LastIndexOf(',');
                        currentCompany = nonDate[..lastComma].Trim();
                        currentLocation = nonDate[(lastComma + 1)..].Trim();
                    }
                    else
                    {
                        currentCompany = nonDate;
                    }

                    if (i + 1 < lines.Count && !lines[i + 1].StartsWith("•") && !lines[i + 1].StartsWith("-") && !datePattern.IsMatch(lines[i + 1]))
                    {
                        currentTitle = lines[++i].Trim();
                    }
                }
            }
            else if (currentBullets.Count == 0 && string.IsNullOrWhiteSpace(currentTitle) && !line.StartsWith("Client:", StringComparison.OrdinalIgnoreCase))
            {
                currentTitle = line;
            }
            else if (currentBullets.Count > 0 && !line.StartsWith("•") && !line.StartsWith("-"))
            {
                currentBullets[^1] += " " + line;
            }
        }

        SaveCurrentRole();
        return list;
    }

    /// <summary>
    /// Overload for structured <see cref="ResumeModel"/> pipeline: applies ATS tailoring
    /// patch to structured model.
    /// </summary>
    public async Task<TailoredPatch?> TailorResumeAsync(string jobDescription, ResumeModel master)
    {
        string cleanedJd = CleanJobDescription(jobDescription);

        // Build per-role context including ALL existing bullets so LLM can preserve + augment them
        var experienceContext = master.Experience.Select(e => new
        {
            company          = e.Company,
            title            = e.Title,
            date_range       = e.DateRange,
            environment      = e.Environment,
            existing_bullets = e.Bullets          // send every bullet, not just Take(3)
        });

        var resumeSummary = new
        {
            candidate_name   = master.Name,
            experience_years = "5+ years",
            experience       = experienceContext,
            skills           = master.Skills.Select(s => new
            {
                category = s.Category,
                items    = s.Items
            })
        };

        string resumeJson   = JsonSerializer.Serialize(resumeSummary, new JsonSerializerOptions { WriteIndented = false });
        string companyList  = string.Join("\n", master.Experience.Select(e => $"- \"{e.Company}\""));

        string prompt =
            $$"""
            === TARGET JOB DESCRIPTION ===
            {{cleanedJd}}

            === CANDIDATE RESUME PROFILE (existing bullets included for every role) ===
            {{resumeJson}}

            TASK:
            Tailor the candidate's resume for the target job description to achieve a maximum ATS match score.
            Return a single JSON object with these exact keys:

            1. "summary": Exactly 5 to 6 concise, achievement-oriented ATS bullet points tailored directly to this job description.
               MANDATORY RULE: The first bullet point MUST explicitly state "[Target Role/Job Title] with 5+ years of professional experience...". NEVER write 6, 6+, or 7 years.

            2. "experience": An array containing an entry for EACH of the candidate's companies listed below:
               {{companyList}}
               CRITICAL RULES FOR EXPERIENCE BULLETS:
               - Each role's existing_bullets are provided above. YOU MUST KEEP EVERY EXISTING BULLET in the output.
               - Do NOT remove, shorten, or paraphrase any existing bullet point.
               - If a role already has 5+ bullets, enrich them by weaving in JD keywords/technologies inline, then output all of them.
               - If a role has fewer than 5 bullets, ADD new achievement-oriented bullet points (strong action verb + measurable impact + JD keywords) until you reach exactly 5–6 total.
               - Inject ALL technologies, frameworks, languages, databases, cloud tools, and keywords from the JD into the bullets.
               Output format:
               [
                 { "company": "<Exact Company Name>", "bullets": ["existing bullet 1 (possibly enriched)", "existing bullet 2", ..., "new bullet if needed"] }
               ]

            3. "skills": Extract and inject EVERY technical keyword, programming language, framework, library, database, cloud service, tool, and methodology from the JD into the appropriate skill categories. Do NOT miss any JD technology. Output format:
               [
                 { "category": "Programming Languages", "items": ["C#", "Python", ...] }
               ]

            CRITICAL FORMATTING INSTRUCTIONS:
            - Return ONLY a valid JSON object starting with { and ending with }.
            - Every property name and string value must use double quotes.
            - Ensure every element and property is separated by a comma.
            - Do not include markdown code fences (no ```), explanations, or conversational filler.
            """;

        var client = new OllamaApiClient(new Uri(OllamaBaseUrl), ModelName);
        var chat   = new Chat(client, JsonSystemPrompt);

        var sb = new StringBuilder();
        await foreach (var token in chat.SendAsync(prompt))
        {
            if (token is not null)
                sb.Append(token);
        }

        return ParsePatch(sb.ToString(), master);
    }

    // ── LaTeX Helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Strips any markdown formatting or commentary, ensuring the string begins
    /// with \documentclass and ends with \end{document}.
    /// </summary>
    public static string CleanLatexOutput(string raw)
    {
        string text = raw.Trim();

        // Strip markdown code fences if present
        text = Regex.Replace(text, @"^```(?:latex|tex)?\s*", "", RegexOptions.IgnoreCase).Trim();
        text = Regex.Replace(text, @"\s*```$", "").Trim();

        // Locate \documentclass and \end{document}
        int start = text.IndexOf("\\documentclass", StringComparison.Ordinal);
        if (start < 0)
            return text;

        int end = text.LastIndexOf("\\end{document}", StringComparison.Ordinal);
        string result = end >= 0
            ? text[start..(end + "\\end{document}".Length)].Trim()
            : text[start..].Trim();

        return EnforceFivePlusYears(result);
    }

    /// <summary>
    /// Enforces that the candidate's stated experience duration is strictly "5+ years".
    /// Replaces instances of "6+ years", "over 6 years", "6 years", "7+ years", etc. with "5+ years".
    /// </summary>
    public static string EnforceFivePlusYears(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = Regex.Replace(text, @"\b(?:over\s+)?([6-9]|\d{2})\+?\s+years\s+of\b", "5+ years of", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?:over\s+)?([6-9]|\d{2})\+?\s+years\b", "5+ years", RegexOptions.IgnoreCase);
        return text;
    }

    /// <summary>
    /// Ensures that every resume's summary explicitly mentions "5+ years" (or "5 years") of professional experience.
    /// If not present in any bullet, it seamlessly integrates "with 5+ years of professional experience" into the first bullet.
    /// </summary>
    public static List<string> EnsureSummaryHasFiveYears(List<string> bullets)
    {
        if (bullets == null || bullets.Count == 0) return bullets ?? [];

        // 1. Normalize any existing year mentions to 5+ years
        var list = bullets.Select(EnforceFivePlusYears).ToList();

        // 2. Check if ANY bullet already mentions "5+ years" or "5 years"
        bool hasFiveYears = list.Any(b => Regex.IsMatch(b, @"\b5\+?\s+years\b", RegexOptions.IgnoreCase));
        if (hasFiveYears)
            return list;

        // 3. If not mentioned in any bullet, integrate into the first bullet
        string first = list[0].Trim();

        // Match common job title prefix patterns
        var titleMatch = Regex.Match(first, @"^(?<title>(?:Senior\s+|Lead\s+|Staff\s+|Principal\s+|Full[- ]Stack\s+|Backend\s+|Frontend\s+|Cloud\s+|AI\s+)*(?:Software\s+Engineer|Software\s+Developer|Full[- ]Stack\s+Developer|Backend\s+Developer|Frontend\s+Developer|System\s+Engineer|Solutions\s+Architect|Architect|Engineer|Developer|Specialist|Professional))\b(?<rest>.*)$", RegexOptions.IgnoreCase);

        if (titleMatch.Success)
        {
            string title = titleMatch.Groups["title"].Value.Trim();
            string rest = titleMatch.Groups["rest"].Value.Trim();

            if (Regex.IsMatch(rest, @"^with\b", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(rest, @"^with\s+(?:extensive|proven|strong|deep|hands-on)?\s*(?:professional\s+)?experience\b", RegexOptions.IgnoreCase))
                {
                    rest = Regex.Replace(rest, @"^with\s+(?:extensive|proven|strong|deep|hands-on)?\s*(?:professional\s+)?experience\s*(?:in\s+|with\s+)?", "", RegexOptions.IgnoreCase).Trim();
                    list[0] = string.IsNullOrWhiteSpace(rest)
                        ? $"{title} with 5+ years of professional experience."
                        : $"{title} with 5+ years of professional experience in {rest}";
                }
                else
                {
                    rest = Regex.Replace(rest, @"^with\s+", "", RegexOptions.IgnoreCase).Trim();
                    list[0] = $"{title} with 5+ years of professional experience and {rest}";
                }
            }
            else
            {
                list[0] = $"{title} with 5+ years of professional experience, {rest.TrimStart(',', ' ')}".Trim();
            }
        }
        else if (Regex.IsMatch(first, @"^[A-Z][a-z]+ing\b"))
        {
            // First bullet starts with an action/participle verb (e.g. "Transforming business operations...")
            list[0] = $"Software Engineer with 5+ years of professional experience {char.ToLower(first[0])}{first[1..]}";
        }
        else
        {
            // General fallback
            list[0] = $"Software Engineer with 5+ years of professional experience, {char.ToLower(first[0])}{first[1..]}";
        }

        return list;
    }

    // ── JSON Helpers for Structured Pipeline ──────────────────────────────────

    private static TailoredPatch? ParsePatch(string raw, ResumeModel master)
    {
        string json = StripCodeFences(raw.Trim());

        int braceStart = json.IndexOf('{');
        int braceEnd   = json.LastIndexOf('}');
        if (braceStart < 0 || braceEnd < braceStart)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("│  [LLM]  ⚠ Could not locate JSON object in response. Using master as-is.");
            Console.ResetColor();
            return null;
        }

        json = SanitizeJson(json[braceStart..(braceEnd + 1)]);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 1. Parse Summary
            var summaryBullets = new List<string>();
            if (root.TryGetProperty("summary", out var sumEl) ||
                root.TryGetProperty("professional_summary", out sumEl) ||
                root.TryGetProperty("summary_bullets", out sumEl))
            {
                if (sumEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in sumEl.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            summaryBullets.Add(s.Trim().TrimStart('•', '-', '*').Trim());
                    }
                }
                else if (sumEl.ValueKind == JsonValueKind.String)
                {
                    var s = sumEl.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        var lines = s.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 1)
                        {
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim().TrimStart('•', '-', '*').Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                    summaryBullets.Add(trimmed);
                            }
                        }
                        else
                        {
                            var sentences = Regex.Split(s, @"(?<=[.!?])\s+(?=[A-Z])");
                            foreach (var sentence in sentences)
                            {
                                var trimmed = sentence.Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                    summaryBullets.Add(trimmed);
                            }
                        }
                    }
                }
            }

            summaryBullets = EnsureSummaryHasFiveYears(summaryBullets);

            // 2. Parse Experience
            var companyBullets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var indexedExpBullets = new List<List<string>>();

            if (root.TryGetProperty("experience", out var expEl) ||
                root.TryGetProperty("experience_bullets", out expEl) ||
                root.TryGetProperty("work_experience", out expEl))
            {
                if (expEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in expEl.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            string company = "";
                            if (item.TryGetProperty("company", out var compEl) ||
                                item.TryGetProperty("company_name", out compEl) ||
                                item.TryGetProperty("name", out compEl))
                            {
                                company = compEl.GetString() ?? "";
                            }

                            var bullets = new List<string>();
                            if (item.TryGetProperty("bullets", out var bEl) ||
                                item.TryGetProperty("bullet_points", out bEl) ||
                                item.TryGetProperty("points", out bEl) ||
                                item.TryGetProperty("responsibilities", out bEl))
                            {
                                if (bEl.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var b in bEl.EnumerateArray())
                                    {
                                        var bStr = b.GetString();
                                        if (!string.IsNullOrWhiteSpace(bStr))
                                            bullets.Add(bStr.Trim().TrimStart('•', '-', '*').Trim());
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(company) && bullets.Count > 0)
                                companyBullets[company] = bullets;
                            else if (bullets.Count > 0)
                                indexedExpBullets.Add(bullets);
                        }
                        else if (item.ValueKind == JsonValueKind.Array)
                        {
                            var bullets = new List<string>();
                            foreach (var b in item.EnumerateArray())
                            {
                                var bStr = b.GetString();
                                if (!string.IsNullOrWhiteSpace(bStr))
                                    bullets.Add(bStr.Trim().TrimStart('•', '-', '*').Trim());
                            }
                            if (bullets.Count > 0)
                                indexedExpBullets.Add(bullets);
                        }
                    }
                }
            }

            // 3. Parse Skills
            var skills = new List<SkillGroup>();
            if (root.TryGetProperty("skills", out var skEl) ||
                root.TryGetProperty("technical_skills", out skEl))
            {
                if (skEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in skEl.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            string cat = "";
                            if (item.TryGetProperty("category", out var cEl) ||
                                item.TryGetProperty("name", out cEl) ||
                                item.TryGetProperty("group", out cEl))
                            {
                                cat = cEl.GetString() ?? "";
                            }

                            var items = new List<string>();
                            if (item.TryGetProperty("items", out var iEl) ||
                                item.TryGetProperty("skills", out iEl) ||
                                item.TryGetProperty("values", out iEl))
                            {
                                if (iEl.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var it in iEl.EnumerateArray())
                                    {
                                        var itStr = it.GetString();
                                        if (!string.IsNullOrWhiteSpace(itStr)) items.Add(itStr.Trim());
                                    }
                                }
                                else if (iEl.ValueKind == JsonValueKind.String)
                                {
                                    var itStr = iEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(itStr))
                                        items.AddRange(itStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(cat) && items.Count > 0)
                                skills.Add(new SkillGroup(cat, items));
                        }
                        else if (item.ValueKind == JsonValueKind.String)
                        {
                            var str = item.GetString();
                            if (!string.IsNullOrWhiteSpace(str))
                            {
                                int colon = str.IndexOf(':');
                                if (colon > 0)
                                {
                                    string cat = str[..colon].Trim();
                                    var subItems = str[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                                    if (!string.IsNullOrWhiteSpace(cat) && subItems.Count > 0)
                                        skills.Add(new SkillGroup(cat, subItems));
                                }
                                else
                                {
                                    var existingAddl = skills.FirstOrDefault(s => s.Category.Equals("Additional Skills", StringComparison.OrdinalIgnoreCase));
                                    if (existingAddl is null)
                                        skills.Add(new SkillGroup("Additional Skills", [str.Trim()]));
                                    else
                                        existingAddl.Items.Add(str.Trim());
                                }
                            }
                        }
                    }
                }
                else if (skEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in skEl.EnumerateObject())
                    {
                        var items = new List<string>();
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var it in prop.Value.EnumerateArray())
                            {
                                var itStr = it.GetString();
                                if (!string.IsNullOrWhiteSpace(itStr)) items.Add(itStr.Trim());
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var itStr = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(itStr))
                                items.AddRange(itStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                        }
                        if (items.Count > 0)
                            skills.Add(new SkillGroup(prop.Name, items));
                    }
                }
            }

            var patch = new TailoredPatch(
                Summary:             summaryBullets,
                ExperienceBullets:   indexedExpBullets,
                ProjectBullets:      [],
                Skills:              skills,
                CompanyBullets:      companyBullets,
                NamedProjectBullets: new(StringComparer.OrdinalIgnoreCase)
            );

            int totalExpBullets = companyBullets.Values.Sum(b => b.Count) + indexedExpBullets.Sum(b => b.Count);
            int rolesTailored   = companyBullets.Count + indexedExpBullets.Count;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"│  [LLM]  ✓ Tailored: {summaryBullets.Count} summary bullets, " +
                              $"{rolesTailored} roles customized ({totalExpBullets} bullets), " +
                              $"{skills.Count} skill groups");
            Console.ResetColor();

            return patch;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│  [LLM]  ⚠ Parse error in strict JSON parser: {ex.Message}. Attempting lenient recovery...");
            Console.ResetColor();

            var recovered = LenientExtractPatch(raw, master);
            if (recovered is not null)
                return recovered;

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("│  [LLM]  ⚠ Could not recover patch. Falling back to master.");
            Console.ResetColor();
            return null;
        }
    }

    private static TailoredPatch? LenientExtractPatch(string raw, ResumeModel master)
    {
        try
        {
            var summaryBullets = new List<string>();
            var companyBullets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var indexedExpBullets = new List<List<string>>();
            var skills = new List<SkillGroup>();

            // 1. Extract Summary bullets
            var summaryMatch = Regex.Match(raw, @"""(?:summary|professional_summary|summary_bullets)""\s*:\s*\[([\s\S]*?)\]", RegexOptions.IgnoreCase);
            if (summaryMatch.Success)
            {
                var bulletMatches = Regex.Matches(summaryMatch.Groups[1].Value, @"""((?:\\.|[^""\\])*)""");
                foreach (Match bm in bulletMatches)
                {
                    string b = Regex.Unescape(bm.Groups[1].Value).Trim().TrimStart('•', '-', '*').Trim();
                    if (!string.IsNullOrWhiteSpace(b))
                        summaryBullets.Add(b);
                }
            }

            summaryBullets = EnsureSummaryHasFiveYears(summaryBullets);

            // 2. Extract Experience by company
            var companyBlockMatches = Regex.Matches(raw, @"\{[^{}]*?""company""\s*:\s*""((?:\\.|[^""\\])*)""[^{}]*?""bullets""\s*:\s*\[([\s\S]*?)\][^{}]*?\}", RegexOptions.IgnoreCase);
            foreach (Match cm in companyBlockMatches)
            {
                string company = Regex.Unescape(cm.Groups[1].Value).Trim();
                string bulletsBlock = cm.Groups[2].Value;
                var bulletMatches = Regex.Matches(bulletsBlock, @"""((?:\\.|[^""\\])*)""");
                var bullets = new List<string>();
                foreach (Match bm in bulletMatches)
                {
                    string b = Regex.Unescape(bm.Groups[1].Value).Trim().TrimStart('•', '-', '*').Trim();
                    if (!string.IsNullOrWhiteSpace(b))
                        bullets.Add(b);
                }
                if (!string.IsNullOrWhiteSpace(company) && bullets.Count > 0)
                    companyBullets[company] = bullets;
            }

            // 3. Extract Skills by category
            var skillBlockMatches = Regex.Matches(raw, @"\{[^{}]*?""category""\s*:\s*""((?:\\.|[^""\\])*)""[^{}]*?""items""\s*:\s*\[([\s\S]*?)\][^{}]*?\}", RegexOptions.IgnoreCase);
            foreach (Match sm in skillBlockMatches)
            {
                string cat = Regex.Unescape(sm.Groups[1].Value).Trim();
                string itemsBlock = sm.Groups[2].Value;
                var itemMatches = Regex.Matches(itemsBlock, @"""((?:\\.|[^""\\])*)""");
                var items = new List<string>();
                foreach (Match im in itemMatches)
                {
                    string it = Regex.Unescape(im.Groups[1].Value).Trim();
                    if (!string.IsNullOrWhiteSpace(it))
                        items.Add(it);
                }
                if (!string.IsNullOrWhiteSpace(cat) && items.Count > 0)
                    skills.Add(new SkillGroup(cat, items));
            }

            if (summaryBullets.Count == 0 && companyBullets.Count == 0 && skills.Count == 0)
                return null;

            int totalExpBullets = companyBullets.Values.Sum(b => b.Count);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"│  [LLM]  ✓ Recovered via lenient parser: {summaryBullets.Count} summary bullets, " +
                              $"{companyBullets.Count} roles ({totalExpBullets} bullets), {skills.Count} skill groups");
            Console.ResetColor();

            return new TailoredPatch(
                Summary:             summaryBullets,
                ExperienceBullets:   indexedExpBullets,
                ProjectBullets:      [],
                Skills:              skills,
                CompanyBullets:      companyBullets,
                NamedProjectBullets: new(StringComparer.OrdinalIgnoreCase)
            );
        }
        catch
        {
            return null;
        }
    }

    private static string CleanJobDescription(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string cleaned = Regex.Replace(text, @"\r\n|\r", "\n");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
        // NOTE: No truncation — we need ALL keywords and technologies from the JD.
        // If context windows become an issue, consider compressing whitespace only.
        return cleaned.Trim();
    }

    private static string StripCodeFences(string text)
    {
        return Regex.Replace(text, @"^```[a-zA-Z]*\n?([\s\S]*?)\n?```$",
            "$1", RegexOptions.Multiline).Trim();
    }
}
