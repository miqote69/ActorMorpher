using System.Diagnostics;

namespace ActorMorpher;

// Temporary measurement for the open-window FPS regression. No control decisions consume it.
public sealed partial class MainWindow
{
    private readonly double[] uiTimingTotals = new double[8];
    private int uiTimingFrames;
    private int uiTimingReports;

    private static long UiTimestamp() => Stopwatch.GetTimestamp();

    private void AddUiTiming(int phase, long start)
        => uiTimingTotals[phase] += Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private void ReportUiTiming(int actors)
    {
        if (++uiTimingFrames < 120)
            return;
        if (uiTimingReports++ < 6)
        {
            plugin.Diagnostics.Log.Write(new DiagnosticLogEntry
            {
                EventId = DiagnosticEventIds.UserActionRequested,
                Category = DiagnosticCategory.UserAction,
                Message = "Temporary Actor UI timing sample.",
                Properties = new Dictionary<string, object?>
                {
                    ["frames"] = uiTimingFrames,
                    ["actors"] = actors,
                    ["totalMs"] = uiTimingTotals[0] / uiTimingFrames,
                    ["pinActionsMs"] = uiTimingTotals[1] / uiTimingFrames,
                    ["filterMs"] = uiTimingTotals[2] / uiTimingFrames,
                    ["listMs"] = uiTimingTotals[3] / uiTimingFrames,
                    ["detailsMs"] = uiTimingTotals[4] / uiTimingFrames,
                    ["equipmentMs"] = uiTimingTotals[5] / uiTimingFrames,
                    ["pinLookupMs"] = uiTimingTotals[6] / uiTimingFrames,
                    ["filterResolveMs"] = uiTimingTotals[7] / uiTimingFrames,
                },
            });
            if (plugin.Diagnostics.Log is DiagnosticLogRouter { ActiveService: { } service })
                _ = service.FlushAsync(TimeSpan.FromSeconds(2));
        }
        uiTimingFrames = 0;
        Array.Clear(uiTimingTotals);
    }
}
