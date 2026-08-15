using System.Text.Json.Serialization;

namespace Huuray.Serialization;

/// <summary>
/// The source-generated serialisation contract for every request and response.
/// </summary>
/// <remarks>
/// Nothing in this library serialises by reflection, which is what makes the package
/// safe to trim and to compile ahead of time. Adding a wire type without registering it
/// here is a compile-time error at its first use, not a run-time surprise.
/// </remarks>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(CatalogueRequestWire))]
[JsonSerializable(typeof(StockRequestWire))]
[JsonSerializable(typeof(OrderRequestWire))]
[JsonSerializable(typeof(SearchRequestWire))]
[JsonSerializable(typeof(ResendRequestWire))]
[JsonSerializable(typeof(CancelRequestWire))]
[JsonSerializable(typeof(BalanceResponseWire))]
[JsonSerializable(typeof(CatalogueResponseWire))]
[JsonSerializable(typeof(TemplateResponseWire))]
[JsonSerializable(typeof(StockResponseWire))]
[JsonSerializable(typeof(ExchangeRatesResponseWire))]
[JsonSerializable(typeof(OrderResponseWire))]
[JsonSerializable(typeof(ResendResponseWire))]
[JsonSerializable(typeof(CancelResponseWire))]
internal sealed partial class HuurayJsonContext : JsonSerializerContext
{
}
