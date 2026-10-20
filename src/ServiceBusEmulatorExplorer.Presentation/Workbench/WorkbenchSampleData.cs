using System.Globalization;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>One row of sample data with the values mapped to CustomerId and Amount, and whether it can be sent.</summary>
public sealed record SampleRow(int Row, string CustomerId, decimal? Amount, string Status)
{
    public const string Ready = "Ready";
}

/// <summary>
/// Where CustomerId and Amount come from: a "CSV column" (named by the value), a "Constant" (the value), or the
/// variable's "Declared default".
/// </summary>
public sealed record SampleMapping(string CustomerSource, string CustomerValue, string AmountSource, string AmountValue);

/// <summary>Why the Prepare inputs cannot be previewed; <see cref="ClearsPreview"/> when the old preview no longer applies.</summary>
public sealed record PrepareInputProblem(string Hint, bool ClearsPreview);

/// <summary>
/// The Message Workbench's sample data: the three rows of the sample CSV files as the mapping reads them, their
/// validation, and the checks on the Prepare inputs before anything is previewed.
/// </summary>
public static class WorkbenchSampleData
{
    /// <summary>The sample file whose second row has an amount that is not a number.</summary>
    public const string InvalidSampleFile = "orders-invalid.csv";

    /// <summary>The sample rows of <paramref name="sampleFile"/> as <paramref name="mapping"/> reads them.</summary>
    /// <param name="commaDelimited">Whether the comma delimiter is chosen; the samples are comma-separated.</param>
    /// <param name="declaredDefault">The default value declared for a variable, read only when the mapping uses it.</param>
    public static IReadOnlyList<SampleRow> Rows(string sampleFile, SampleMapping mapping, bool commaDelimited, Func<string, string> declaredDefault)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(declaredDefault);
        var sample = new[]
        {
            (Row: 1, Customer: "C1001", Amount: "149.90"),
            (Row: 2, Customer: "C1002", Amount: sampleFile == InvalidSampleFile ? "abc" : "224.00"),
            (Row: 3, Customer: "C1003", Amount: "79.50")
        };
        return sample.Select(value =>
        {
            string customer = mapping.CustomerSource == "Declared default" ? declaredDefault("CustomerId")
                : mapping.CustomerSource == "Constant" ? mapping.CustomerValue
                : mapping.CustomerValue == "Amount" ? value.Amount : value.Customer;
            string amountText = mapping.AmountSource == "Declared default" ? declaredDefault("Amount")
                : mapping.AmountSource == "Constant" ? mapping.AmountValue
                : mapping.AmountValue == "CustomerId" ? value.Customer : value.Amount;
            bool numeric = decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount);
            string status = numeric && !string.IsNullOrWhiteSpace(customer) && commaDelimited ? SampleRow.Ready
                : !commaDelimited ? "Invalid delimiter" : !numeric ? $"Expected number, got {amountText}" : "CustomerId required";
            return new SampleRow(value.Row, customer, numeric ? amount : null, status);
        }).ToArray();
    }

    /// <summary>The validation summary: every row valid, or how many are not and that nothing can be sent.</summary>
    public static string Summary(IReadOnlyCollection<SampleRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int invalid = rows.Count(row => row.Status != SampleRow.Ready);
        return invalid == 0 ? $"{rows.Count} of {rows.Count} rows valid" : $"{invalid} invalid row{(invalid == 1 ? "" : "s")} · Nothing can be sent";
    }

    /// <summary>The value a mapping source starts with: 0 the column of the same name, 1 a sample constant, 2 the declared default.</summary>
    public static string MappingStartValue(int source, string column, string constant, string? declaredDefault) =>
        source == 0 ? column : source == 1 ? constant : declaredDefault ?? "";

    /// <summary>Why a mapping cannot be applied: CustomerId and Amount both need a value.</summary>
    public static string? MappingProblem(string customerValue, string amountValue) =>
        string.IsNullOrWhiteSpace(customerValue) || string.IsNullOrWhiteSpace(amountValue)
            ? "CustomerId and Amount are required. Map a CSV column, enter a constant, or declare a default in Variables." : null;

    /// <summary>
    /// What stops a preview: a time to live that is not a positive whole number, a custom message ID that is empty or
    /// used with CSV data, or, for a single message, no CustomerId or an Amount that is not a number.
    /// </summary>
    public static PrepareInputProblem? InputProblem(bool specifyTtl, string ttlMinutes, bool customMessageId, string customMessageIdValue,
        bool csv, string customerId, string amount)
    {
        if (specifyTtl && (!int.TryParse(ttlMinutes, out int ttl) || ttl <= 0) ||
            customMessageId && (string.IsNullOrWhiteSpace(customMessageIdValue) || csv))
            return new("Enter a positive TTL; custom message IDs require a nonempty value and single-message mode.", ClearsPreview: false);
        if (!csv && (string.IsNullOrWhiteSpace(customerId) || !decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
            return new("Enter a CustomerId and a numeric Amount to preview.", ClearsPreview: true);
        return null;
    }
}
