using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Waa.App.Data;

namespace Waa.App.ViewModels;

public sealed class DriverRowViewModel
{
    private static readonly Brush NormalBrush = Brushes.Black;
    private static readonly Brush WarningBrush = Brushes.Firebrick;
    private static readonly Brush FollowUpBrush = Brushes.DarkGoldenrod;
    private static readonly Brush CompletedBrush = Brushes.DarkGreen;
    private static readonly Brush QuietBrush = Brushes.SlateGray;

    public DriverRowViewModel(FleetDriverRecord record, decimal threshold)
    {
        Record = record;
        Threshold = threshold;
    }

    public FleetDriverRecord Record { get; }
    public decimal Threshold { get; }
    public string DriverCode => Record.DriverCode;
    public string DriverName => Record.DriverName;
    public string UnitCode => Record.UnitCode;
    public string DriverLeader => Record.DriverLeader;
    public string IdentityLine => $"{DriverCode}  •  Unit {UnitCode}  •  Leader {DriverLeader}";

    public bool IsIdle7Above => Record.IdlePercent7Day is not null && Record.IdlePercent7Day.Value > Threshold;
    public bool IsIdle28Above => Record.IsComplete28Day && Record.IdlePercent28Day is not null && Record.IdlePercent28Day.Value > Threshold;
    public bool IsAboveThreshold => IsIdle7Above || IsIdle28Above;
    public bool NeedsIdleAttention => IsAboveThreshold && Record.LatestOutcome != IdleContactOutcome.Spoke;

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

    public int PriorityBand => IsAboveThreshold
        ? Record.LatestOutcome == IdleContactOutcome.Spoke ? 1 : 0
        : 2;

    public int AttentionRank => Record.LatestOutcome switch
    {
        IdleContactOutcome.SpokeFollowUp => 0,
        IdleContactOutcome.Attempted => 1,
        null => 2,
        IdleContactOutcome.Spoke => 3,
        _ => 4
    };

    public string Idle7Display => FormatPercent(Record.IdlePercent7Day);
    public string Idle28Display => Record.IsComplete28Day
        ? FormatPercent(Record.IdlePercent28Day)
        : $"Incomplete {Record.Coverage28Day}/4";

    public Brush Idle7Brush => IsIdle7Above ? WarningBrush : NormalBrush;
    public Brush Idle28Brush => IsIdle28Above ? WarningBrush : NormalBrush;
    public FontWeight Idle7FontWeight => IsIdle7Above ? FontWeights.SemiBold : FontWeights.Normal;
    public FontWeight Idle28FontWeight => IsIdle28Above ? FontWeights.SemiBold : FontWeights.Normal;

    public string AttentionText => IsAboveThreshold
        ? Record.LatestOutcome switch
        {
            IdleContactOutcome.SpokeFollowUp => "FOLLOW-UP",
            IdleContactOutcome.Attempted => "ATTEMPTED",
            IdleContactOutcome.Spoke => "SPOKE",
            _ => "NEEDS CALL"
        }
        : Record.LatestOutcome == IdleContactOutcome.SpokeFollowUp ? "FOLLOW-UP" : "OK";

    public Brush AttentionBrush => AttentionText switch
    {
        "NEEDS CALL" => WarningBrush,
        "ATTEMPTED" => FollowUpBrush,
        "FOLLOW-UP" => FollowUpBrush,
        "SPOKE" => CompletedBrush,
        _ => QuietBrush
    };

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
