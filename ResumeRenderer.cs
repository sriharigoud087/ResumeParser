using System.Text;

namespace ResumeTailor;

/// <summary>
/// Renders a <see cref="ResumeModel"/> to an HTML document that faithfully replicates
/// the typography, structure, and spacing of sample_resume.pdf:
///   - Centered all-caps bold serif name
///   - Contact line with em-dash separators
///   - Title Case section headings with bottom border rule
///   - Technical skills with bullet points and bold category prefixes
///   - Two-column experience headers (Company/Location left-bold, Date right-italic)
///   - Italic job titles and clean bullet points
///   - Environment line with bold prefix
///   - Clean education and certifications sections
/// </summary>
public static class ResumeRenderer
{
    // ── CSS ────────────────────────────────────────────────────────────────────
    // Matches sample_resume.pdf: Times New Roman serif body, clean line-height,
    // tight bullet spacing, and full-width section divider rules.
    private const string CSS = """
        @page {
            size: A4;
            margin: 0.38in 0.45in 0.38in 0.45in;
        }
        *, *::before, *::after {
            box-sizing: border-box;
        }
        html, body {
            margin: 0;
            padding: 0;
            background: #ffffff;
        }
        body {
            font-family: 'Times New Roman', Times, 'Nimbus Roman No9 L', serif;
            font-size: 9.6pt;
            color: #000000;
            line-height: 1.28;
        }

        /* ── Header ── */
        .header {
            text-align: center;
            margin-bottom: 6px;
        }
        .name {
            font-size: 17pt;
            font-weight: 700;
            letter-spacing: 0.02em;
            margin: 0 0 3px 0;
            text-transform: uppercase;
        }
        .contact {
            font-size: 9.2pt;
            margin: 0;
            color: #111;
        }
        .contact a {
            color: inherit;
            text-decoration: none;
        }

        /* ── Section Headings ── */
        .section-heading {
            font-size: 10.8pt;
            font-weight: 700;
            margin: 8px 0 2px 0;
            padding-bottom: 1px;
            border-bottom: 1px solid #000000;
            page-break-after: avoid;
            break-after: avoid;
        }

        /* ── Two-Column Row Tables (Company/Date, Degree/Date) ── */
        table.entry-header {
            width: 100%;
            border-collapse: collapse;
            border: none;
            margin: 4px 0 1px 0;
            page-break-inside: avoid;
            break-inside: avoid;
        }
        table.entry-header td {
            padding: 0;
            border: none;
            vertical-align: baseline;
        }
        td.left-col {
            text-align: left;
            font-weight: 700;
            font-size: 9.8pt;
        }
        td.right-col {
            text-align: right;
            font-style: italic;
            font-size: 9.2pt;
            white-space: nowrap;
        }

        .job-title {
            font-style: italic;
            font-size: 9.2pt;
            margin: 0 0 2px 0;
            page-break-after: avoid;
            break-after: avoid;
        }

        /* ── Bullets ── */
        ul {
            margin: 1.5px 0 3px 0;
            padding-left: 16px;
        }
        li {
            margin-bottom: 1.5px;
            font-size: 9.6pt;
            line-height: 1.28;
        }

        /* ── Skills ── */
        ul.skills-list {
            margin: 1.5px 0 3px 0;
            padding-left: 16px;
        }
        ul.skills-list li {
            margin-bottom: 2px;
        }
        .skill-cat {
            font-weight: 700;
        }

        /* ── Environment ── */
        .exp-env {
            font-size: 9.2pt;
            margin: 2px 0 5px 0;
            line-height: 1.26;
        }
        .exp-env b {
            font-weight: 700;
        }

        /* ── Projects ── */
        .proj-name {
            font-weight: 700;
            font-size: 9.8pt;
            margin: 5px 0 1px 0;
            page-break-after: avoid;
            break-after: avoid;
        }

        /* ── Education & Certifications ── */
        .edu-notes {
            font-size: 9.5pt;
            margin: 0 0 4px 0;
        }
        .cert-item {
            font-size: 10pt;
            margin: 4px 0;
        }
        """;

    /// <summary>Alias for <see cref="RenderMarkdown"/> for backwards compatibility.</summary>
    public static string Render(ResumeModel m) => RenderMarkdown(m);

    /// <summary>Returns a complete HTML document matching sample_resume.pdf styling.</summary>
    public static string RenderHtml(ResumeModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <title>{H(m.Name)} - Resume</title>
                <style>{CSS}</style>
            </head>
            <body>
            """);

        AppendHeaderHtml(sb, m);
        AppendSummaryHtml(sb, m);
        AppendSkillsHtml(sb, m);
        AppendExperienceHtml(sb, m);
        AppendProjectsHtml(sb, m);
        AppendEducationHtml(sb, m);
        AppendCertificationsHtml(sb, m);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Returns clean Markdown for saving to .md files.
    /// </summary>
    public static string RenderMarkdown(ResumeModel m)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"# {m.Name}");
        var cParts = new[] { m.Contact.Email, m.Contact.Phone, m.Contact.LinkedIn, m.Contact.Location }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        sb.AppendLine(string.Join(" — ", cParts));
        sb.AppendLine();

        if (m.Summary.Count > 0)
        {
            sb.AppendLine("## Professional Summary");
            sb.AppendLine();
            foreach (var b in m.Summary)
                sb.AppendLine($"- {b}");
            sb.AppendLine();
        }

        if (m.Skills.Count > 0)
        {
            sb.AppendLine("## Technical Skills");
            sb.AppendLine();
            foreach (var g in m.Skills.Where(g => g.Items.Count > 0))
                sb.AppendLine($"- **{g.Category}:** {string.Join(", ", g.Items)}");
            sb.AppendLine();
        }

        if (m.Experience.Count > 0)
        {
            sb.AppendLine("## Professional Experience");
            sb.AppendLine();
            foreach (var job in m.Experience)
            {
                var loc = string.IsNullOrWhiteSpace(job.Location) ? "" : $" – {job.Location}";
                sb.AppendLine($"### {job.Company}{loc} | *{job.DateRange}*");
                sb.AppendLine($"*{job.Title}*");
                sb.AppendLine();
                foreach (var b in job.Bullets)
                    sb.AppendLine($"- {b}");
                if (!string.IsNullOrWhiteSpace(job.Environment))
                    sb.AppendLine($"**Environment:** {job.Environment}");
                sb.AppendLine();
            }
        }

        if (m.Projects.Count > 0)
        {
            sb.AppendLine("## Projects");
            sb.AppendLine();
            foreach (var p in m.Projects)
            {
                sb.AppendLine($"### {p.Name}");
                if (!string.IsNullOrWhiteSpace(p.Technologies))
                    sb.AppendLine($"*{p.Technologies}*");
                sb.AppendLine();
                foreach (var b in p.Bullets)
                    sb.AppendLine($"- {b}");
                sb.AppendLine();
            }
        }

        if (m.Education.Count > 0)
        {
            sb.AppendLine("## Education");
            sb.AppendLine();
            foreach (var e in m.Education)
            {
                sb.AppendLine($"### {e.Degree} – {e.School} | *{e.DateRange}*");
                if (!string.IsNullOrWhiteSpace(e.Notes))
                    sb.AppendLine(e.Notes);
                sb.AppendLine();
            }
        }

        var certs = m.Certifications.Count > 0 ? m.Certifications : m.Honors;
        if (certs.Count > 0)
        {
            sb.AppendLine("## Certifications");
            sb.AppendLine();
            foreach (var c in certs)
                sb.AppendLine($"- {c}");
        }

        return sb.ToString().TrimEnd();
    }

    // ── HTML Helpers ──────────────────────────────────────────────────────────

    private static void AppendHeaderHtml(StringBuilder sb, ResumeModel m)
    {
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine($"  <div class=\"name\">{H(m.Name)}</div>");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.Contact.Email))
            parts.Add($"<a href=\"mailto:{H(m.Contact.Email)}\">{H(m.Contact.Email)}</a>");
        if (!string.IsNullOrWhiteSpace(m.Contact.Phone))
            parts.Add(H(m.Contact.Phone));
        if (!string.IsNullOrWhiteSpace(m.Contact.LinkedIn))
        {
            var url = m.Contact.LinkedIn.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? m.Contact.LinkedIn
                : $"https://{m.Contact.LinkedIn}";
            parts.Add($"<a href=\"{H(url)}\">{H(m.Contact.LinkedIn)}</a>");
        }

        sb.AppendLine($"  <div class=\"contact\">{string.Join(" &mdash; ", parts)}</div>");
        sb.AppendLine("</div>");
    }

    private static void AppendSummaryHtml(StringBuilder sb, ResumeModel m)
    {
        if (m.Summary.Count == 0) return;
        sb.AppendLine("<div class=\"section-heading\">Professional Summary</div>");
        sb.AppendLine("<ul>");
        foreach (var bullet in m.Summary)
            sb.AppendLine($"  <li>{H(bullet)}</li>");
        sb.AppendLine("</ul>");
    }

    private static void AppendSkillsHtml(StringBuilder sb, ResumeModel m)
    {
        if (m.Skills.Count == 0) return;
        sb.AppendLine("<div class=\"section-heading\">Technical Skills</div>");
        sb.AppendLine("<ul class=\"skills-list\">");
        foreach (var g in m.Skills.Where(g => g.Items.Count > 0))
        {
            sb.AppendLine($"  <li><span class=\"skill-cat\">{H(g.Category)}:</span> {H(string.Join(", ", g.Items))}</li>");
        }
        sb.AppendLine("</ul>");
    }

    private static void AppendExperienceHtml(StringBuilder sb, ResumeModel m)
    {
        if (m.Experience.Count == 0) return;
        sb.AppendLine("<div class=\"section-heading\">Professional Experience</div>");
        foreach (var job in m.Experience)
        {
            var companyAndLoc = string.IsNullOrWhiteSpace(job.Location)
                ? H(job.Company)
                : $"{H(job.Company)} &ndash; {H(job.Location)}";

            sb.AppendLine($"""
                <table class="entry-header">
                  <tr>
                    <td class="left-col">{companyAndLoc}</td>
                    <td class="right-col">{H(job.DateRange)}</td>
                  </tr>
                </table>
                <div class="job-title">{H(job.Title)}</div>
                """);

            if (job.Bullets.Count > 0)
            {
                sb.AppendLine("<ul>");
                foreach (var b in job.Bullets)
                    sb.AppendLine($"  <li>{H(b)}</li>");
                sb.AppendLine("</ul>");
            }

            if (!string.IsNullOrWhiteSpace(job.Environment))
                sb.AppendLine($"<div class=\"exp-env\"><b>Environment:</b> {H(job.Environment)}</div>");
        }
    }

    private static void AppendProjectsHtml(StringBuilder sb, ResumeModel m)
    {
        if (m.Projects.Count == 0) return;
        sb.AppendLine("<div class=\"section-heading\">Projects</div>");
        foreach (var proj in m.Projects)
        {
            sb.AppendLine($"<div class=\"proj-name\">{H(proj.Name)}</div>");
            if (proj.Bullets.Count > 0)
            {
                sb.AppendLine("<ul>");
                foreach (var b in proj.Bullets)
                    sb.AppendLine($"  <li>{H(b)}</li>");
                sb.AppendLine("</ul>");
            }
        }
    }

    private static void AppendEducationHtml(StringBuilder sb, ResumeModel m)
    {
        if (m.Education.Count == 0) return;
        sb.AppendLine("<div class=\"section-heading\">Education</div>");
        foreach (var edu in m.Education)
        {
            var degreeSchool = string.IsNullOrWhiteSpace(edu.School)
                ? H(edu.Degree)
                : $"{H(edu.Degree)} &ndash; {H(edu.School)}";

            sb.AppendLine($"""
                <table class="entry-header">
                  <tr>
                    <td class="left-col">{degreeSchool}</td>
                    <td class="right-col">{H(edu.DateRange)}</td>
                  </tr>
                </table>
                """);

            if (!string.IsNullOrWhiteSpace(edu.Notes))
                sb.AppendLine($"<div class=\"edu-notes\">{H(edu.Notes)}</div>");
        }
    }

    private static void AppendCertificationsHtml(StringBuilder sb, ResumeModel m)
    {
        var certs = m.Certifications.Count > 0 ? m.Certifications : m.Honors;
        if (certs.Count == 0) return;
        sb.AppendLine("<div class=\"section-heading\">Certifications</div>");
        foreach (var c in certs)
            sb.AppendLine($"<div class=\"cert-item\">{H(c)}</div>");
    }

    private static string H(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
