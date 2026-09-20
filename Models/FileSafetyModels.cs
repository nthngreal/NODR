namespace NODR.Models;

public enum FileSafetyLevel
{
    SafeToRemove,
    ReviewFirst,
    DoNotRemoveDirectly,
    Unknown
}

public sealed record FileSafetyAssessment(
    FileSafetyLevel Level,
    string Category,
    string Label,
    string Effect,
    string Why,
    string IconGlyph,
    bool AllowQuickRecycle);
