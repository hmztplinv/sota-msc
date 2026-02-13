// src/BuildingBlocks/BuildingBlocks.Results/IResultBase.cs

namespace BuildingBlocks.Results;

/// <summary>
/// Result ve Result{T} için ortak interface.
/// Pipeline Behavior'lar bu interface üzerinden çalışır —
/// hem Result hem Result{T}'yi aynı generic constraint ile handle edebilir.
/// </summary>
public interface IResultBase
{
    bool IsSuccess { get; }
    bool IsFailure { get; }
    Error Error { get; }
}