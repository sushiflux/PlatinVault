using System.Globalization;
using System.Reflection;

namespace PlatinVault.Bridge;

/// <summary>
/// Test aid behind the hidden <c>--test-texts</c> option: walks the given text catalogues (the static text classes of a host, e.g. <see cref="CoreTexts"/>) by reflection and
/// checks every entry. A fixed text is a field, a parameterised one a method - the methods are called with marker arguments, so
/// the check also sees whether BOTH languages use every argument (a forgotten <c>{name}</c> in the English line shows up as a
/// marker that is only in the German text). Failures: an empty text, a text that is identical in both languages without being
/// declared with <see cref="Msg.Same"/> (a forgotten translation), a stray brace (a formatting slip) and a marker in only one
/// language. Exit code 0 = fine, 1 = problems (listed), so a script can gate on it.
/// </summary>
internal static class TextCatalog
{
    /// <param name="roots">The catalogue classes; their nested classes are walked too.</param>
    /// <param name="samples">Values for parameters of a type the check cannot invent itself (e.g. the host's environment object).</param>
    public static int Run(IEnumerable<Type> roots, IReadOnlyDictionary<Type, object>? samples = null)
    {
        samples ??= new Dictionary<Type, object>();
        var problems = new List<string>();
        var checkedCount = 0;

        foreach (var type in roots.SelectMany(AllTypes))
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Msg)))
            {
                checkedCount++;
                Inspect($"{Name(type)}.{field.Name}", (Msg)field.GetValue(null)!, [], problems);
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(Msg)))
            {
                foreach (var arguments in ArgumentSets(method, samples))
                {
                    checkedCount++;
                    Msg message;
                    try
                    {
                        message = (Msg)method.Invoke(null, arguments.Values)!;
                    }
                    catch (TargetInvocationException ex)
                    {
                        problems.Add($"{Name(type)}.{method.Name}: throws {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
                        continue;
                    }
                    Inspect($"{Name(type)}.{method.Name}({arguments.Description})", message, arguments.Markers, problems);
                }
            }
        }

        BridgeLog.Print($"Text catalogue: {checkedCount} entries checked, {problems.Count} problem(s).");
        foreach (var problem in problems) BridgeLog.Print("  " + problem);
        return problems.Count == 0 ? 0 : 1;
    }

    private static void Inspect(string name, Msg message, IReadOnlyList<string> markers, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(message.De)) problems.Add($"{name}: German text is empty");
        if (string.IsNullOrWhiteSpace(message.En)) problems.Add($"{name}: English text is empty");
        if (string.IsNullOrWhiteSpace(message.De) || string.IsNullOrWhiteSpace(message.En)) return;

        if (message.De == message.En && !message.Shared)
            problems.Add($"{name}: identical in both languages (forgotten translation? use Msg.Same if it is meant)");

        if (message.De.Contains('{') || message.De.Contains('}') || message.En.Contains('{') || message.En.Contains('}'))
            problems.Add($"{name}: contains a brace (formatting slip?)");

        foreach (var marker in markers)
        {
            var inDe = message.De.Contains(marker, StringComparison.Ordinal);
            var inEn = message.En.Contains(marker, StringComparison.Ordinal);
            if (inDe != inEn)
                problems.Add($"{name}: argument '{marker}' is used in the {(inDe ? "German" : "English")} text only");
        }
    }

    private static IEnumerable<Type> AllTypes(Type root)
    {
        yield return root;
        foreach (var nested in root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var type in AllTypes(nested))
                yield return type;
    }

    private static string Name(Type type) => type.DeclaringType is { } outer && outer != typeof(object) ? $"{Name(outer)}.{type.Name}" : type.Name;

    private sealed record ArgumentSet(object?[] Values, IReadOnlyList<string> Markers, string Description);

    /// <summary>Marker arguments for a method; every bool parameter runs both ways (the texts often differ by such a switch).</summary>
    private static IEnumerable<ArgumentSet> ArgumentSets(MethodInfo method, IReadOnlyDictionary<Type, object> samples)
    {
        var parameters = method.GetParameters();
        var boolIndexes = parameters.Select((p, i) => (p, i)).Where(x => x.p.ParameterType == typeof(bool)).Select(x => x.i).ToArray();

        for (var combination = 0; combination < (1 << boolIndexes.Length); combination++)
        {
            var values = new object?[parameters.Length];
            var markers = new List<string>();
            var description = new List<string>();

            for (var i = 0; i < parameters.Length; i++)
            {
                var type = Nullable.GetUnderlyingType(parameters[i].ParameterType) ?? parameters[i].ParameterType;
                if (type == typeof(bool))
                {
                    var flag = (combination >> Array.IndexOf(boolIndexes, i) & 1) == 1;
                    values[i] = flag;
                    description.Add(flag ? "true" : "false");
                }
                else if (type == typeof(string))
                {
                    var marker = $"<arg{i}>";
                    values[i] = marker;
                    markers.Add(marker);
                    description.Add(marker);
                }
                else if (type == typeof(int) || type == typeof(uint))
                {
                    var number = 1000 + 111 * i;
                    values[i] = type == typeof(uint) ? (object)(uint)number : number;
                    markers.Add(number.ToString(CultureInfo.InvariantCulture));
                    description.Add(number.ToString(CultureInfo.InvariantCulture));
                }
                else if (type == typeof(double))
                {
                    // Formatted with a pattern ("0" / "0.0") in the texts, so no exact marker: just a plausible value
                    values[i] = 2.0;
                    description.Add("2");
                }
                else if (type == typeof(Version))
                {
                    var version = new Version(1, 2, 30 + i);
                    values[i] = version;
                    markers.Add(version.ToString());
                    description.Add(version.ToString());
                }
                else if (type == typeof(Msg))
                {
                    var sample = new Msg($"<de{i}>", $"<en{i}>");
                    values[i] = sample;
                    description.Add("Msg");
                }
                else if (samples.TryGetValue(type, out var provided))
                {
                    values[i] = provided;
                    description.Add(type.Name);
                }
                else if (typeof(IEnumerable<Msg>).IsAssignableFrom(type))
                {
                    values[i] = new[] { new Msg("<de-a>", "<en-a>"), new Msg("<de-b>", "<en-b>") };
                    description.Add("Msg[]");
                }
                else
                {
                    throw new InvalidOperationException($"{method.DeclaringType?.Name}.{method.Name}: add a sample for parameter type {type.Name} to TextCatalog (or pass one in the samples).");
                }
            }

            yield return new ArgumentSet(values, markers, string.Join(", ", description));
        }
    }
}
