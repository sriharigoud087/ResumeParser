// JdKeywordInjector.cs
// Deterministic post-processor that extracts every technology / keyword from the
// job description and injects any missing ones directly into the resume skill groups.

using System.Text.RegularExpressions;

namespace ResumeTailor;

public static class JdKeywordInjector
{
    // (display name, category bucket, regex pattern to match in JD)
    private static readonly (string Display, string Category, string Pattern)[] KnownTech =
    [
        // Programming Languages
        ("Java",            "Programming Languages",          @"\bJava\b"),
        ("Go",              "Programming Languages",          @"\bGo(?:lang)?\b"),
        ("Rust",            "Programming Languages",          @"\bRust\b"),
        ("Kotlin",          "Programming Languages",          @"\bKotlin\b"),
        ("Scala",           "Programming Languages",          @"\bScala\b"),
        ("Ruby",            "Programming Languages",          @"\bRuby\b"),
        ("Swift",           "Programming Languages",          @"\bSwift\b"),
        ("PHP",             "Programming Languages",          @"\bPHP\b"),
        ("Bash",            "Programming Languages",          @"\bBash\b"),
        ("Shell Scripting", "Programming Languages",          @"\bShell(?:\s+scripting)?\b"),
        // Web / Frontend
        ("Node.js",         "Web Technologies",               @"\bNode(?:\.js|JS)?\b"),
        ("Next.js",         "Web Technologies",               @"\bNext(?:\.js|JS)?\b"),
        ("Vue",             "Web Technologies",               @"\bVue(?:\.js|JS)?\b"),
        ("GraphQL",         "Backend & Frameworks",           @"\bGraphQL\b"),
        ("gRPC",            "Backend & Frameworks",           @"\bgRPC\b"),
        // Backend / Frameworks
        ("Spring Boot",     "Backend & Frameworks",           @"\bSpring\s+Boot\b"),
        ("Spring",          "Backend & Frameworks",           @"\bSpring\b"),
        ("Django",          "Backend & Frameworks",           @"\bDjango\b"),
        ("Flask",           "Backend & Frameworks",           @"\bFlask\b"),
        ("FastAPI",         "Backend & Frameworks",           @"\bFastAPI\b"),
        // Databases
        ("DynamoDB",        "Databases",                      @"\bDynamoDB\b"),
        ("Elasticsearch",   "Databases",                      @"\bElasticsearch\b"),
        ("Aurora",          "Databases",                      @"\bAurora\b"),
        ("Cassandra",       "Databases",                      @"\bCassandra\b"),
        ("Neo4j",           "Databases",                      @"\bNeo4j\b"),
        ("ClickHouse",      "Databases",                      @"\bClickHouse\b"),
        // AWS Services
        ("AWS",             "Cloud Platforms",                @"\bAWS\b"),
        ("Amazon EC2",      "Cloud Platforms",                @"\bEC2\b"),
        ("Amazon S3",       "Cloud Platforms",                @"\bS3\b"),
        ("AWS Lambda",      "Cloud Platforms",                @"\bLambda\b"),
        ("AWS ECS",         "Cloud Platforms",                @"\bECS\b"),
        ("AWS EKS",         "Cloud Platforms",                @"\bEKS\b"),
        ("AWS Fargate",     "Cloud Platforms",                @"\bFargate\b"),
        ("AWS CDK",         "Cloud Platforms",                @"\bCDK\b"),
        ("AWS CloudFormation","Cloud Platforms",              @"\bCloudFormation\b"),
        ("AWS SQS",         "Cloud Platforms",                @"\bSQS\b"),
        ("AWS SNS",         "Cloud Platforms",                @"\bSNS\b"),
        ("AWS RDS",         "Cloud Platforms",                @"\bRDS\b"),
        ("AWS CloudWatch",  "Cloud Platforms",                @"\bCloudWatch\b"),
        ("AWS IAM",         "Cloud Platforms",                @"\bIAM\b"),
        ("AWS VPC",         "Cloud Platforms",                @"\bVPC\b"),
        ("AWS Route 53",    "Cloud Platforms",                @"\bRoute\s+53\b"),
        ("AWS API Gateway", "Cloud Platforms",                @"\bAPI\s+Gateway\b"),
        ("AWS Glue",        "Cloud Platforms",                @"\bAWS\s+Glue\b"),
        ("AWS Kinesis",     "Cloud Platforms",                @"\bKinesis\b"),
        ("AWS Step Functions","Cloud Platforms",              @"\bStep\s+Functions\b"),
        ("AWS Secrets Manager","Cloud Platforms",             @"\bSecrets\s+Manager\b"),
        ("AWS CodeBuild",   "Cloud Platforms",                @"\bCodeBuild\b"),
        ("AWS CodeDeploy",  "Cloud Platforms",                @"\bCodeDeploy\b"),
        ("AWS Elastic Beanstalk","Cloud Platforms",           @"\bElastic\s+Beanstalk\b"),
        ("AWS Cognito",     "Cloud Platforms",                @"\bCognito\b"),
        ("GCP",             "Cloud Platforms",                @"\bGCP\b|\bGoogle\s+Cloud\b"),
        ("Azure",           "Cloud Platforms",                @"\bAzure\b"),
        // DevOps
        ("Terraform",       "DevOps & ORM Tools",             @"\bTerraform\b"),
        ("Ansible",         "DevOps & ORM Tools",             @"\bAnsible\b"),
        ("Helm",            "DevOps & ORM Tools",             @"\bHelm\b"),
        ("Pulumi",          "DevOps & ORM Tools",             @"\bPulumi\b"),
        ("Jenkins",         "DevOps & ORM Tools",             @"\bJenkins\b"),
        ("CircleCI",        "DevOps & ORM Tools",             @"\bCircleCI\b"),
        ("GitOps",          "DevOps & ORM Tools",             @"\bGitOps\b"),
        // Messaging / Streaming
        ("Apache Spark",    "Messaging & Distributed Systems",@"\bSpark\b"),
        ("Apache Flink",    "Messaging & Distributed Systems",@"\bFlink\b"),
        // AI / ML
        ("AI",              "Artificial Intelligence and Machine Learning", @"\bAI\b|\bArtificial\s+Intelligence\b"),
        ("Machine Learning","Artificial Intelligence and Machine Learning", @"\bMachine\s+Learning\b|\bML\b"),
        ("LLM",             "Artificial Intelligence and Machine Learning", @"\bLLM\b|\bLarge\s+Language\s+Model\b"),
        ("PyTorch",         "Artificial Intelligence and Machine Learning", @"\bPyTorch\b"),
        ("TensorFlow",      "Artificial Intelligence and Machine Learning", @"\bTensorFlow\b"),
        ("Hugging Face",    "Artificial Intelligence and Machine Learning", @"\bHugging\s+Face\b"),
        ("OpenAI",          "Artificial Intelligence and Machine Learning", @"\bOpenAI\b"),
        ("SageMaker",       "Artificial Intelligence and Machine Learning", @"\bSageMaker\b"),
        // Observability
        ("Prometheus",      "Monitoring & Observability",     @"\bPrometheus\b"),
        ("Grafana",         "Monitoring & Observability",     @"\bGrafana\b"),
        ("Datadog",         "Monitoring & Observability",     @"\bDatadog\b"),
        ("OpenTelemetry",   "Monitoring & Observability",     @"\bOpenTelemetry\b"),
        ("AWS X-Ray",       "Monitoring & Observability",     @"\bX-Ray\b"),
        // Architecture
        ("Serverless",      "Architecture & Design",          @"\bServerless\b"),
        ("Service Mesh",    "Architecture & Design",          @"\bService\s+Mesh\b"),
        ("Istio",           "Architecture & Design",          @"\bIstio\b"),
        ("Event-Driven Architecture","Architecture & Design", @"\bEvent.Driven\b"),
        // Methodologies
        ("Agile",           "Methodologies",                  @"\bAgile\b"),
        ("Scrum",           "Methodologies",                  @"\bScrum\b"),
        ("DevOps",          "Methodologies",                  @"\bDevOps\b"),
        ("SRE",             "Methodologies",                  @"\bSRE\b|\bSite\s+Reliability\b"),
    ];

    /// <summary>
    /// Scans the job description for every technology in KnownTech and injects
    /// any that are missing from the resume skill groups into the right category.
    /// Purely deterministic — no LLM.
    /// </summary>
    public static ResumeModel InjectJdKeywords(string jobDescription, ResumeModel resume)
    {
        if (string.IsNullOrWhiteSpace(jobDescription))
            return resume;

        var jdMatches = KnownTech
            .Where(t => Regex.IsMatch(jobDescription, t.Pattern, RegexOptions.IgnoreCase))
            .ToList();

        if (jdMatches.Count == 0)
            return resume;

        var existingSkills = new HashSet<string>(
            resume.Skills.SelectMany(g => g.Items),
            StringComparer.OrdinalIgnoreCase);

        var missing = jdMatches
            .Where(t => !existingSkills.Contains(t.Display))
            .ToList();

        if (missing.Count == 0)
            return resume;

        var byCategory = missing
            .GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Display).ToList(), StringComparer.OrdinalIgnoreCase);

        var newSkills = resume.Skills.Select(group =>
        {
            if (byCategory.TryGetValue(group.Category, out var additions))
            {
                byCategory.Remove(group.Category);
                var combined = new List<string>(group.Items);
                foreach (var item in additions)
                    if (!combined.Any(x => x.Equals(item, StringComparison.OrdinalIgnoreCase)))
                        combined.Add(item);
                return group with { Items = combined };
            }
            return group;
        }).ToList();

        foreach (var (cat, items) in byCategory)
            if (items.Count > 0)
                newSkills.Add(new SkillGroup(cat, items));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"│  [JDI]  + Injected {missing.Count} JD keyword(s) into skills: {string.Join(", ", missing.Select(t => t.Display))}");
        Console.ResetColor();

        return resume with { Skills = newSkills };
    }

    /// <summary>
    /// Merges LLM-generated bullets WITH the master original bullets so no
    /// existing bullet is ever lost. Master bullets come first, then any
    /// genuinely new LLM bullets are appended (near-duplicate deduplication).
    /// </summary>
    public static ResumeModel MergeExistingBullets(ResumeModel tailored, ResumeModel master)
    {
        var mergedExperience = tailored.Experience.Select((tailoredEntry, i) =>
        {
            var masterEntry = master.Experience.FirstOrDefault(m =>
                NormalizeCompany(m.Company).Contains(NormalizeCompany(tailoredEntry.Company), StringComparison.OrdinalIgnoreCase) ||
                NormalizeCompany(tailoredEntry.Company).Contains(NormalizeCompany(m.Company), StringComparison.OrdinalIgnoreCase));

            masterEntry ??= i < master.Experience.Count ? master.Experience[i] : null;

            if (masterEntry is null || masterEntry.Bullets.Count == 0)
                return tailoredEntry;

            // Always start with every original master bullet
            var merged = new List<string>(masterEntry.Bullets);

            // Append any genuinely new LLM bullets that are not near-duplicates
            foreach (var llmBullet in tailoredEntry.Bullets)
                if (!IsNearDuplicate(llmBullet, merged))
                    merged.Add(llmBullet);

            return tailoredEntry with { Bullets = merged };
        }).ToList();

        return tailored with { Experience = mergedExperience };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NormalizeCompany(string name) =>
        Regex.Replace(name, @"\b(?:Hyderabad|India|USA|Remote|LLC|LLP|Ltd|Inc|Corp|Pvt)\b|Client:.*|[,\(\)]",
            "", RegexOptions.IgnoreCase).Trim();

    private static bool IsNearDuplicate(string candidate, IEnumerable<string> existing)
    {
        var candWords = TokenizeWords(candidate);
        if (candWords.Count == 0) return true;
        foreach (var ex in existing)
        {
            var exWords = TokenizeWords(ex);
            if (exWords.Count == 0) continue;
            int shared = candWords.Count(w => exWords.Contains(w));
            double overlap = (double)shared / Math.Max(candWords.Count, exWords.Count);
            if (overlap >= 0.60) return true;
        }
        return false;
    }

    private static HashSet<string> TokenizeWords(string text) =>
        new(Regex.Matches(text.ToLowerInvariant(), @"\b[a-z]{3,}\b")
            .Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
}
