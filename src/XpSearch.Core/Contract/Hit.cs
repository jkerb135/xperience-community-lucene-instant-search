using System.Text.Json;
using System.Text.Json.Serialization;

namespace XpSearch.Core.Contract;

/// <summary>
/// Hand-written half of the generated <see cref="Hit"/> partial class
/// (see <c>Contract/Generated/XpSearchContract.g.cs</c> and
/// <c>contract/xpsearch-api.schema.json</c>, definition <c>Hit</c>).
/// </summary>
/// <remarks>
/// A hit is an open object: <c>objectID</c> and the underscore-prefixed members are reserved by the
/// contract, everything else is a retrieved document attribute. quicktype does not emit
/// <see cref="JsonExtensionDataAttribute"/> for a JSON Schema <c>additionalProperties</c>, so the
/// open half lives here instead of in the generated file.
/// </remarks>
public partial class Hit
{
    /// <summary>
    /// Gets or sets the document attributes that are not reserved by the contract - whatever the index
    /// projects and <c>attributesToRetrieve</c> asks for, for example <c>title</c>, <c>summary</c> or
    /// <c>image</c>. They are serialized as siblings of <c>objectID</c>, not nested under a property.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Attributes { get; set; } = [];
}
