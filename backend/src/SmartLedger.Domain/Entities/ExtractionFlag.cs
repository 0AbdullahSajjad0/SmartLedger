namespace SmartLedger.Domain.Entities;

/// <summary>
/// A field the AI was unsure about, shown to the user as "please confirm" during review.
/// </summary>
/// <param name="Field">The receipt field name, e.g. "PaymentMethod".</param>
/// <param name="Reason">Why it needs checking, e.g. "One digit of the card number was faded."</param>
public record ExtractionFlag(string Field, string Reason);
