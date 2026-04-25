using System.Collections.Generic;

namespace NetworkBlaster.OData;

/// <summary>
/// One page of an OData v4 collection response: the entities plus the optional
/// <c>@odata.count</c> and <c>@odata.nextLink</c> annotations.
/// </summary>
public sealed record ODataPage<T>
{
    /// <summary>The items in this page (the OData <c>"value"</c> array).</summary>
    public IReadOnlyList<T> Value { get; init; } = System.Array.Empty<T>();

    /// <summary>Total entity count on the server, when the request asked for <c>$count=true</c>.</summary>
    public long? Count { get; init; }

    /// <summary>Absolute URL for the next page; <c>null</c> when this is the last page.</summary>
    public string? NextLink { get; init; }

    /// <summary>The <c>@odata.context</c> annotation, when the server provides one.</summary>
    public string? Context { get; init; }
}
