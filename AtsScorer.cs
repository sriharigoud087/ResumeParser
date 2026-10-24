using System.Text.RegularExpressions;

namespace ResumeTailor;

public record AtsScoreResult(
    int          ScorePercentage,
    List<string> MatchedKeywords,
    List<string> MissingKeywords,
    string       MatchRating);

/// <summary>
/// Evaluates the ATS (Applicant Tracking System) keyword match score between
/// a target job description and the tailored resume.
/// </summary>
public static class AtsScorer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "while", "with", "at", "by", "for",
        "of", "on", "to", "in", "out", "over", "under", "again", "further", "then",
        "once", "here", "there", "when", "where", "why", "how", "all", "any", "both",
        "each", "few", "more", "most", "other", "some", "such", "no", "nor", "not",
        "only", "own", "same", "so", "than", "too", "very", "can", "will", "just",
        "should", "now", "job", "work", "role", "team", "years", "year", "candidate",
        "experience", "ability", "skills", "required", "preferred", "responsibilities",
        "qualifications", "description", "opportunity", "company", "location", "apply",
        "status", "equal", "employment", "benefits", "salary", "including", "across",
        "proven", "demonstrated", "looking", "join", "working", "degree", "bachelor", "master"
    };

    // Well-known industry technical keywords to prioritize in ATS matching
    private static readonly HashSet<string> TechDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        "C#", ".NET", ".NET Core", "ASP.NET", "ASP.NET Core", "Web API", "Entity Framework", "EF Core",
        "LINQ", "WPF", "WCF", "MVVM", "Microservices", "REST", "RESTful", "REST APIs", "GraphQL", "gRPC",
        "SQL", "SQL Server", "T-SQL", "PL/SQL", "PostgreSQL", "MySQL", "Oracle", "MongoDB", "Cosmos DB", "Redis",
        "React", "Redux", "Angular", "NgRx", "Vue", "TypeScript", "JavaScript", "HTML5", "CSS3", "Blazor",
        "Swift", "SwiftUI", "Objective-C", "iOS", "iOS SDK", "UIKit", "Combine", "Xcode", "CocoaPods", "CoreData",
        "Python", "Java", "C++", "C", "Go", "Golang", "Rust", "Kotlin", "Android",
        "Azure", "AWS", "GCP", "Google Cloud", "Docker", "Kubernetes", "AKS", "EKS", "ECS", "Lambda",
        "Azure DevOps", "GitHub", "GitLab", "GitHub Actions", "CI/CD", "Pipelines", "Terraform", "IIS", "PowerShell",
        "Kafka", "RabbitMQ", "ActiveMQ", "Azure Service Bus", "Amazon SQS", "Event-Driven", "CQRS",
        "OAuth", "OAuth2", "OAuth 2.0", "JWT", "SAML", "SAML 2.0", "Azure AD", "Active Directory", "RBAC", "SSO",
        "NUnit", "xUnit", "MSTest", "Jest", "Jasmine", "Karma", "Selenium", "Cypress", "Postman", "Swagger", "TDD",
        "Clean Architecture", "SOLID", "Dependency Injection", "Repository Pattern", "Design Patterns",
        "Ollama", "OllamaSharp", "LLM", "Local LLMs", "RAG", "Retrieval-Augmented Generation", "Vector Database",
        "Embeddings", "PyTorch", "Hugging Face", "Prompt Engineering", "Fine-Tuning", "AI", "ML", "SDK", "API",
        "Splunk", "Application Insights", "CloudWatch", "Prometheus", "Grafana", "DataDog",
        "Agile", "Scrum", "Kanban", "Sprint Planning", "Jira", "Azure Boards", "SDLC"
    };

    /// <summary>
    /// Computes the ATS score between a job description and the tailored resume.
    /// </summary>
    public static AtsScoreResult ScoreResume(string jobDescription, ResumeModel resume)
    {
        string resumeText = ExtractFullResumeText(resume);
        return CalculateScore(jobDescription, resumeText);
    }

    /// <summary>
    /// Computes the ATS score between a job description and raw resume text (Markdown or plain text).
    /// </summary>
    public static AtsScoreResult CalculateScore(string jobDescription, string resumeText)
    {
        var targetKeywords = ExtractTargetKeywords(jobDescription);
        if (targetKeywords.Count == 0)
        {
            return new AtsScoreResult(100, [], [], "N/A");
        }

        var matched = new List<string>();
        var missing = new List<string>();

        foreach (var keyword in targetKeywords)
        {
            if (ContainsKeyword(resumeText, keyword))
                matched.Add(keyword);
            else
                missing.Add(keyword);
        }

        int score = (int)Math.Round((double)matched.Count / targetKeywords.Count * 100);
        score = Math.Clamp(score, 0, 100);

        string rating = score switch
        {
            >= 90 => "Excellent (90%+ match)",
            >= 80 => "Strong Match (80-89%)",
            >= 70 => "Good Match (70-79%)",
            >= 60 => "Moderate Match (60-69%)",
            _     => "Needs Improvement (<60%)"
        };

        return new AtsScoreResult(score, matched, missing, rating);
    }

    private static List<string> ExtractTargetKeywords(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Match known high-value industry tech keywords from dictionary
        foreach (var tech in TechDictionary)
        {
            if (ContainsKeyword(text, tech))
                found.Add(tech);
        }

        return found.OrderBy(x => x).ToList();
    }

    private static bool ContainsKeyword(string source, string keyword)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(keyword))
            return false;

        // Special handling for keywords with symbols like C#, .NET, C++, etc.
        string pattern = keyword switch
        {
            "C#"           => @"(?i)(?:^|[\s,;/\(])C#(?=[\s,;/\)]|$)",
            ".NET"         => @"(?i)(?:^|[\s,;/\(])\.NET(?=[\s,;/\)]|$)",
            ".NET Core"    => @"(?i)\.NET\s+Core",
            "ASP.NET"      => @"(?i)ASP\.NET",
            "ASP.NET Core" => @"(?i)ASP\.NET\s+Core",
            "C++"          => @"(?i)(?:^|[\s,;/\(])C\+\+(?=[\s,;/\)]|$)",
            "CI/CD"        => @"(?i)CI/CD",
            _              => $@"(?i)\b{Regex.Escape(keyword)}\b"
        };

        return Regex.IsMatch(source, pattern);
    }

    private static string ExtractFullResumeText(ResumeModel r)
    {
        var parts = new List<string> { r.Name };

        parts.AddRange(r.Summary);

        foreach (var s in r.Skills)
        {
            parts.Add(s.Category);
            parts.AddRange(s.Items);
        }

        foreach (var exp in r.Experience)
        {
            parts.Add(exp.Company);
            parts.Add(exp.Title);
            parts.AddRange(exp.Bullets);
            if (!string.IsNullOrWhiteSpace(exp.Environment))
                parts.Add(exp.Environment);
        }

        foreach (var proj in r.Projects)
        {
            parts.Add(proj.Name);
            parts.AddRange(proj.Bullets);
        }

        foreach (var edu in r.Education)
        {
            parts.Add(edu.School);
            parts.Add(edu.Degree);
        }

        parts.AddRange(r.Certifications);
        parts.AddRange(r.Honors);

        return string.Join(" ", parts);
    }
}
