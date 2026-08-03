using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence;

/// <summary>
/// Cross-cutting column conventions applied explicitly by every configuration, so the
/// storage shape of timestamps, money, and enums is never left to provider defaults.
/// </summary>
internal static class ColumnTypes
{
    /// <summary>Storage type for every stored instant.</summary>
    public const string Timestamp = "datetimeoffset(7)";

    /// <summary>Length used for every enum-as-string column.</summary>
    public const int EnumLength = 32;

    /// <summary>Length of an ISO-4217 currency code.</summary>
    public const int CurrencyLength = 3;

    /// <summary>Maps a stored instant to <c>datetimeoffset(7)</c>.</summary>
    public static PropertyBuilder<DateTimeOffset> AsTimestamp(this PropertyBuilder<DateTimeOffset> builder)
        => builder.HasColumnType(Timestamp).IsRequired();

    /// <summary>Maps an optional stored instant to <c>datetimeoffset(7)</c>.</summary>
    public static PropertyBuilder<DateTimeOffset?> AsTimestamp(this PropertyBuilder<DateTimeOffset?> builder)
        => builder.HasColumnType(Timestamp);

    /// <summary>
    /// Stores an enum as a constrained, non-Unicode string of fixed maximum length. The
    /// matching SQL check constraint is added by <see cref="EnumValues{TEnum}"/>.
    /// </summary>
    public static PropertyBuilder<TEnum> AsEnumString<TEnum>(this PropertyBuilder<TEnum> builder)
        where TEnum : struct, Enum
        => builder.HasConversion<string>().HasMaxLength(EnumLength).IsUnicode(false).IsRequired();

    /// <summary>Maps an uppercase ISO-4217 currency code to <c>char(3)</c>.</summary>
    public static PropertyBuilder<string> AsCurrency(this PropertyBuilder<string> builder)
        => builder.HasMaxLength(CurrencyLength).IsUnicode(false).IsFixedLength().IsRequired();

    /// <summary>
    /// Renders the allowed values of an enum as a SQL <c>IN</c> list. Values come from
    /// compile-time enum names, never from user input.
    /// </summary>
    public static string EnumValues<TEnum>(string column)
        where TEnum : struct, Enum
        => $"[{column}] IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"))})";

    /// <summary>
    /// Case-sensitive assertion that a currency column holds only uppercase characters.
    /// The explicit binary collation is required because the database default collation is
    /// case-insensitive, which would make the comparison trivially true.
    /// </summary>
    public static string UppercaseCurrency(string column)
        => $"[{column}] = UPPER([{column}]) COLLATE Latin1_General_BIN2";
}
