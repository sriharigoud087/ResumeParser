// ResumeModel.cs — Strongly-typed data model for the structured resume.
// The renderer (ResumeRenderer.cs) owns all formatting; only the *values*
// inside this model are ever changed by the LLM.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ResumeTailor;

/// <summary>
/// Converts either a JSON array of strings or a single multi-line string into List&lt;string&gt;.
/// </summary>
public class StringListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str)) return [];
            return str.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim().TrimStart('•', '-', '*').Trim())
                      .Where(s => !string.IsNullOrWhiteSpace(s))
                      .ToList();
        }
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    var val = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) list.Add(val);
                }
            }
            return list;
        }
        return [];
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Deserializes skills from either an array of objects [ { category, items } ]
/// or a dictionary { "category": ["item1", ...] } to handle variations from the LLM.
/// </summary>
public class SkillGroupListConverter : JsonConverter<List<SkillGroup>>
{
    public override List<SkillGroup> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<SkillGroup>();

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var items = new List<string>();
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        var str = item.GetString();
                        if (!string.IsNullOrWhiteSpace(str)) items.Add(str);
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var str = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        items.AddRange(str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
                    }
                }
                result.Add(new SkillGroup(prop.Name, items));
            }
            return result;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object)
                {
                    string cat = "";
                    var items = new List<string>();
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (prop.Name.Equals("category", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("group", StringComparison.OrdinalIgnoreCase))
                        {
                            cat = prop.Value.GetString() ?? "";
                        }
                        else if (prop.Name.Equals("items", StringComparison.OrdinalIgnoreCase) ||
                                 prop.Name.Equals("skills", StringComparison.OrdinalIgnoreCase) ||
                                 prop.Name.Equals("values", StringComparison.OrdinalIgnoreCase))
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in prop.Value.EnumerateArray())
                                {
                                    var str = item.GetString();
                                    if (!string.IsNullOrWhiteSpace(str)) items.Add(str);
                                }
                            }
                            else if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                var str = prop.Value.GetString();
                                if (!string.IsNullOrWhiteSpace(str))
                                {
                                    items.AddRange(str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
                                }
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(cat) || items.Count > 0)
                        result.Add(new SkillGroup(cat, items));
                }
            }
            return result;
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<SkillGroup> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var group in value)
        {
            writer.WriteStartObject();
            writer.WriteString("category", group.Category);
            writer.WriteStartArray("items");
            foreach (var item in group.Items) writer.WriteStringValue(item);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}

public record ContactInfo(
    string Email    = "",
    string Phone    = "",
    string LinkedIn = "",
    string Location = "");

public record ExperienceEntry(
    string       Title       = "",
    string       Company     = "",
    string       DateRange   = "",
    string       Location    = "",
    string       Environment = "",
    List<string> Bullets     = null!)
{
    [JsonConverter(typeof(StringListConverter))]
    public List<string> Bullets { get; init; } = Bullets ?? [];
}

public record EducationEntry(
    string Degree    = "",
    string School    = "",
    string DateRange = "",
    string Notes     = "");

public record SkillGroup(
    string       Category = "",
    List<string> Items    = null!)
{
    [JsonConverter(typeof(StringListConverter))]
    public List<string> Items { get; init; } = Items ?? [];
}

public record ProjectEntry(
    string       Name         = "",
    string       Technologies = "",
    List<string> Bullets      = null!)
{
    [JsonConverter(typeof(StringListConverter))]
    public List<string> Bullets { get; init; } = Bullets ?? [];
}

/// <summary>
/// The full structured resume. All presentation details live in ResumeRenderer.
/// </summary>
public record ResumeModel(
    string                Name           = "",
    ContactInfo           Contact        = null!,
    List<string>          Summary        = null!,
    List<ExperienceEntry> Experience     = null!,
    List<EducationEntry>  Education      = null!,
    List<SkillGroup>      Skills         = null!,
    List<ProjectEntry>    Projects       = null!,
    List<string>          Certifications = null!,
    List<string>          Honors         = null!)
{
    public ContactInfo           Contact        { get; init; } = Contact        ?? new();
    [JsonConverter(typeof(StringListConverter))]
    public List<string>          Summary        { get; init; } = Summary        ?? [];
    public List<ExperienceEntry> Experience     { get; init; } = Experience     ?? [];
    public List<EducationEntry>  Education      { get; init; } = Education      ?? [];
    [JsonConverter(typeof(SkillGroupListConverter))]
    public List<SkillGroup>      Skills         { get; init; } = Skills         ?? [];
    public List<ProjectEntry>    Projects       { get; init; } = Projects       ?? [];
    [JsonConverter(typeof(StringListConverter))]
    public List<string>          Certifications { get; init; } = Certifications ?? [];
    [JsonConverter(typeof(StringListConverter))]
    public List<string>          Honors         { get; init; } = Honors         ?? [];
}

// ── LLM patch ─────────────────────────────────────────────────────────────

/// <summary>
/// The JSON structure the LLM returns. Only contains the parts it may change.
/// Indexed lists correspond 1-to-1 with <see cref="ResumeModel.Experience"/>
/// and <see cref="ResumeModel.Projects"/> arrays.
/// </summary>
public record TailoredPatch(
    List<string>                           Summary             = null!,
    List<List<string>>                     ExperienceBullets   = null!,
    List<List<string>>                     ProjectBullets      = null!,
    List<SkillGroup>                       Skills              = null!,
    Dictionary<string, List<string>>       CompanyBullets      = null!,
    Dictionary<string, List<string>>       NamedProjectBullets = null!)
{
    [JsonConverter(typeof(StringListConverter))]
    public List<string>                     Summary             { get; init; } = Summary             ?? [];
    public List<List<string>>               ExperienceBullets   { get; init; } = ExperienceBullets   ?? [];
    public List<List<string>>               ProjectBullets      { get; init; } = ProjectBullets      ?? [];
    [JsonConverter(typeof(SkillGroupListConverter))]
    public List<SkillGroup>                 Skills              { get; init; } = Skills              ?? [];
    public Dictionary<string, List<string>> CompanyBullets      { get; init; } = CompanyBullets      ?? new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> NamedProjectBullets { get; init; } = NamedProjectBullets ?? new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Applies this patch over a master model and returns a new tailored model.</summary>
    public ResumeModel ApplyTo(ResumeModel master)
    {
        var experience = master.Experience.Select((e, i) =>
        {
            // First check CompanyBullets dictionary by normalized company name (stripping location suffixes)
            if (CompanyBullets.Count > 0)
            {
                static string CleanComp(string name) => Regex.Replace(name, @"\b(?:Hyderabad|India|USA|Remote|Client:.*)\b|[,\(\)]", "", RegexOptions.IgnoreCase).Trim();

                string cleanMaster = CleanComp(e.Company);
                var match = CompanyBullets.FirstOrDefault(kv =>
                {
                    string cleanKey = CleanComp(kv.Key);
                    if (cleanMaster.Length > 2 && cleanKey.Length > 2)
                    {
                        if (cleanMaster.Contains(cleanKey, StringComparison.OrdinalIgnoreCase) ||
                            cleanKey.Contains(cleanMaster, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;
                });

                if (match.Value is not null && match.Value.Count > 0)
                    return e with { Bullets = match.Value };
            }

            // Fallback to indexed array in ExperienceBullets
            if (i < ExperienceBullets.Count && ExperienceBullets[i].Count > 0)
                return e with { Bullets = ExperienceBullets[i] };

            return e;
        }).ToList();

        var projects = master.Projects.Select((p, i) =>
        {
            if (NamedProjectBullets.Count > 0)
            {
                var match = NamedProjectBullets.FirstOrDefault(kv =>
                    p.Name.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Contains(p.Name, StringComparison.OrdinalIgnoreCase));

                if (match.Value is not null && match.Value.Count > 0)
                    return p with { Bullets = match.Value };
            }

            if (i < ProjectBullets.Count && ProjectBullets[i].Count > 0)
                return p with { Bullets = ProjectBullets[i] };

            return p;
        }).ToList();

        // Merge skills: retain all master categories and inject tailored skills without duplicating
        List<SkillGroup> skills;
        if (Skills.Count == 0)
        {
            skills = master.Skills;
        }
        else
        {
            var merged = new List<SkillGroup>();
            var patchMap = Skills.ToDictionary(s => s.Category.Trim(), s => s.Items, StringComparer.OrdinalIgnoreCase);

            foreach (var masterGroup in master.Skills)
            {
                string catName = masterGroup.Category.Trim();
                var patchEntry = patchMap.FirstOrDefault(kv =>
                    catName.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Contains(catName, StringComparison.OrdinalIgnoreCase));

                if (patchEntry.Value is not null && patchEntry.Value.Count > 0)
                {
                    var combined = new List<string>(masterGroup.Items);
                    foreach (var item in patchEntry.Value)
                    {
                        if (!combined.Any(existing => existing.Equals(item, StringComparison.OrdinalIgnoreCase)))
                            combined.Add(item);
                    }
                    merged.Add(new SkillGroup(masterGroup.Category, combined));
                    patchMap.Remove(patchEntry.Key);
                }
                else
                {
                    merged.Add(masterGroup);
                }
            }

            foreach (var remaining in patchMap)
            {
                if (remaining.Value.Count > 0)
                    merged.Add(new SkillGroup(remaining.Key, remaining.Value));
            }

            skills = merged;
        }

        var summary = Summary.Count > 0 ? Summary : master.Summary;
        summary = LlmService.EnsureSummaryHasFiveYears(summary);

        return master with
        {
            Summary    = summary,
            Experience = experience,
            Projects   = projects,
            Skills     = skills,
        };
    }
}
