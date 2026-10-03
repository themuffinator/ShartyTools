using System.Text;

namespace ShartyTools.Core.Formats;

public sealed record Entity(IReadOnlyDictionary<string, string> Properties, IReadOnlyList<string>? RepeatedKeys = null)
{
    public string this[string key] => Properties.GetValueOrDefault(key, "");
}

public static class EntityText
{
    public static IReadOnlyList<Entity> Parse(string text)
    {
        var position = 0;
        string? Token()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position]) || text[position] == '\0') { position++; continue; }
                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
                {
                    while (position < text.Length && text[position] != '\n') position++;
                    continue;
                }
                break;
            }
            if (position == text.Length) return null;
            if (text[position] is '{' or '}') return text[position++].ToString();
            if (text[position++] != '"') throw new InvalidDataException($"Expected quoted entity token at character {position}.");
            var start = position;
            while (position < text.Length && text[position] != '"' && text[position] != '\0') position++;
            if (position == text.Length || text[position] != '"') throw new InvalidDataException("Unterminated entity string.");
            // Quake's lexer treats backslashes literally; do not apply JSON/C# escapes.
            return text[start..position++];
        }

        var entities = new List<Entity>();
        while (Token() is { } token)
        {
            if (token != "{") throw new InvalidDataException("Expected opening entity brace.");
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new List<string>();
            while (true)
            {
                var key = Token() ?? throw new InvalidDataException("Unterminated entity.");
                if (key == "}") break;
                if (key == "{") throw new InvalidDataException("Unexpected nested entity brace.");
                var value = Token();
                if (value is null or "{" or "}") throw new InvalidDataException($"Missing value for entity key '{key}'.");
                if (!properties.TryAdd(key, value))
                {
                    duplicates.Add(key);
                    properties[key] = value; // ED_ParseEdict applies fields in source order.
                }
            }
            entities.Add(new Entity(properties, duplicates.AsReadOnly()));
        }
        return entities.AsReadOnly();
    }

    public static string Serialize(IEnumerable<Entity> entities)
    {
        var output = new StringBuilder();
        foreach (var entity in entities)
        {
            output.Append("{\n");
            foreach (var (key, value) in entity.Properties)
            {
                if (key.Contains('"') || value.Contains('"')) throw new InvalidDataException("Entity strings cannot contain quotes.");
                output.Append('"').Append(key).Append("\" \"").Append(value).Append("\"\n");
            }
            output.Append("}\n");
        }
        return output.ToString();
    }
}
