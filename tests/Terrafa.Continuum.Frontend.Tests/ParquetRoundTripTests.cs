// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Parquet;
using Parquet.Schema;

namespace Terrafa.Continuum.Frontend.Tests;

public class ParquetRoundTripTests
{
    [Theory]
    [InlineData(CompressionMethod.Snappy)]
    [InlineData(CompressionMethod.Gzip)]
    [InlineData(CompressionMethod.None)]
    public async Task FlatSchemaRoundTripsAcrossThreeRowGroups(CompressionMethod codec)
    {
        var timestampField = new DataField<long>("timestamp");
        var levelField = new DataField<double>("level");
        var statusField = new DataField<string>("status");
        var schema = new ParquetSchema(timestampField, levelField, statusField);

        var groupSizes = new[] { 4, 4, 2 };
        var timestamps = Enumerable.Range(0, 10).Select(i => 1_600_000_000L + i * 60).ToArray();
        var levels = Enumerable.Range(0, 10).Select(i => 20.0 + i * 0.25).ToArray();
        var statuses = Enumerable.Range(0, 10).Select(i => i % 3 == 0 ? "DRIFT" : "OK").ToArray();

        using var stream = new MemoryStream();
        var options = new ParquetOptions { CompressionMethod = codec };
        await using (var writer = await ParquetWriter.CreateAsync(schema, stream, options))
        {
            var offset = 0;
            foreach (var size in groupSizes)
            {
                using var rowGroup = writer.CreateRowGroup();
                await rowGroup.WriteAsync(timestampField, new ReadOnlyMemory<long>(timestamps, offset, size));
                await rowGroup.WriteAsync(levelField, new ReadOnlyMemory<double>(levels, offset, size));
                await rowGroup.WriteAsync(statusField, statuses.Skip(offset).Take(size).ToArray());
                offset += size;
            }
        }

        stream.Position = 0;
        await using var reader = await ParquetReader.CreateAsync(stream);

        Assert.Equal(3, reader.RowGroupCount);
        Assert.Equal(
            new[] { "timestamp", "level", "status" },
            reader.Schema.DataFields.Select(field => field.Name).ToArray());

        var readOffset = 0;
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(group);
            var rowCount = (int)rowGroupReader.RowCount;
            Assert.Equal(groupSizes[group], rowCount);

            var readTimestamps = new long[rowCount];
            var readLevels = new double[rowCount];
            var readStatuses = new string?[rowCount];
            await rowGroupReader.ReadAsync(timestampField, readTimestamps.AsMemory());
            await rowGroupReader.ReadAsync(levelField, readLevels.AsMemory());
            await rowGroupReader.ReadAsync(statusField, readStatuses.AsMemory());

            Assert.Equal(timestamps.AsSpan(readOffset, rowCount).ToArray(), readTimestamps);
            Assert.Equal(levels.AsSpan(readOffset, rowCount).ToArray(), readLevels);
            Assert.Equal(statuses.AsSpan(readOffset, rowCount).ToArray(), readStatuses);
            readOffset += rowCount;
        }
    }
}
