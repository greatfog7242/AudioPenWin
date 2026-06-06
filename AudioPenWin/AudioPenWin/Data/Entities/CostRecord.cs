namespace AudioPenWin.Data.Entities;

public record CostRecord
{
    public string SttProvider { get; init; } = "";
    public long TotalCostCents { get; init; }
    public long TotalDurationMs { get; init; }
}
