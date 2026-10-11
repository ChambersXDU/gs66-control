namespace Gs66Control.Core;

/// <summary>Class definitions only; no vendor instance values or method execution.</summary>
public sealed record MsiWmiClassSchema(
    string Name,
    string? Guid,
    IReadOnlyList<WmiPropertySchema> Properties,
    IReadOnlyList<string> Methods);

public sealed record WmiPropertySchema(string Name, string Type, bool IsArray, bool IsKey);
