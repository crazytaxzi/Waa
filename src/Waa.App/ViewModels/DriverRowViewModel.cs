using System.Globalization;
using Waa.App.Data;

namespace Waa.App.ViewModels;

public sealed class DriverRowViewModel
{
    public DriverRowViewModel(
        FleetDriverRecord record,
        decimal threshold,
        MissingBolDriverSummary? missingBolSummary = null)
    {
        Record = record;
        Threshold = threshold;
        MissingBolSummary = missingBolSummary;
    }

    public FleetDriverRecord Record { get; }
    public decimal Threshold { get; }
    public MissingBolDriverSummary? MissingBolSummary { get; }
    public string DriverCode => Record.DriverCode;
    public string DriverName => Record.DriverName;
    public string UnitCode => Record.UnitCode;
    public string DriverLeader => Record.DriverLeader;
    public string IdentityLine => $"{DriverCode}  •  Unit {UnitCode}  •  Leader {DriverLeader}";
    public string FleetIdentityLine => $"{DriverCode} • Unit {UnitCode}";
    public int OpenWorkCount => Record.OpenWorkCount;
    public bool HasOpenWork => OpenWorkCount > 0;
    public string OpenWorkDisplay => HasOpenWork
        ? $"{OpenWorkCount.ToString(CultureInfo.CurrentCulture)} open"
        : string.Empty;
    public int MissingBolCount => MissingBolSummary?.OpenCount ?? 0;
    public bool HasMissingBol => MissingBolCount > 0;
    public string MissingBolDisplay => HasMissingBol
        ? MissingBolCount.ToString(CultureInfo.CurrentCulture)
        : string.Empty;
    public DateOnly? OldestMissingBolDate => MissingBolSummary?.OldestOpenEmptyCallDate;
    public string OrderSearchText => MissingBolSummary?.OrderSearchText ?? string.Empty;

    public bool IsIdle7Above => Record.IdlePercent7Day is not null && Record.IdlePercent7Day.Value > Threshold;
    public bool IsIdle28Above => Record.IsComplete28Day && Record.IdlePercent28Day is not null && Record.IdlePercent28Day.Value > Threshold;
    public bool IsAboveThreshold => IsIdle7Above || IsIdle28Above;
    public bool NeedsIdleAttention => IsAboveThreshold && Record.LatestOutcome != IdleContactOutcome.Spoke;
    public bool NeedsAnyAttention => NeedsIdleAttention || HasOpenWork;

    public decimal? ConcernPercent
    {
        get
        {
            var seven = Record.IdlePercent7Day;
            var twentyEight = Record.IsComplete28Day ? Record.IdlePercent28Day : null;
            if (seven is null)
            {
                return twentyEight;
            }

            if (twentyEight is null)
            {
                return seven;
            }

            return Math.Max(seven.Value, twentyEight.Value);
        }
    }

    public int PriorityBand
    {
        get
        {
            if (NeedsIdleAttention)
            {
                return 0;
            }

            if (IsAboveThreshold)
            {
                return 1;
            }

            return HasOpenWork ? 2 : 3;
        }
    }

    public int PriorityWithinBand => PriorityBand switch
    {
        0 => Record.LatestOutcome switch
        {
            IdleContactOutcome.SpokeFollowUp => 0,
            IdleContactOutcome.Attempted => 1,
            null => 2,
            _ => 3
        },
        1 => HasOpenWork ? 0 : 1,
        _ => 0
    };

    public decimal? PriorityConcern => PriorityBand <= 1 ? ConcernPercent : null;

    public string Idle7Display => FormatPercent(Record.IdlePercent7Day);
    public string Idle28Display => Record.IsComplete28Day
        ? FormatPercent(Record.IdlePercent28Day)
        : $"Incomplete {Record.Coverage28Day}/4";

    public string AttentionText
    {
        get
        {
            if (IsAboveThreshold)
            {
                return Record.LatestOutcome switch
                {
                    IdleContactOutcome.SpokeFollowUp => "FOLLOW-UP",
                    IdleContactOutcome.Attempted => "ATTEMPTED",
                    IdleContactOutcome.Spoke => HasOpenWork ? "SPOKE + WORK" : "SPOKE",
                    _ => "NEEDS CALL"
                };
            }

            return HasOpenWork ? "OPEN WORK" : "OK";
        }
    }

    public string ContactDisplay
    {
        get
        {
            var status = Record.LatestOutcome switch
            {
                IdleContactOutcome.Attempted => "Attempted",
                IdleContactOutcome.Spoke => "Spoke",
                IdleContactOutcome.SpokeFollowUp => "Spoke — Follow-up",
                _ => "Not contacted"
            };

            if (Record.LatestContactUtc is null)
            {
                return status;
            }

            var local = Record.LatestContactUtc.Value.ToLocalTime();
            return $"{status} {local.ToString("M/d h:mm tt", CultureInfo.CurrentCulture)}";
        }
    }

    private static string FormatPercent(decimal? value) =>
        value is null ? "N/A" : $"{value.Value.ToString("0.0", CultureInfo.CurrentCulture)}%";
}
