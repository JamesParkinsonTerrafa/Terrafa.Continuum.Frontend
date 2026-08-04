// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.Json.Serialization;

namespace Terrafa.Continuum.Frontend.Services;

// The wire shapes of Terrafa.Continuum.Core.DataFeed, mirrored rather than shared: the service is
// a separate repository and a separate deployment, and taking a project reference on it would drag
// the AWS SDK into a WASM bundle to gain nothing. They are records with the same member names, so
// drift shows up as a null where a value was expected — the names below are the contract.

/// <param name="CatalogName">The Athena catalog the datasets were read from.</param>
/// <param name="Databases">One entry per configured database that was read successfully.</param>
/// <param name="Errors">Databases that could not be read. The rest of the response is still valid.</param>
internal sealed record AvailableDatasetsResponse(
    string? CatalogName,
    IReadOnlyList<DatabaseDatasets>? Databases,
    IReadOnlyList<DatabaseError>? Errors);

internal sealed record DatabaseDatasets(string? Database, IReadOnlyList<DatasetSummary>? Datasets);

internal sealed record DatasetSummary(
    string? Name,
    string? TableType,
    DateTime? CreatedAt,
    DateTime? LastAccessedAt,
    IReadOnlyList<string>? PartitionKeys,
    IReadOnlyList<DatasetColumn>? Columns);

/// <param name="Type">Athena/Hive type string, e.g. "string" or "struct&lt;id:bigint&gt;".</param>
internal sealed record DatasetColumn(string? Name, string? Type, string? Comment);

internal sealed record DatabaseError(string? Database, string? Message);

/// <param name="PartitionKeys">
/// Athena keeps these out of <paramref name="Columns"/>, but they are selectable and filterable
/// like any other column, so the tree has to read both lists to be complete.
/// </param>
internal sealed record DatasetSchemaResponse(
    string? CatalogName,
    string? Database,
    string? Name,
    string? TableType,
    DateTime? CreatedAt,
    DateTime? LastAccessedAt,
    IReadOnlyList<DatasetColumn>? Columns,
    IReadOnlyList<DatasetColumn>? PartitionKeys);

/// <param name="Rows">
/// One value per entry in <paramref name="Columns"/>, in the same order. Values are Athena's own
/// string rendering and a SQL NULL is null, so anything wanting a number parses it here.
/// </param>
internal sealed record DatasetDataResponse(
    string? CatalogName,
    string? Database,
    string? Table,
    IReadOnlyList<ResultColumn>? Columns,
    IReadOnlyList<IReadOnlyList<string?>>? Rows,
    bool Truncated,
    string? QueryExecutionId,
    long? DataScannedBytes);

/// <param name="Name">The resolved column path, e.g. "id" or "payload.device.id".</param>
internal sealed record ResultColumn(string? Name, string? Type);

/// <summary>
/// RFC 7807, which is what every failure path in the service returns — <c>Problem(...)</c> on the
/// controller and the framework's own model-binding errors alike. Read on a non-2xx so the message
/// the service went to the trouble of writing reaches the screen instead of a bare status code.
/// </summary>
internal sealed record ProblemDetails(string? Title, string? Detail, int? Status);

/// <summary>
/// Source-generated metadata for the types above. The browser head publishes with AOT and the
/// trimmer, where reflection-based serialisation is exactly the pattern that gets stripped — the
/// same failure mode the UI project's TrimmerRootAssembly note describes. Generating the readers
/// at build time sidesteps it rather than relying on a root to keep it working.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AvailableDatasetsResponse))]
[JsonSerializable(typeof(DatasetSchemaResponse))]
[JsonSerializable(typeof(DatasetDataResponse))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class DataFeedJson : JsonSerializerContext;

/// <summary>
/// A call to the DataFeed service that did not produce a usable answer. Carries a message already
/// fit to show a user: the service's own <c>title — detail</c> when it replied, and the transport
/// failure when it did not.
/// </summary>
public sealed class DataFeedException : Exception
{
    public DataFeedException(string message, Exception? inner = null) : base(message, inner)
    {
        Console.WriteLine($"[datafeed] {message}");
    }
}
