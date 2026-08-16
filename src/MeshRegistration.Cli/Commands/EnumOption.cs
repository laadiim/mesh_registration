using System.CommandLine;
using System.CommandLine.Parsing;

namespace MeshRegistration.Cli.Commands;

/// <summary>
/// Builds enum-valued options that accept human-friendly spellings.
/// </summary>
/// <remarks>
/// The default binding only accepts the exact member name, so a value such as
/// <c>PairBestContinuation</c> has to be typed in full and in PascalCase. Command lines are
/// written by hand, so this parser also accepts the hyphenated, underscored and lower-case forms
/// people actually type — <c>pair-best</c>, <c>pair_best</c>, <c>PAIRBEST</c> — plus explicit
/// short aliases.
/// </remarks>
internal static class EnumOption
{
    public static Option<TEnum> Create<TEnum>(
        string name,
        string description,
        TEnum defaultValue,
        params (string Alias, TEnum Value)[] aliases)
        where TEnum : struct, Enum
    {
        return new Option<TEnum>(name)
        {
            Description = $"{description} One of: {DescribeValues(aliases)}.",
            DefaultValueFactory = _ => defaultValue,
            CustomParser = result => Parse(result, defaultValue, aliases),
        };
    }

    /// <summary>
    /// Builds an enum option with no default, so that "not given" stays distinguishable from any
    /// particular value.
    /// </summary>
    public static Option<TEnum?> CreateNullable<TEnum>(
        string name,
        string description,
        params (string Alias, TEnum Value)[] aliases)
        where TEnum : struct, Enum
    {
        return new Option<TEnum?>(name)
        {
            Description = $"{description} One of: {DescribeValues(aliases)}.",
            CustomParser = result =>
            {
                TEnum parsed = Parse(result, default, aliases);
                return result.Tokens.Count == 0 ? null : parsed;
            },
        };
    }

    private static TEnum Parse<TEnum>(
        ArgumentResult result,
        TEnum defaultValue,
        (string Alias, TEnum Value)[] aliases)
        where TEnum : struct, Enum
    {
        if (result.Tokens.Count == 0)
        {
            return defaultValue;
        }

        string token = result.Tokens[0].Value;
        string normalised = Normalise(token);

        foreach ((string alias, TEnum value) in aliases)
        {
            if (Normalise(alias).Equals(normalised, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (Normalise(candidate.ToString()).Equals(normalised, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        result.AddError($"'{token}' is not a valid value. Expected one of: {DescribeValues(aliases)}.");
        return defaultValue;
    }

    /// <summary>Strips the separators that distinguish otherwise-equivalent spellings.</summary>
    private static string Normalise(string value) =>
        value.Replace("-", string.Empty, StringComparison.Ordinal)
             .Replace("_", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Lists the accepted values for help text, preferring a short alias where one exists.
    /// </summary>
    private static string DescribeValues<TEnum>((string Alias, TEnum Value)[] aliases)
        where TEnum : struct, Enum
    {
        IEnumerable<string> names = Enum.GetValues<TEnum>().Select(value =>
        {
            foreach ((string alias, TEnum aliased) in aliases)
            {
                if (EqualityComparer<TEnum>.Default.Equals(aliased, value))
                {
                    return alias;
                }
            }

            return value.ToString().ToLowerInvariant();
        });

        return string.Join(", ", names);
    }
}
