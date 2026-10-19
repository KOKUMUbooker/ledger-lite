using System.Globalization;

namespace LedgerLite.Web.Services;

public static class Fmt
{
    public static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string MoneyOrBlank(decimal value) => value == 0 ? string.Empty : Money(value);
}