using System.Globalization;
using System.Reflection;
using System.Text;

namespace NextItemEvaluation;

/// <summary>
/// Reads a <c>psql \copy … with csv header</c> export into entity objects. The schema's
/// columns are PascalCase and named after the entity properties, so the header maps onto
/// them one to one; enums arrive as their text form, per the persistence rule.
/// </summary>
internal static class CsvRows
{
    public static List<T> Read<T>(string path)
        where T : new()
    {
        using var reader = new StreamReader(path);
        var header = ReadRecord(reader) ?? throw new InvalidDataException($"{path} is empty.");
        var properties = header
            .Select(name => typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .ToArray();

        var rows = new List<T>();
        while (ReadRecord(reader) is { } record)
        {
            var row = new T();
            for (var i = 0; i < record.Count && i < properties.Length; i++)
            {
                if (properties[i] is { CanWrite: true } property && record[i].Length > 0)
                {
                    property.SetValue(row, Convert(record[i], property.PropertyType));
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The raw fields of every record, header first.</summary>
    public static IEnumerable<List<string>> Records(string path)
    {
        using var reader = new StreamReader(path);
        while (ReadRecord(reader) is { } record)
        {
            yield return record;
        }
    }

    private static object? Convert(string value, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(string))
        {
            return value;
        }

        if (target.IsEnum)
        {
            return Enum.Parse(target, value);
        }

        if (target == typeof(bool))
        {
            return value is "t" or "true" or "True";
        }

        if (target == typeof(Guid))
        {
            return Guid.Parse(value);
        }

        if (target == typeof(DateTime))
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    /// <summary>One RFC 4180 record: quoted fields may hold commas, doubled quotes and newlines.</summary>
    private static List<string>? ReadRecord(StreamReader reader)
    {
        if (reader.Peek() < 0)
        {
            return null;
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                fields.Add(field.ToString());
                return fields;
            }

            var c = (char)next;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                return fields;
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }
    }
}
