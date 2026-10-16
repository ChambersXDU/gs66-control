namespace Gs66Control.Core;

public sealed record WmiPropertySchema(string Name, string Type, bool IsArray, bool IsKey);
public sealed record WmiMethodSchema(
    string Name,
    IReadOnlyList<WmiPropertySchema> InputParameters,
    IReadOnlyList<WmiPropertySchema> OutputParameters);
public sealed record WmiClassSchema(
    string Name,
    IReadOnlyList<WmiPropertySchema> Properties,
    IReadOnlyList<WmiMethodSchema> Methods,
    int? InstanceCount);
