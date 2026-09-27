using System.ComponentModel.DataAnnotations;

namespace DTOs.Common;

public record PageQuery
{
    [Range(1, 10_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record OptionDto(string Key, string Label);

public sealed record ReferenceDataDto(IReadOnlyList<OptionDto> Categories, IReadOnlyList<string> Governorates);

public sealed record UploadResultDto(string Url);

public sealed record CountDto(int Count);
