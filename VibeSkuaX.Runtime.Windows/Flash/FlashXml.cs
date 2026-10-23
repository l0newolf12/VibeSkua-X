using System.Collections;
using System.Dynamic;
using System.Globalization;
using System.Xml.Linq;

namespace VibeSkuaX.Runtime.Windows.Flash;

/// <summary>The ExternalInterface value format, shared by returns and callbacks.</summary>
internal static class FlashXml
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static XElement Write(object? value)
    {
        if (value is null) return new XElement("null");
        if (value is bool boolean) return new XElement(boolean ? "true" : "false");
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return new XElement("number", ((IFormattable)value).ToString(null, Invariant));
        if (value is IDictionary<string, object?> properties)
            return new XElement("object", properties.Select(p =>
                new XElement("property", new XAttribute("id", p.Key), Write(p.Value))));
        if (value is Array array)
            return new XElement("array", array.Cast<object?>().Select((item, index) =>
                new XElement("property", new XAttribute("id", index), Write(item))));
        return new XElement("string", value.ToString());
    }

    public static object? Read(XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "null":
            case "undefined": return null;
            case "true": return true;
            case "false": return false;
            case "string": return element.Value;
            case "number":
                if (int.TryParse(element.Value, NumberStyles.Integer, Invariant, out int integer)) return integer;
                if (long.TryParse(element.Value, NumberStyles.Integer, Invariant, out long wide)) return wide;
                return double.Parse(element.Value, NumberStyles.Float, Invariant);
            case "array":
                var items = new SortedDictionary<int, object?>();
                foreach (var property in element.Elements("property"))
                {
                    int index = int.Parse(RequiredId(property), NumberStyles.None, Invariant);
                    if (index < 0) throw new FormatException("Flash array index must be non-negative.");
                    items.Add(index, Read(RequiredValue(property)));
                }
                if (items.Count == 0) return Array.Empty<object?>();
                var array = new object?[checked(items.Keys.Last() + 1)];
                foreach (var item in items) array[item.Key] = item.Value;
                return array;
            case "object":
                IDictionary<string, object?> result = new ExpandoObject();
                foreach (var property in element.Elements("property"))
                    result.Add(RequiredId(property), Read(RequiredValue(property)));
                return result;
            default: throw new FormatException($"Unsupported Flash value element: {element.Name}.");
        }
    }

    public static object? ConvertResult(XElement element, Type type)
    {
        object? value = Read(element);
        if (value is null || type == typeof(void)) return null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsInstanceOfType(value) ? value : Convert.ChangeType(value, type, Invariant);
    }

    public static object[] ReadArguments(XElement request)
    {
        var arguments = request.Element("arguments")?.Elements().Select(Read).ToArray() ?? [];
        // Skua's Externalizer.call(name, ...rest) passes rest as one Flash array.
        return (arguments.Length == 1 && arguments[0] is object[] rest ? rest : arguments)!;
    }

    private static string RequiredId(XElement property) =>
        (string?)property.Attribute("id") ?? throw new FormatException("Flash property has no id.");
    private static XElement RequiredValue(XElement property) =>
        property.Elements().SingleOrDefault() ?? throw new FormatException("Flash property has no value.");
}
