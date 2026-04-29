using System.Collections.Generic;

namespace ARealmReorganized.Models;

public sealed class SetGroup
{
    public required uint SeriesId { get; init; }
    public required string Name { get; init; }
    public required List<DresserItem> Pieces { get; init; }
}
