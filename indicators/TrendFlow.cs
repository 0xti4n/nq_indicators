#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

public enum TrendFlowBandPreset { Scalping, Balanced, DeepTrend, Custom }
public enum TrendFlowFlipBand { FastBand2, BalancedBand3, DeepBand4, ExtremeBand5, MaximumBand6, Band7, Band8, Band9, Band10 }
public enum TrendFlowPriceSource { Close, HL2, HLC3, OHLC4 }
public enum TrendFlowTimeFrameMode { Chart, Minute, Day, Week, Month }
public enum TrendFlowTheme { Auto, Dark, Light }
public enum TrendFlowSize { Tiny, Small, Normal, Large, Huge }
public enum TrendFlowRiskPreset { Conservative, Balanced, Aggressive, Scalping, Custom }
public enum TrendFlowSlMode { WickAnchored, ATR }
public enum TrendFlowLineStyle { Solid, Dashed, Dotted }
public enum TrendFlowDashPosition { TopLeft, TopRight, BottomLeft, BottomRight, MiddleRight }

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// TrendFlow v1.3.0 port for NinjaTrader 8.
    /// ATR trail-band trend engine, scored retests, trade guide, segment volume profile,
    /// dashboard and realtime alerts. Historical drawing is bounded by PlotDays by default.
    /// </summary>
    public class TrendFlow : Indicator
    {
        private const string IndicatorVersion = "v1.3.0-nt8";
        private const int LineForwardBars = 20;
        private const int LineUpdateBars = 5;
        private const int LabelOffsetBars = 1;
        private const int FormLen = 10;

        private int engineBip = -1;
        private int htfBip = -1;
        private DateTime plotCutoff = DateTime.MinValue;
        private int firstDrawBar = -1;

        private Series<double> band1Series;
        private Series<double> band2Series;
        private Series<double> band3Series;
        private Series<double> band4Series;
        private Series<double> band5Series;
        private Series<double> band6Series;
        private Series<double> band7Series;
        private Series<double> band8Series;
        private Series<double> band9Series;
        private Series<double> band10Series;

        private SimpleFont signalFont;
        private SimpleFont labelFont;
        private SimpleFont dashboardFont;
        private SimpleFont profileFont;

        private Brush bullPlot1;
        private Brush bullPlot2;
        private Brush bullPlot3;
        private Brush bullPlot4;
        private Brush bullPlot5;
        private Brush bullPlot6;
        private Brush bullPlot7;
        private Brush bullPlot8;
        private Brush bullPlot9;
        private Brush bullPlot10;
        private Brush bullHeat1;
        private Brush bullHeat2;
        private Brush bullHeat3;
        private Brush bullHeat4;
        private Brush bullHeat5;
        private Brush bullHeat6;
        private Brush bullHeat7;
        private Brush bullHeat8;
        private Brush bullHeat9;
        private Brush bearPlot1;
        private Brush bearPlot2;
        private Brush bearPlot3;
        private Brush bearPlot4;
        private Brush bearPlot5;
        private Brush bearPlot6;
        private Brush bearPlot7;
        private Brush bearPlot8;
        private Brush bearPlot9;
        private Brush bearPlot10;
        private Brush bearHeat1;
        private Brush bearHeat2;
        private Brush bearHeat3;
        private Brush bearHeat4;
        private Brush bearHeat5;
        private Brush bearHeat6;
        private Brush bearHeat7;
        private Brush bearHeat8;
        private Brush bearHeat9;
        private Brush textBrush;
        private Brush dashboardAreaBrush;
        private Brush dashboardOutlineBrush;
        private Brush entryBrush;
        private Brush slBrush;
        private Brush tp1Brush;
        private Brush tp2Brush;
        private Brush tp3Brush;
        private Brush tpHitBrush;
        private Brush beBrush;
        private Brush pocBrush;
        private Brush vaBrush;
        private Brush lvnBrush;

        // Manual indicator state. This avoids hard dependencies on secondary-series
        // indicator overloads and keeps request.security-style logic explicit.
        private double chartTrailAtr = double.NaN;
        private double chartTrailAtrSum;
        private int chartTrailAtrSamples;
        private double riskAtr = double.NaN;
        private double riskAtrSum;
        private int riskAtrSamples;
        private double chartEma50 = double.NaN;
        private double chartEma50Prev = double.NaN;
        private int chartEma50Samples;

        private double engineAtr = double.NaN;
        private double engineAtrSum;
        private int engineAtrSamples;
        private double engineSource = double.NaN;

        private double htfEma50 = double.NaN;
        private int htfEma50Samples;
        private double htfClose = double.NaN;
        private double htfEmaClosed = double.NaN;

        private readonly Queue<double> volumeWindow = new Queue<double>();
        private double volumeSum;
        private double volumeSmaPrev;
        private double cumulativeVolume;

        // Trend engine
        private double ts1 = double.NaN;
        private double ts2 = double.NaN;
        private double ts3 = double.NaN;
        private double ts4 = double.NaN;
        private double ts5 = double.NaN;
        private double ts6 = double.NaN;
        private double ts7 = double.NaN;
        private double ts8 = double.NaN;
        private double ts9 = double.NaN;
        private double ts10 = double.NaN;
        private int trend = 1;
        private int trendStartBar;
        private int segmentId;
        private double trendStartPrice = double.NaN;
        private double trendHigh = double.NaN;
        private double trendLow = double.NaN;

        // Retest signal state
        private int pendingLong;
        private int pendingLongDepth;
        private int pendingShort;
        private int pendingShortDepth;
        private int lastSigBar = -10000;
        private string lastSignalStr = "-";
        private int lastSignalBar = -1;
        private int lastScore;
        private int lastSignalDepth;

        // Trade engine
        private double activeEntry = double.NaN;
        private double activeSL = double.NaN;
        private double activeTP1 = double.NaN;
        private double activeTP2 = double.NaN;
        private double activeTP3 = double.NaN;
        private int activeDir;
        private int entryBarIdx = -1;
        private bool tp1Reached;
        private bool tp2Reached;
        private bool tp3Reached;
        private bool beActive;
        private bool trailActive;
        private int winCount;
        private int lossCount;
        private string formStr = "";

        // Persisted guide drawings. Pine keeps the guide until next entry unless SL clears it.
        private bool guideVisible;
        private int guideEntryBarIdx = -1;
        private double guideEntry = double.NaN;
        private double guideSL = double.NaN;
        private double guideTP1 = double.NaN;
        private double guideTP2 = double.NaN;
        private double guideTP3 = double.NaN;
        private bool guideTP1Hit;
        private bool guideTP2Hit;
        private bool guideTP3Hit;
        private bool guideBe;
        private bool guideTrail;

        // Profile state
        private readonly List<double> lvnLevels = new List<double>();
        private double pocPrice = double.NaN;
        private double vahPrice = double.NaN;
        private double valPrice = double.NaN;
        private int profileObjectSlots;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TrendFlow";
                Description = "TrendFlow " + IndicatorVersion + " - NT8 port with bounded historical drawing and segment volume profile.";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                DisplayInDataBox = true;
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;

                BandPreset = TrendFlowBandPreset.Balanced;
                BaseMultiplier = 5.0;
                BandSpacing = 0.25;
                AtrLength = 13;
                Source = TrendFlowPriceSource.Close;
                EngineTimeFrameMode = TrendFlowTimeFrameMode.Chart;
                EngineTimeFrameValue = 15;
                FlipBand = TrendFlowFlipBand.BalancedBand3;
                HigherTimeFrameBiasMode = TrendFlowTimeFrameMode.Chart;
                HigherTimeFrameBiasValue = 60;

                MinRetestScore = 80;
                RetestWindow = 8;
                SignalCooldown = 5;

                ShowProfile = true;
                ProfileRows = 30;
                ProfileWidthBars = 34;
                MaxProfileBars = 500;
                ShowPoc = true;
                ShowValueArea = true;
                ShowNodes = true;

                Theme = TrendFlowTheme.Auto;
                ShowBands = true;
                ShowBand1 = true;
                ShowBand2 = true;
                ShowBand3 = true;
                ShowBand4 = true;
                ShowBand5 = true;
                ShowBand6 = true;
                ShowBand7 = true;
                ShowBand8 = true;
                ShowBand9 = true;
                ShowBand10 = true;
                ShowHeatmap = true;
                ShowTrendPnl = false;
                ShowSignals = true;
                ShowFlipLabels = true;
                SignalLabelSize = TrendFlowSize.Small;
                RiskLabelSize = TrendFlowSize.Small;
                PlotDays = 3;
                HistoricalSignalBars = 0;

                RiskPreset = TrendFlowRiskPreset.Balanced;
                SlMode = TrendFlowSlMode.WickAnchored;
                AtrLengthRisk = 14;
                SlMultiplier = 1.5;
                Tp1Multiplier = 1.0;
                Tp2Multiplier = 2.0;
                Tp3Multiplier = 3.0;
                BreakEvenAfterTp1 = true;
                ShowSlTpLines = true;
                ShowSlTpLabels = true;
                ShowPctOnLabels = true;
                EntryLineStyle = TrendFlowLineStyle.Dotted;
                SlLineStyle = TrendFlowLineStyle.Solid;
                TpLineStyle = TrendFlowLineStyle.Dashed;

                ShowDashboard = true;
                DashboardPosition = TrendFlowDashPosition.TopRight;
                DashboardFontSize = TrendFlowSize.Small;
                ShowMarketSection = true;
                ShowProfileSection = true;
                ShowTradeSection = true;
                ShowStatsSection = true;

                EnableAlerts = false;
                WebhookJsonFormat = false;
                AlertOnTrendFlips = true;
                AlertOnSlHitOrReversal = true;
                AlertOnTpOrBreakEven = false;
                AlertOnLvnBreak = false;

                BullBrush = FrozenBrush(0x00, 0xE6, 0x76);
                BearBrush = FrozenBrush(0xFF, 0x52, 0x52);

                HvnThreshold = 0.55;
                LvnThreshold = 0.30;

                AddPlot(new Stroke(Brushes.LimeGreen, 1), PlotStyle.Line, "Band1");
                AddPlot(new Stroke(Brushes.LimeGreen, 1), PlotStyle.Line, "Band2");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band3");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band4");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band5");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band6");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band7");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band8");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band9");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "Band10");
            }
            else if (State == State.Configure)
            {
                int nextBip = 1;
                if (EngineTimeFrameMode != TrendFlowTimeFrameMode.Chart)
                {
                    engineBip = nextBip++;
                    AddDataSeries(ToBarsPeriodType(EngineTimeFrameMode), Math.Max(1, EngineTimeFrameValue));
                }

                if (HigherTimeFrameBiasMode != TrendFlowTimeFrameMode.Chart)
                {
                    htfBip = nextBip++;
                    AddDataSeries(ToBarsPeriodType(HigherTimeFrameBiasMode), Math.Max(1, HigherTimeFrameBiasValue));
                }
            }
            else if (State == State.DataLoaded)
            {
                BullBrush = EnsureFrozen(BullBrush);
                BearBrush = EnsureFrozen(BearBrush);

                band1Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band2Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band3Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band4Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band5Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band6Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band7Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band8Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band9Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
                band10Series = new Series<double>(this, MaximumBarsLookBack.Infinite);

                signalFont = new SimpleFont("Arial", FontSize(SignalLabelSize));
                labelFont = new SimpleFont("Arial", FontSize(RiskLabelSize));
                profileFont = new SimpleFont("Arial", Math.Max(9, FontSize(SignalLabelSize) - 1));
                dashboardFont = new SimpleFont("Consolas", FontSize(DashboardFontSize));

                BuildBrushCache();
                ResetRuntimeState();

                plotCutoff = PlotDays > 0 && Bars != null && Bars.Count > 0
                    ? Bars.GetTime(Bars.Count - 1).AddDays(-PlotDays)
                    : DateTime.MinValue;
            }
            else if (State == State.Terminated)
            {
                ClearProfileDrawings();
                ClearGuideDrawings();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == engineBip)
            {
                UpdateEngineSeries();
                return;
            }

            if (BarsInProgress == htfBip)
            {
                UpdateHtfSeries();
                return;
            }

            if (BarsInProgress != 0)
                return;

            if (CurrentBar == 0)
                ResetRuntimeState();

            UpdatePrimarySeries();

            bool useEngineTimeFrame = EngineTimeFrameMode != TrendFlowTimeFrameMode.Chart;
            double src = useEngineTimeFrame && !double.IsNaN(engineSource) ? engineSource : GetSource(0);
            double trailAtr = useEngineTimeFrame ? engineAtr : chartTrailAtr;
            if (double.IsNaN(trailAtr))
                trailAtr = 0.0;

            double effBase, effStep;
            ResolveBandGeometry(out effBase, out effStep);
            double[] multipliers = new double[10];
            double[] upper = new double[10];
            double[] lower = new double[10];
            for (int i = 0; i < 10; i++)
            {
                multipliers[i] = effBase * (1.0 + i * effStep);
                upper[i] = src - trailAtr * multipliers[i];
                lower[i] = src + trailAtr * multipliers[i];
            }

            int flipChoice = ResolveFlipChoice();
            int warmupBars = Math.Max(AtrLength * 3, 60);
            bool isWarmedUp = CurrentBar >= warmupBars;

            int trendPrev = trend;
            double flipPrev = GetBandValue(flipChoice);
            bool doFlipDown = trend == 1 && !double.IsNaN(flipPrev) && src < flipPrev;
            bool doFlipUp = trend == -1 && !double.IsNaN(flipPrev) && src > flipPrev;

            if (trend == 1)
            {
                if (doFlipDown)
                {
                    trend = -1;
                    SetBandValues(lower);
                }
                else
                {
                    ts1 = Math.Max(upper[0], double.IsNaN(ts1) ? upper[0] : ts1);
                    ts2 = Math.Max(upper[1], double.IsNaN(ts2) ? upper[1] : ts2);
                    ts3 = Math.Max(upper[2], double.IsNaN(ts3) ? upper[2] : ts3);
                    ts4 = Math.Max(upper[3], double.IsNaN(ts4) ? upper[3] : ts4);
                    ts5 = Math.Max(upper[4], double.IsNaN(ts5) ? upper[4] : ts5);
                    ts6 = Math.Max(upper[5], double.IsNaN(ts6) ? upper[5] : ts6);
                    ts7 = Math.Max(upper[6], double.IsNaN(ts7) ? upper[6] : ts7);
                    ts8 = Math.Max(upper[7], double.IsNaN(ts8) ? upper[7] : ts8);
                    ts9 = Math.Max(upper[8], double.IsNaN(ts9) ? upper[8] : ts9);
                    ts10 = Math.Max(upper[9], double.IsNaN(ts10) ? upper[9] : ts10);
                }
            }
            else
            {
                if (doFlipUp)
                {
                    trend = 1;
                    SetBandValues(upper);
                }
                else
                {
                    ts1 = Math.Min(lower[0], double.IsNaN(ts1) ? lower[0] : ts1);
                    ts2 = Math.Min(lower[1], double.IsNaN(ts2) ? lower[1] : ts2);
                    ts3 = Math.Min(lower[2], double.IsNaN(ts3) ? lower[2] : ts3);
                    ts4 = Math.Min(lower[3], double.IsNaN(ts4) ? lower[3] : ts4);
                    ts5 = Math.Min(lower[4], double.IsNaN(ts5) ? lower[4] : ts5);
                    ts6 = Math.Min(lower[5], double.IsNaN(ts6) ? lower[5] : ts6);
                    ts7 = Math.Min(lower[6], double.IsNaN(ts7) ? lower[6] : ts7);
                    ts8 = Math.Min(lower[7], double.IsNaN(ts8) ? lower[7] : ts8);
                    ts9 = Math.Min(lower[8], double.IsNaN(ts9) ? lower[8] : ts9);
                    ts10 = Math.Min(lower[9], double.IsNaN(ts10) ? lower[9] : ts10);
                }
            }

            bool flipBar = trend != trendPrev;
            if (flipBar)
            {
                trendStartBar = CurrentBar;
                trendStartPrice = Close[0];
                trendHigh = High[0];
                trendLow = Low[0];
                segmentId++;
            }
            else
            {
                trendHigh = Math.Max(double.IsNaN(trendHigh) ? High[0] : trendHigh, High[0]);
                trendLow = Math.Min(double.IsNaN(trendLow) ? Low[0] : trendLow, Low[0]);
            }

            int barsInTrend = CurrentBar - trendStartBar;
            UpdateBandSeries();

            int biasDir = ResolveBiasDirection();
            bool symbolHasVolume = cumulativeVolume > 0;
            double volRaw = Volume[0];
            double volBase = volumeSmaPrev;

            bool confirmedBullFlip = flipBar && trend == 1 && isWarmedUp;
            bool confirmedBearFlip = flipBar && trend == -1 && isWarmedUp;

            UpdatePendingRetests(flipBar);

            int touchDepthL = trend == 1 && !double.IsNaN(ts1) ? LongTouchDepth() : 0;
            int touchDepthS = trend == -1 && !double.IsNaN(ts1) ? ShortTouchDepth() : 0;

            if (touchDepthL > 0)
            {
                pendingLong = RetestWindow;
                pendingLongDepth = Math.Max(pendingLongDepth, touchDepthL);
            }

            if (touchDepthS > 0)
            {
                pendingShort = RetestWindow;
                pendingShortDepth = Math.Max(pendingShortDepth, touchDepthS);
            }

            bool longReclaim = pendingLong > 0 && trend == 1 && !double.IsNaN(ts1) && Close[0] > ts1 && Close[0] > Open[0];
            bool shortReclaim = pendingShort > 0 && trend == -1 && !double.IsNaN(ts1) && Close[0] < ts1 && Close[0] < Open[0];

            double barRange = High[0] - Low[0];
            double clrL = barRange > 0 ? (Close[0] - Low[0]) / barRange : 0.5;
            double clrS = barRange > 0 ? (High[0] - Close[0]) / barRange : 0.5;

            int depthPtsL = ScoreDepth(pendingLongDepth);
            int depthPtsS = ScoreDepth(pendingShortDepth);
            int candlePtsL = clrL > 0.7 ? 20 : clrL > 0.5 ? 12 : 5;
            int candlePtsS = clrS > 0.7 ? 20 : clrS > 0.5 ? 12 : 5;
            int agePts = barsInTrend >= 10 && barsInTrend <= 150 ? 15 : barsInTrend < 10 ? 8 : 5;
            int volPts = symbolHasVolume ? (volRaw > volBase * 1.2 ? 20 : volRaw > volBase ? 12 : 5) : 12;
            int biasPtsL = biasDir == 1 ? 20 : biasDir == 0 ? 10 : 0;
            int biasPtsS = biasDir == -1 ? 20 : biasDir == 0 ? 10 : 0;
            int longScoreCalc = depthPtsL + candlePtsL + agePts + volPts + biasPtsL;
            int shortScoreCalc = depthPtsS + candlePtsS + agePts + volPts + biasPtsS;
            bool cooldownOk = CurrentBar - lastSigBar >= SignalCooldown;

            bool confirmedLongRetest = longReclaim && isWarmedUp && cooldownOk && longScoreCalc >= MinRetestScore;
            bool confirmedShortRetest = shortReclaim && isWarmedUp && cooldownOk && shortScoreCalc >= MinRetestScore;

            if (confirmedLongRetest)
            {
                lastSignalStr = "Long Retest";
                lastSignalBar = CurrentBar;
                lastScore = longScoreCalc;
                lastSignalDepth = pendingLongDepth;
                lastSigBar = CurrentBar;
                pendingLong = 0;
                pendingLongDepth = 0;
            }

            if (confirmedShortRetest)
            {
                lastSignalStr = "Short Retest";
                lastSignalBar = CurrentBar;
                lastScore = shortScoreCalc;
                lastSignalDepth = pendingShortDepth;
                lastSigBar = CurrentBar;
                pendingShort = 0;
                pendingShortDepth = 0;
            }

            if (confirmedBullFlip)
            {
                lastSignalStr = "Bull Flip";
                lastSignalBar = CurrentBar;
                lastScore = 0;
            }

            if (confirmedBearFlip)
            {
                lastSignalStr = "Bear Flip";
                lastSignalBar = CurrentBar;
                lastScore = 0;
            }

            bool beJustActivated;
            bool tp1FirstTouch;
            bool tp2FirstTouch;
            bool tp3FirstTouch;
            bool evSlHit;
            bool evReversal;
            bool evLongEntry;
            bool evShortEntry;
            double exitEntry;
            int exitDir;
            bool exitBeActive;
            bool exitTrailActive;
            double hitSL;
            double hitEntry;
            RunTradeEngine(confirmedLongRetest, confirmedShortRetest, confirmedBullFlip, confirmedBearFlip,
                out beJustActivated, out tp1FirstTouch, out tp2FirstTouch, out tp3FirstTouch,
                out evSlHit, out evReversal, out evLongEntry, out evShortEntry,
                out exitEntry, out exitDir, out exitBeActive, out exitTrailActive, out hitSL, out hitEntry);

            double currentRR = activeDir != 0 ? EffectiveTp1Multiplier() : 0.0;
            double riskPct = activeDir != 0 && !double.IsNaN(activeEntry) && activeEntry != 0 && !double.IsNaN(activeSL)
                ? Math.Abs(activeEntry - activeSL) / activeEntry * 100.0
                : 0.0;

            bool canPlot = ShouldPlotCurrentBar();
            if (canPlot && firstDrawBar < 0)
                firstDrawBar = CurrentBar;

            PlotBands(canPlot, flipBar);
            DrawVisuals(canPlot, confirmedBullFlip, confirmedBearFlip, confirmedLongRetest, confirmedShortRetest,
                evLongEntry, evShortEntry, longScoreCalc, shortScoreCalc);

            if (evSlHit)
                ClearGuideDrawings();

            if (ShowSlTpLines && guideVisible && ShouldDrawActiveObject())
                DrawTradeGuide();
            else if (!ShowSlTpLines)
                ClearGuideDrawings();

            bool lvnCross = false;
            double lvnCrossLvl = double.NaN;
            if (ShouldDrawActiveObject())
                ComputeAndDrawProfile(out lvnCross, out lvnCrossLvl);

            if (ShowDashboard && ShouldDrawActiveObject())
                DrawDashboard(barsInTrend, biasDir, currentRR, riskPct);
            else if (!ShowDashboard)
                RemoveDrawObject("TF_Dash");

            FireAlerts(beJustActivated, tp1FirstTouch, tp2FirstTouch, tp3FirstTouch, evSlHit, evReversal,
                evLongEntry, evShortEntry, confirmedBullFlip, confirmedBearFlip, lvnCross, lvnCrossLvl,
                exitEntry, exitDir, exitBeActive, exitTrailActive, hitSL, hitEntry);
        }

        private void ResetRuntimeState()
        {
            chartTrailAtr = double.NaN;
            chartTrailAtrSum = 0;
            chartTrailAtrSamples = 0;
            riskAtr = double.NaN;
            riskAtrSum = 0;
            riskAtrSamples = 0;
            chartEma50 = double.NaN;
            chartEma50Prev = double.NaN;
            chartEma50Samples = 0;
            engineAtr = double.NaN;
            engineAtrSum = 0;
            engineAtrSamples = 0;
            engineSource = double.NaN;
            htfEma50 = double.NaN;
            htfEma50Samples = 0;
            htfClose = double.NaN;
            htfEmaClosed = double.NaN;
            volumeWindow.Clear();
            volumeSum = 0;
            volumeSmaPrev = 0;
            cumulativeVolume = 0;

            ts1 = double.NaN; ts2 = double.NaN; ts3 = double.NaN; ts4 = double.NaN; ts5 = double.NaN;
            ts6 = double.NaN; ts7 = double.NaN; ts8 = double.NaN; ts9 = double.NaN; ts10 = double.NaN;
            trend = 1;
            trendStartBar = 0;
            segmentId = 0;
            trendStartPrice = double.NaN;
            trendHigh = double.NaN;
            trendLow = double.NaN;

            pendingLong = 0; pendingLongDepth = 0; pendingShort = 0; pendingShortDepth = 0;
            lastSigBar = -10000;
            lastSignalStr = "-";
            lastSignalBar = -1;
            lastScore = 0;
            lastSignalDepth = 0;

            ResetTradeState();
            winCount = 0;
            lossCount = 0;
            formStr = "";
            guideVisible = false;
            firstDrawBar = -1;
            ClearProfileState();
        }

        private void ResetTradeState()
        {
            activeEntry = double.NaN;
            activeSL = double.NaN;
            activeTP1 = double.NaN;
            activeTP2 = double.NaN;
            activeTP3 = double.NaN;
            activeDir = 0;
            entryBarIdx = -1;
            tp1Reached = false;
            tp2Reached = false;
            tp3Reached = false;
            beActive = false;
            trailActive = false;
        }

        private void UpdatePrimarySeries()
        {
            UpdateRmaAtr(0, AtrLength, ref chartTrailAtr, ref chartTrailAtrSum, ref chartTrailAtrSamples);
            UpdateRmaAtr(0, AtrLengthRisk, ref riskAtr, ref riskAtrSum, ref riskAtrSamples);
            chartEma50Prev = chartEma50;
            UpdateEma(0, 50, ref chartEma50, ref chartEma50Samples);

            double vol = Math.Max(0.0, Volume[0]);
            double previousVolumeSma = volumeWindow.Count == 20 ? volumeSum / 20.0 : double.NaN;
            volumeWindow.Enqueue(vol);
            volumeSum += vol;
            while (volumeWindow.Count > 20)
                volumeSum -= volumeWindow.Dequeue();
            double currentVolumeSma = volumeWindow.Count == 20 ? volumeSum / 20.0 : double.NaN;
            volumeSmaPrev = !double.IsNaN(previousVolumeSma) ? previousVolumeSma : !double.IsNaN(currentVolumeSma) ? currentVolumeSma : 0.0;
            cumulativeVolume += vol;
        }

        private void UpdateEngineSeries()
        {
            if (engineBip < 0)
                return;
            UpdateRmaAtr(engineBip, AtrLength, ref engineAtr, ref engineAtrSum, ref engineAtrSamples);
            engineSource = GetSource(engineBip);
        }

        private void UpdateHtfSeries()
        {
            if (htfBip < 0)
                return;
            UpdateEma(htfBip, 50, ref htfEma50, ref htfEma50Samples);
            htfClose = Closes[htfBip][0];
            htfEmaClosed = htfEma50;
        }

        private void UpdateRmaAtr(int bip, int period, ref double value, ref double sum, ref int samples)
        {
            if (CurrentBars[bip] < 0)
                return;

            double h = Highs[bip][0];
            double l = Lows[bip][0];
            double prevClose = CurrentBars[bip] > 0 ? Closes[bip][1] : Closes[bip][0];
            double tr = Math.Max(h - l, Math.Max(Math.Abs(h - prevClose), Math.Abs(l - prevClose)));

            if (samples < period)
            {
                sum += tr;
                samples++;
                value = samples == period ? sum / period : double.NaN;
            }
            else
            {
                value = (value * (period - 1) + tr) / period;
            }
        }

        private void UpdateEma(int bip, int period, ref double value, ref int samples)
        {
            double close = Closes[bip][0];
            double alpha = 2.0 / (period + 1.0);
            if (samples == 0 || double.IsNaN(value))
                value = close;
            else
                value = value + alpha * (close - value);
            samples++;
        }

        private void ResolveBandGeometry(out double effBase, out double effStep)
        {
            switch (BandPreset)
            {
                case TrendFlowBandPreset.Scalping:
                    effBase = 2.5; effStep = 0.20; return;
                case TrendFlowBandPreset.DeepTrend:
                    effBase = 6.0; effStep = 0.30; return;
                case TrendFlowBandPreset.Custom:
                    effBase = BaseMultiplier; effStep = BandSpacing; return;
                default:
                    effBase = 4.0; effStep = 0.25; return;
            }
        }

        private int ResolveFlipChoice()
        {
            switch (FlipBand)
            {
                case TrendFlowFlipBand.FastBand2: return 2;
                case TrendFlowFlipBand.DeepBand4: return 4;
                case TrendFlowFlipBand.ExtremeBand5: return 5;
                case TrendFlowFlipBand.MaximumBand6: return 6;
                case TrendFlowFlipBand.Band7: return 7;
                case TrendFlowFlipBand.Band8: return 8;
                case TrendFlowFlipBand.Band9: return 9;
                case TrendFlowFlipBand.Band10: return 10;
                default: return 3;
            }
        }

        private double GetBandValue(int band)
        {
            switch (band)
            {
                case 2: return ts2;
                case 3: return ts3;
                case 4: return ts4;
                case 5: return ts5;
                case 6: return ts6;
                case 7: return ts7;
                case 8: return ts8;
                case 9: return ts9;
                case 10: return ts10;
                default: return ts1;
            }
        }

        private void SetBandValues(double[] values)
        {
            if (values == null || values.Length < 10)
                return;
            ts1 = values[0];
            ts2 = values[1];
            ts3 = values[2];
            ts4 = values[3];
            ts5 = values[4];
            ts6 = values[5];
            ts7 = values[6];
            ts8 = values[7];
            ts9 = values[8];
            ts10 = values[9];
        }

        private int LongTouchDepth()
        {
            int depth = 0;
            if (Low[0] <= ts1) depth = 1;
            if (Low[0] <= ts2) depth = 2;
            if (Low[0] <= ts3) depth = 3;
            if (Low[0] <= ts4) depth = 4;
            if (Low[0] <= ts5) depth = 5;
            if (Low[0] <= ts6) depth = 6;
            if (Low[0] <= ts7) depth = 7;
            if (Low[0] <= ts8) depth = 8;
            if (Low[0] <= ts9) depth = 9;
            if (Low[0] <= ts10) depth = 10;
            return depth;
        }

        private int ShortTouchDepth()
        {
            int depth = 0;
            if (High[0] >= ts1) depth = 1;
            if (High[0] >= ts2) depth = 2;
            if (High[0] >= ts3) depth = 3;
            if (High[0] >= ts4) depth = 4;
            if (High[0] >= ts5) depth = 5;
            if (High[0] >= ts6) depth = 6;
            if (High[0] >= ts7) depth = 7;
            if (High[0] >= ts8) depth = 8;
            if (High[0] >= ts9) depth = 9;
            if (High[0] >= ts10) depth = 10;
            return depth;
        }

        private double FlipLabelLevel()
        {
            switch (ResolveFlipChoice())
            {
                case 5: return ts5;
                case 6: return ts6;
                case 7: return ts7;
                case 8: return ts8;
                case 9: return ts9;
                case 10: return ts10;
                default: return ts4;
            }
        }

        private int ResolveBiasDirection()
        {
            if (HigherTimeFrameBiasMode == TrendFlowTimeFrameMode.Chart)
            {
                if (CurrentBar < 1 || double.IsNaN(chartEma50Prev))
                    return 0;
                return Close[1] > chartEma50Prev ? 1 : -1;
            }

            if (double.IsNaN(htfClose) || double.IsNaN(htfEmaClosed))
                return 0;
            return htfClose > htfEmaClosed ? 1 : -1;
        }

        private void UpdateBandSeries()
        {
            band1Series[0] = ts1;
            band2Series[0] = ts2;
            band3Series[0] = ts3;
            band4Series[0] = ts4;
            band5Series[0] = ts5;
            band6Series[0] = ts6;
            band7Series[0] = ts7;
            band8Series[0] = ts8;
            band9Series[0] = ts9;
            band10Series[0] = ts10;
        }

        private void UpdatePendingRetests(bool flipBar)
        {
            pendingLong = Math.Max(pendingLong - 1, 0);
            pendingShort = Math.Max(pendingShort - 1, 0);

            if (pendingLong == 0)
                pendingLongDepth = 0;
            if (pendingShort == 0)
                pendingShortDepth = 0;

            if (flipBar)
            {
                pendingLong = 0;
                pendingLongDepth = 0;
                pendingShort = 0;
                pendingShortDepth = 0;
            }
        }

        private void RunTradeEngine(bool confirmedLongRetest, bool confirmedShortRetest, bool confirmedBullFlip, bool confirmedBearFlip,
            out bool beJustActivated, out bool tp1FirstTouch, out bool tp2FirstTouch, out bool tp3FirstTouch,
            out bool evSlHit, out bool evReversal, out bool evLongEntry, out bool evShortEntry,
            out double exitEntry, out int exitDir, out bool exitBeActive, out bool exitTrailActive,
            out double hitSL, out double hitEntry)
        {
            beJustActivated = false;
            tp1FirstTouch = false;
            tp2FirstTouch = false;
            tp3FirstTouch = false;
            evSlHit = false;
            evReversal = false;
            evLongEntry = false;
            evShortEntry = false;
            hitSL = double.NaN;
            hitEntry = double.NaN;

            double effSLMult = EffectiveSlMultiplier();
            double effTP1m = EffectiveTp1Multiplier();
            double effTP2m = EffectiveTp2Multiplier();
            double effTP3m = EffectiveTp3Multiplier();
            double atrForRisk = double.IsNaN(riskAtr) ? 0.0 : riskAtr;
            double slDistance = atrForRisk * effSLMult;
            bool conservativeBandStop = RiskPreset == TrendFlowRiskPreset.Conservative && SlMode == TrendFlowSlMode.ATR;

            bool beActiveAtBarStart = beActive;
            bool trailActiveAtBarStart = trailActive;
            double effectiveSL = activeSL;

            bool canCheckHit = activeDir != 0 && entryBarIdx >= 0 && CurrentBar > entryBarIdx;
            bool slHitRaw = canCheckHit && (activeDir == 1 ? Close[0] <= effectiveSL : Close[0] >= effectiveSL);
            bool tp1HitRaw = canCheckHit && (activeDir == 1 ? High[0] >= activeTP1 : Low[0] <= activeTP1);
            bool tp2HitRaw = canCheckHit && (activeDir == 1 ? High[0] >= activeTP2 : Low[0] <= activeTP2);
            bool tp3HitRaw = canCheckHit && (activeDir == 1 ? High[0] >= activeTP3 : Low[0] <= activeTP3);

            tp1FirstTouch = tp1HitRaw && !tp1Reached && !slHitRaw;
            tp2FirstTouch = tp2HitRaw && !tp2Reached && !slHitRaw;
            tp3FirstTouch = tp3HitRaw && !tp3Reached && !slHitRaw;

            if (tp1FirstTouch) tp1Reached = true;
            if (tp2FirstTouch) tp2Reached = true;
            if (tp3FirstTouch) tp3Reached = true;

            if (BreakEvenAfterTp1 && !conservativeBandStop && tp1FirstTouch && !beActive && activeDir != 0)
            {
                activeSL = activeEntry;
                beActive = true;
                beJustActivated = true;
            }

            if (conservativeBandStop && activeDir != 0 && tp1Reached && !slHitRaw && !tp3HitRaw && !double.IsNaN(activeSL))
            {
                double trailCandidate = activeDir == 1 && !double.IsNaN(ts10) && ts10 < Close[0]
                    ? ts10
                    : activeDir == -1 && !double.IsNaN(ts10) && ts10 > Close[0] ? ts10 : double.NaN;
                if (!double.IsNaN(trailCandidate))
                {
                    activeSL = trailActive
                        ? (activeDir == 1 ? Math.Max(activeSL, trailCandidate) : Math.Min(activeSL, trailCandidate))
                        : trailCandidate;
                    trailActive = true;
                }
            }

            if (slHitRaw)
            {
                hitSL = effectiveSL;
                hitEntry = activeEntry;
            }

            exitEntry = activeEntry;
            exitDir = activeDir;
            exitBeActive = beActiveAtBarStart;
            exitTrailActive = trailActiveAtBarStart;

            evSlHit = slHitRaw;
            if ((slHitRaw || tp3HitRaw) && activeDir != 0)
            {
                AddOutcome(tp1Reached);
                guideTP1Hit = tp1Reached;
                guideTP2Hit = tp2Reached;
                guideTP3Hit = tp3Reached;
                if (slHitRaw)
                    guideVisible = false;
                ResetTradeState();
            }

            bool longEntrySignal = confirmedLongRetest || confirmedBullFlip;
            bool shortEntrySignal = confirmedShortRetest || confirmedBearFlip;
            int sigDir = longEntrySignal ? 1 : shortEntrySignal ? -1 : 0;
            bool lockUntilExit = conservativeBandStop && activeDir != 0;
            bool reverseToShort = sigDir == -1 && activeDir == 1 && !lockUntilExit;
            bool reverseToLong = sigDir == 1 && activeDir == -1 && !lockUntilExit;

            if (reverseToShort || reverseToLong)
            {
                evReversal = true;
                exitEntry = activeEntry;
                exitDir = activeDir;
                exitBeActive = beActiveAtBarStart;
                exitTrailActive = trailActiveAtBarStart;
                AddOutcome(tp1Reached);
                guideTP1Hit = tp1Reached;
                guideTP2Hit = tp2Reached;
                guideTP3Hit = tp3Reached;
                ResetTradeState();
            }

            if (longEntrySignal && activeDir == 0 && slDistance > 0)
            {
                double slAtrLong = Close[0] - slDistance;
                double slBandLong = conservativeBandStop && !double.IsNaN(ts10) && ts10 < Close[0] ? ts10 : slAtrLong;
                double slWickLong = Math.Min(Low[0] - atrForRisk * 0.25, Close[0] - atrForRisk * 0.5);
                double slLong = SlMode == TrendFlowSlMode.ATR ? slBandLong : slWickLong;
                double riskLong = Close[0] - slLong;
                if (riskLong > 0)
                {
                    activeEntry = Close[0];
                    activeSL = slLong;
                    activeTP1 = Close[0] + riskLong * effTP1m;
                    activeTP2 = Close[0] + riskLong * effTP2m;
                    activeTP3 = Close[0] + riskLong * effTP3m;
                    activeDir = 1;
                    entryBarIdx = CurrentBar;
                    tp1Reached = false; tp2Reached = false; tp3Reached = false;
                    beActive = false; trailActive = false;
                    evLongEntry = true;
                    StartGuide();
                }
            }

            if (shortEntrySignal && activeDir == 0 && slDistance > 0)
            {
                double slAtrShort = Close[0] + slDistance;
                double slBandShort = conservativeBandStop && !double.IsNaN(ts10) && ts10 > Close[0] ? ts10 : slAtrShort;
                double slWickShort = Math.Max(High[0] + atrForRisk * 0.25, Close[0] + atrForRisk * 0.5);
                double slShort = SlMode == TrendFlowSlMode.ATR ? slBandShort : slWickShort;
                double riskShort = slShort - Close[0];
                if (riskShort > 0)
                {
                    activeEntry = Close[0];
                    activeSL = slShort;
                    activeTP1 = Close[0] - riskShort * effTP1m;
                    activeTP2 = Close[0] - riskShort * effTP2m;
                    activeTP3 = Close[0] - riskShort * effTP3m;
                    activeDir = -1;
                    entryBarIdx = CurrentBar;
                    tp1Reached = false; tp2Reached = false; tp3Reached = false;
                    beActive = false; trailActive = false;
                    evShortEntry = true;
                    StartGuide();
                }
            }

            if (activeDir != 0 && guideVisible)
            {
                guideSL = activeSL;
                guideTP1Hit = tp1Reached;
                guideTP2Hit = tp2Reached;
                guideTP3Hit = tp3Reached;
                guideBe = beActive;
                guideTrail = trailActive;
            }
        }

        private void StartGuide()
        {
            guideVisible = true;
            guideEntryBarIdx = entryBarIdx;
            guideEntry = activeEntry;
            guideSL = activeSL;
            guideTP1 = activeTP1;
            guideTP2 = activeTP2;
            guideTP3 = activeTP3;
            guideTP1Hit = false;
            guideTP2Hit = false;
            guideTP3Hit = false;
            guideBe = false;
            guideTrail = false;
        }

        private void AddOutcome(bool win)
        {
            if (win)
                winCount++;
            else
                lossCount++;
            formStr += win ? "▰" : "▱";
            if (formStr.Length > FormLen)
                formStr = formStr.Substring(formStr.Length - FormLen);
        }

        private void PlotBands(bool canPlot, bool flipBar)
        {
            if (canPlot && ShowBands && !flipBar)
            {
                SetBandPlot(0, ShowBand1, ts1, trend == 1 ? bullPlot1 : bearPlot1);
                SetBandPlot(1, ShowBand2, ts2, trend == 1 ? bullPlot2 : bearPlot2);
                SetBandPlot(2, ShowBand3, ts3, trend == 1 ? bullPlot3 : bearPlot3);
                SetBandPlot(3, ShowBand4, ts4, trend == 1 ? bullPlot4 : bearPlot4);
                SetBandPlot(4, ShowBand5, ts5, trend == 1 ? bullPlot5 : bearPlot5);
                SetBandPlot(5, ShowBand6, ts6, trend == 1 ? bullPlot6 : bearPlot6);
                SetBandPlot(6, ShowBand7, ts7, trend == 1 ? bullPlot7 : bearPlot7);
                SetBandPlot(7, ShowBand8, ts8, trend == 1 ? bullPlot8 : bearPlot8);
                SetBandPlot(8, ShowBand9, ts9, trend == 1 ? bullPlot9 : bearPlot9);
                SetBandPlot(9, ShowBand10, ts10, trend == 1 ? bullPlot10 : bearPlot10);
            }
            else
            {
                ResetBandPlots();
            }

            int fillStartBar = Math.Max(trendStartBar, firstDrawBar < 0 ? trendStartBar : firstDrawBar);
            if (ShowHeatmap && ShowBands && canPlot && CurrentBar > fillStartBar)
            {
                if (ShowBand1 && ShowBand2)
                    Draw.Region(this, "TF_Heat12_" + segmentId, CurrentBar - fillStartBar, 0, band1Series, band2Series, Brushes.Transparent, trend == 1 ? bullHeat1 : bearHeat1, 12);
                if (ShowBand2 && ShowBand3)
                    Draw.Region(this, "TF_Heat23_" + segmentId, CurrentBar - fillStartBar, 0, band2Series, band3Series, Brushes.Transparent, trend == 1 ? bullHeat2 : bearHeat2, 18);
                if (ShowBand3 && ShowBand4)
                    Draw.Region(this, "TF_Heat34_" + segmentId, CurrentBar - fillStartBar, 0, band3Series, band4Series, Brushes.Transparent, trend == 1 ? bullHeat3 : bearHeat3, 24);
                if (ShowBand4 && ShowBand5)
                    Draw.Region(this, "TF_Heat45_" + segmentId, CurrentBar - fillStartBar, 0, band4Series, band5Series, Brushes.Transparent, trend == 1 ? bullHeat4 : bearHeat4, 30);
                if (ShowBand5 && ShowBand6)
                    Draw.Region(this, "TF_Heat56_" + segmentId, CurrentBar - fillStartBar, 0, band5Series, band6Series, Brushes.Transparent, trend == 1 ? bullHeat5 : bearHeat5, 36);
                if (ShowBand6 && ShowBand7)
                    Draw.Region(this, "TF_Heat67_" + segmentId, CurrentBar - fillStartBar, 0, band6Series, band7Series, Brushes.Transparent, trend == 1 ? bullHeat6 : bearHeat6, 42);
                if (ShowBand7 && ShowBand8)
                    Draw.Region(this, "TF_Heat78_" + segmentId, CurrentBar - fillStartBar, 0, band7Series, band8Series, Brushes.Transparent, trend == 1 ? bullHeat7 : bearHeat7, 48);
                if (ShowBand8 && ShowBand9)
                    Draw.Region(this, "TF_Heat89_" + segmentId, CurrentBar - fillStartBar, 0, band8Series, band9Series, Brushes.Transparent, trend == 1 ? bullHeat8 : bearHeat8, 54);
                if (ShowBand9 && ShowBand10)
                    Draw.Region(this, "TF_Heat910_" + segmentId, CurrentBar - fillStartBar, 0, band9Series, band10Series, Brushes.Transparent, trend == 1 ? bullHeat9 : bearHeat9, 60);
            }
        }

        private void SetBandPlot(int index, bool enabled, double value, Brush brush)
        {
            if (enabled && !double.IsNaN(value))
            {
                Values[index][0] = value;
                PlotBrushes[index][0] = brush;
            }
            else
            {
                Values[index].Reset();
            }
        }

        private void ResetBandPlots()
        {
            for (int i = 0; i < 10; i++)
                Values[i].Reset();
        }

        private void DrawVisuals(bool canPlot, bool confirmedBullFlip, bool confirmedBearFlip, bool confirmedLongRetest, bool confirmedShortRetest,
            bool evLongEntry, bool evShortEntry, int longScoreCalc, int shortScoreCalc)
        {
            if (!canPlot || !ShouldDrawEventObject())
                return;

            double flipLabelLevel = FlipLabelLevel();

            if (ShowFlipLabels && confirmedBullFlip)
                Draw.Text(this, "TF_BullFlip" + CurrentBar, false, "▲ FLIP", 0, flipLabelLevel, 0,
                    Brushes.Black, signalFont, TextAlignment.Center, Brushes.Transparent, BullBrush, 90);

            if (ShowFlipLabels && confirmedBearFlip)
                Draw.Text(this, "TF_BearFlip" + CurrentBar, false, "▼ FLIP", 0, flipLabelLevel, 0,
                    Brushes.White, signalFont, TextAlignment.Center, Brushes.Transparent, BearBrush, 90);

            if (ShowSignals && confirmedLongRetest)
            {
                string txt = "◆ " + longScoreCalc;
                Draw.Text(this, "TF_LongRetest" + CurrentBar, false, txt, 0, Low[0], -12,
                    Brushes.Black, signalFont, TextAlignment.Center, Brushes.Transparent, BullBrush, 90);
            }

            if (ShowSignals && confirmedShortRetest)
            {
                string txt = "◆ " + shortScoreCalc;
                Draw.Text(this, "TF_ShortRetest" + CurrentBar, false, txt, 0, High[0], 12,
                    Brushes.White, signalFont, TextAlignment.Center, Brushes.Transparent, BearBrush, 90);
            }

            if (ShowSignals && evLongEntry && !confirmedLongRetest)
                Draw.Text(this, "TF_LongEntry" + CurrentBar, false, "Long ▲", 0, High[0], 12,
                    Brushes.Black, signalFont, TextAlignment.Center, Brushes.Transparent, BullBrush, 90);

            if (ShowSignals && evShortEntry && !confirmedShortRetest)
                Draw.Text(this, "TF_ShortEntry" + CurrentBar, false, "Short ▼", 0, Low[0], -12,
                    Brushes.White, signalFont, TextAlignment.Center, Brushes.Transparent, BearBrush, 90);

            if (ShowTrendPnl && !double.IsNaN(trendStartPrice) && trendStartPrice != 0)
            {
                double pnlPct = (Close[0] - trendStartPrice) / trendStartPrice * 100.0 * trend;
                Brush pnlBrush = pnlPct >= 0 ? tpHitBrush : slBrush;
                int startAgo = CurrentBar - Math.Max(trendStartBar, firstDrawBar < 0 ? trendStartBar : firstDrawBar);
                Draw.Line(this, "TF_TrendPnlLine", false, startAgo, trendStartPrice, 0, trendStartPrice, Faded(pnlBrush, 150), DashStyleHelper.Dot, 1);
                Draw.Text(this, "TF_TrendPnlLabel", false, (trend == 1 ? "▲ " : "▼ ") + Signed(pnlPct) + "%", -2, Close[0], 0,
                    Brushes.White, signalFont, TextAlignment.Left, Brushes.Transparent, pnlBrush, 80);
            }
        }

        private void DrawTradeGuide()
        {
            if (!guideVisible || double.IsNaN(guideEntry))
                return;

            int startBar = guideEntryBarIdx >= 0 ? guideEntryBarIdx : CurrentBar;
            if (firstDrawBar >= 0)
                startBar = Math.Max(startBar, firstDrawBar);
            int startAgo = Math.Max(0, CurrentBar - startBar);
            int forwardBars = CurrentBar == guideEntryBarIdx ? LineForwardBars : LineUpdateBars;
            int endAgo = -forwardBars;
            int labelAgo = -forwardBars - LabelOffsetBars;

            Brush slDrawBrush = guideTrail ? beBrush : guideBe ? Faded(slBrush, 90) : slBrush;
            Brush tp1DrawBrush = guideTP1Hit ? tpHitBrush : tp1Brush;
            Brush tp2DrawBrush = guideTP2Hit ? tpHitBrush : tp2Brush;
            Brush tp3DrawBrush = guideTP3Hit ? tpHitBrush : tp3Brush;

            Draw.Line(this, "TF_EntryLine", false, startAgo, guideEntry, endAgo, guideEntry, entryBrush, ToDashStyle(EntryLineStyle), 1);
            Draw.Line(this, "TF_SLLine", false, startAgo, guideSL, endAgo, guideSL, slDrawBrush, ToDashStyle(SlLineStyle), 2);
            Draw.Line(this, "TF_TP1Line", false, startAgo, guideTP1, endAgo, guideTP1, tp1DrawBrush, guideTP1Hit ? DashStyleHelper.Solid : ToDashStyle(TpLineStyle), 1);
            Draw.Line(this, "TF_TP2Line", false, startAgo, guideTP2, endAgo, guideTP2, tp2DrawBrush, guideTP2Hit ? DashStyleHelper.Solid : ToDashStyle(TpLineStyle), 1);
            Draw.Line(this, "TF_TP3Line", false, startAgo, guideTP3, endAgo, guideTP3, tp3DrawBrush, guideTP3Hit ? DashStyleHelper.Solid : ToDashStyle(TpLineStyle), 1);

            if (!ShowSlTpLabels)
                return;

            string entryText = "ENTRY " + Fmt(guideEntry);
            if (guideTrail)
                entryText += " -> SL TRAIL";
            else if (guideBe)
                entryText += " -> SL BE";

            string slPrefix = guideTrail ? "SL TRAIL " : guideBe ? "SL BE " : "SL ";
            Draw.Text(this, "TF_EntryLabel", false, entryText, labelAgo, guideEntry, 0,
                Brushes.White, labelFont, TextAlignment.Left, Brushes.Transparent, entryBrush, 80);
            Draw.Text(this, "TF_SLLabel", false, slPrefix + Fmt(guideSL) + FormatPctFromEntry(guideSL, guideEntry), labelAgo, guideSL, 0,
                Brushes.White, labelFont, TextAlignment.Left, Brushes.Transparent, slDrawBrush, 80);
            Draw.Text(this, "TF_TP1Label", false, "TP1 " + (guideTP1Hit ? "HIT " : "") + Fmt(guideTP1) + FormatPctFromEntry(guideTP1, guideEntry), labelAgo, guideTP1, 0,
                Brushes.White, labelFont, TextAlignment.Left, Brushes.Transparent, tp1DrawBrush, 80);
            Draw.Text(this, "TF_TP2Label", false, "TP2 " + (guideTP2Hit ? "HIT " : "") + Fmt(guideTP2) + FormatPctFromEntry(guideTP2, guideEntry), labelAgo, guideTP2, 0,
                Brushes.White, labelFont, TextAlignment.Left, Brushes.Transparent, tp2DrawBrush, 80);
            Draw.Text(this, "TF_TP3Label", false, "TP3 " + (guideTP3Hit ? "HIT " : "") + Fmt(guideTP3) + FormatPctFromEntry(guideTP3, guideEntry), labelAgo, guideTP3, 0,
                Brushes.White, labelFont, TextAlignment.Left, Brushes.Transparent, tp3DrawBrush, 80);
        }

        private void ComputeAndDrawProfile(out bool lvnCross, out double lvnCrossLvl)
        {
            lvnCross = false;
            lvnCrossLvl = double.NaN;

            bool profileNeeded = ShowProfile || ShowPoc || ShowValueArea || ShowNodes || AlertOnLvnBreak || ShowDashboard;
            if (!profileNeeded)
            {
                ClearProfileDrawings();
                ClearProfileState();
                return;
            }

            ClearProfileDrawings();
            ClearProfileState();

            int lookback = Math.Min(Math.Min(CurrentBar - trendStartBar + 1, MaxProfileBars), CurrentBar + 1);
            if (lookback < 3 || ProfileRows < 3)
                return;

            double wHi = High[0];
            double wLo = Low[0];
            for (int i = 0; i < lookback; i++)
            {
                wHi = Math.Max(wHi, High[i]);
                wLo = Math.Min(wLo, Low[i]);
            }

            double binSize = (wHi - wLo) / ProfileRows;
            if (binSize <= 0)
                return;

            double[] vols = new double[ProfileRows];
            bool symbolHasVolume = cumulativeVolume > 0;
            for (int i = 0; i < lookback; i++)
            {
                double bH = High[i];
                double bL = Low[i];
                double w = symbolHasVolume ? Math.Max(0.0, Volume[i]) : Math.Max(bH - bL, TickSize);
                if (w <= 0)
                    continue;

                double rng = bH - bL;
                if (rng <= 0)
                {
                    int cIdx = ClampInt((int)Math.Floor((Close[i] - wLo) / binSize), 0, ProfileRows - 1);
                    vols[cIdx] += w;
                }
                else
                {
                    int i0 = ClampInt((int)Math.Floor((bL - wLo) / binSize), 0, ProfileRows - 1);
                    int i1 = ClampInt((int)Math.Floor((bH - wLo) / binSize), 0, ProfileRows - 1);
                    for (int bn = i0; bn <= i1; bn++)
                    {
                        double bin0 = wLo + binSize * bn;
                        double bin1 = bin0 + binSize;
                        double overlap = Math.Max(0.0, Math.Min(bH, bin1) - Math.Max(bL, bin0));
                        if (overlap > 0)
                            vols[bn] += w * overlap / rng;
                    }
                }
            }

            double maxVol = 0;
            double totalVol = 0;
            int pocIdx = 0;
            for (int i = 0; i < ProfileRows; i++)
            {
                totalVol += vols[i];
                if (vols[i] > maxVol)
                {
                    maxVol = vols[i];
                    pocIdx = i;
                }
            }

            if (maxVol <= 0 || totalVol <= 0)
                return;

            pocPrice = wLo + binSize * (pocIdx + 0.5);

            double vaTarget = totalVol * 0.70;
            double vaAcc = vols[pocIdx];
            int upIdx = pocIdx + 1;
            int dnIdx = pocIdx - 1;
            while (vaAcc < vaTarget)
            {
                double vu = upIdx <= ProfileRows - 1 ? vols[upIdx] : -1.0;
                double vd = dnIdx >= 0 ? vols[dnIdx] : -1.0;
                if (vu < 0 && vd < 0)
                    break;
                if (vu >= vd)
                {
                    vaAcc += Math.Max(vu, 0.0);
                    upIdx++;
                }
                else
                {
                    vaAcc += Math.Max(vd, 0.0);
                    dnIdx--;
                }
            }

            int vahIdx = upIdx - 1;
            int valIdx = dnIdx + 1;
            vahPrice = wLo + binSize * (vahIdx + 1);
            valPrice = wLo + binSize * valIdx;

            int segStartAgo = lookback - 1;
            int profOffset = ProfileWidthBars + 8;
            int obj = 0;

            if (ShowValueArea)
            {
                Draw.Rectangle(this, "TF_ProfileVA", false, segStartAgo, vahPrice, -profOffset, valPrice,
                    Brushes.Transparent, Faded(vaBrush, 38), 15);
                Draw.Line(this, "TF_ProfileVAH", false, segStartAgo, vahPrice, -profOffset, vahPrice, vaBrush, DashStyleHelper.Dash, 1);
                Draw.Line(this, "TF_ProfileVAL", false, segStartAgo, valPrice, -profOffset, valPrice, vaBrush, DashStyleHelper.Dash, 1);
            }

            Brush trendBrush = trend == 1 ? BullBrush : BearBrush;
            for (int i = 0; i < ProfileRows; i++)
            {
                double v = vols[i];
                double bTop = wLo + binSize * (i + 1);
                double bBot = wLo + binSize * i;
                double bMid = (bTop + bBot) / 2.0;
                int bw = (int)Math.Round(v / maxVol * ProfileWidthBars);
                int leftAgo = -profOffset + bw;
                int rightAgo = -profOffset;

                if (ShowProfile && bw > 0)
                {
                    Brush binBrush = GradientBrush(trendBrush, v / maxVol, 45, 205);
                    Draw.Rectangle(this, "TF_ProfileBin" + obj, false, leftAgo, bTop, rightAgo, bBot,
                        Brushes.Transparent, binBrush, 70);
                    obj++;
                }

                bool isLocalMax = i > 0 && i < ProfileRows - 1 && v > vols[i - 1] && v > vols[i + 1];
                bool isLocalMin = i > 0 && i < ProfileRows - 1 && v < vols[i - 1] && v < vols[i + 1];
                bool insideVA = bMid <= vahPrice && bMid >= valPrice;

                if (ShowNodes && isLocalMax && v >= maxVol * HvnThreshold && i != pocIdx)
                {
                    Draw.Line(this, "TF_ProfileHVN" + obj, false, segStartAgo, bMid, leftAgo, bMid,
                        Faded(trendBrush, 160), DashStyleHelper.Dot, 1);
                    obj++;
                }

                if (ShowNodes && isLocalMin && v <= maxVol * LvnThreshold && insideVA)
                {
                    Draw.Line(this, "TF_ProfileLVN" + obj, false, segStartAgo, bMid, leftAgo, bMid,
                        lvnBrush, DashStyleHelper.Dot, 1);
                    Draw.Text(this, "TF_ProfileLVNLbl" + obj, false, "LVN", -profOffset - 1, bMid, 0,
                        lvnBrush, profileFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                    lvnLevels.Add(bMid);
                    obj++;
                }
            }

            if (ShowPoc)
            {
                Draw.Line(this, "TF_ProfilePOC", false, segStartAgo, pocPrice, -profOffset, pocPrice, pocBrush, DashStyleHelper.Solid, 2);
                string pocVolStr = symbolHasVolume ? maxVol.ToString("0") : "range-wt";
                Draw.Text(this, "TF_ProfilePOCLbl", false, "POC " + Fmt(pocPrice) + " " + pocVolStr, -profOffset - 1, pocPrice, 0,
                    pocBrush, profileFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }

            profileObjectSlots = Math.Max(profileObjectSlots, obj + 5);

            if (AlertOnLvnBreak && CurrentBar > 0)
            {
                for (int i = 0; i < lvnLevels.Count; i++)
                {
                    double lvl = lvnLevels[i];
                    bool crossedUp = Close[1] < lvl && Close[0] >= lvl;
                    bool crossedDown = Close[1] > lvl && Close[0] <= lvl;
                    if (crossedUp || crossedDown)
                    {
                        lvnCross = true;
                        lvnCrossLvl = lvl;
                        break;
                    }
                }
            }
        }

        private void DrawDashboard(int barsInTrend, int biasDir, double currentRR, double riskPct)
        {
            bool dark = IsDarkTheme();
            Brush dashText = dark ? textBrush : Brushes.Black;
            int closedTrades = winCount + lossCount;
            double winRate = closedTrades > 0 ? winCount / (double)closedTrades * 100.0 : 0.0;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TrendFlow " + IndicatorVersion + " | " + (trend == 1 ? "Bullish" : "Bearish"));
            if (ShowMarketSection)
            {
                sb.AppendLine("-- Market --");
                sb.AppendLine("Trend:      " + (trend == 1 ? "Bullish" : "Bearish"));
                sb.AppendLine("Age:        " + barsInTrend + " bars");
                sb.AppendLine("HTF Bias:   " + (biasDir == 1 ? "Bullish" : biasDir == -1 ? "Bearish" : "-"));
                sb.AppendLine("Signal:     " + (activeDir == 1 ? "LONG" : activeDir == -1 ? "SHORT" : "Wait"));
                sb.AppendLine("Last:       " + LastSignalText());
            }

            if (ShowProfileSection)
            {
                sb.AppendLine("-- Profile --");
                sb.AppendLine("POC:        " + FmtOrDash(pocPrice));
                sb.AppendLine("VA High:    " + FmtOrDash(vahPrice));
                sb.AppendLine("VA Low:     " + FmtOrDash(valPrice));
            }

            if (ShowTradeSection)
            {
                sb.AppendLine("-- Trade --");
                if (activeDir != 0)
                {
                    sb.AppendLine("Entry:      " + Fmt(activeEntry));
                    sb.AppendLine("SL:         " + (trailActive ? "TRAIL @ " : beActive ? "BE @ " : "") + Fmt(activeSL));
                    sb.AppendLine("TP1:        " + (tp1Reached ? "HIT " : "") + Fmt(activeTP1));
                    sb.AppendLine("TP2:        " + (tp2Reached ? "HIT " : "") + Fmt(activeTP2));
                    sb.AppendLine("TP3:        " + (tp3Reached ? "HIT " : "") + Fmt(activeTP3));
                    sb.AppendLine("R:R TP1:    " + currentRR.ToString("0.0") + "R");
                    sb.AppendLine("SL Dist:    " + riskPct.ToString("0.##") + "%");
                }
                else
                {
                    sb.AppendLine("Flat - waiting for retest");
                }
            }

            if (ShowStatsSection)
            {
                sb.AppendLine("-- Stats --");
                sb.AppendLine("Trades:     " + closedTrades);
                sb.AppendLine("W / L:      " + winCount + " / " + lossCount);
                sb.AppendLine("Win rate:   " + (closedTrades > 0 ? winRate.ToString("0.0") + "% " + BuildGauge(winRate, 100, 8) : "-"));
                sb.AppendLine("Form:       " + (formStr.Length == 0 ? "-" : formStr));
            }

            Draw.TextFixed(this, "TF_Dash", sb.ToString(), ToTextPosition(DashboardPosition), dashText,
                dashboardFont, dashboardOutlineBrush, dashboardAreaBrush, 70);
        }

        private void FireAlerts(bool beJustActivated, bool tp1FirstTouch, bool tp2FirstTouch, bool tp3FirstTouch,
            bool evSlHit, bool evReversal, bool evLongEntry, bool evShortEntry, bool confirmedBullFlip, bool confirmedBearFlip,
            bool lvnCross, double lvnCrossLvl, double exitEntry, int exitDir, bool exitBeActive, bool exitTrailActive,
            double hitSL, double hitEntry)
        {
            if (!EnableAlerts || State != State.Realtime)
                return;

            string ticker = Instrument != null ? Instrument.FullName : "";
            string tf = BarsPeriod != null ? BarsPeriod.ToString() : "";
            string price = Fmt(Close[0]);

            if (beJustActivated && AlertOnTpOrBreakEven)
                Alert("TF_BE" + CurrentBar, Priority.Medium, "BREAK-EVEN | " + ticker + " | SL moved to " + Fmt(exitEntry), "", 0, Brushes.Black, beBrush);

            if (tp1FirstTouch && AlertOnTpOrBreakEven)
                Alert("TF_TP1" + CurrentBar, Priority.Medium, "TP1 HIT | " + ticker + " | TP1: " + Fmt(guideTP1), "", 0, Brushes.Black, tpHitBrush);

            if (tp2FirstTouch && AlertOnTpOrBreakEven)
                Alert("TF_TP2" + CurrentBar, Priority.Medium, "TP2 HIT | " + ticker + " | TP2: " + Fmt(guideTP2), "", 0, Brushes.Black, tpHitBrush);

            if (tp3FirstTouch && AlertOnTpOrBreakEven)
                Alert("TF_TP3" + CurrentBar, Priority.High, "TP3 HIT | " + ticker + " | TP3: " + Fmt(guideTP3), "", 0, Brushes.Black, tpHitBrush);

            if (evSlHit && AlertOnSlHitOrReversal)
            {
                string slDir = exitDir == 1 ? "Long" : "Short";
                string prefix = exitTrailActive ? "TRAIL STOP" : exitBeActive ? "BE STOP-OUT" : "SL HIT";
                Alert("TF_SL" + CurrentBar, Priority.High, prefix + " | " + ticker + " | " + slDir + " | Entry: " + Fmt(hitEntry) + " | SL: " + Fmt(hitSL), "", 0, Brushes.White, slBrush);
            }

            if (evReversal && AlertOnSlHitOrReversal)
            {
                string revDir = exitDir == 1 ? "Long -> Short" : "Short -> Long";
                Alert("TF_REV" + CurrentBar, Priority.High, "REVERSAL | " + ticker + " | " + revDir + " | Prev entry: " + Fmt(exitEntry), "", 0, Brushes.White, BearBrush);
            }

            if (evLongEntry || evShortEntry)
            {
                string action = evLongEntry ? "buy" : "sell";
                string text = (evLongEntry ? "LONG" : "SHORT") + " | " + ticker + " | TF: " + tf + " | Price: " + price
                    + " | Score: " + lastScore + " | SL: " + Fmt(activeSL) + " | TP1: " + Fmt(activeTP1)
                    + " | TP2: " + Fmt(activeTP2) + " | TP3: " + Fmt(activeTP3) + " | R:R: " + EffectiveTp1Multiplier().ToString("0.0");
                string json = "{\"action\":\"" + action + "\",\"ticker\":\"" + ticker + "\",\"tf\":\"" + tf
                    + "\",\"price\":" + Close[0].ToString("0.########") + ",\"score\":" + lastScore
                    + ",\"sl\":" + activeSL.ToString("0.########") + ",\"tp1\":" + activeTP1.ToString("0.########")
                    + ",\"tp2\":" + activeTP2.ToString("0.########") + ",\"tp3\":" + activeTP3.ToString("0.########")
                    + ",\"rr\":" + EffectiveTp1Multiplier().ToString("0.0") + "}";
                Alert("TF_ENTRY" + CurrentBar, Priority.High, WebhookJsonFormat ? json : text, "", 0, Brushes.Black, evLongEntry ? BullBrush : BearBrush);
            }

            if (confirmedBullFlip && !evLongEntry && AlertOnTrendFlips)
                Alert("TF_BULLFLIP" + CurrentBar, Priority.Medium, "BULL FLIP | " + ticker + " | TF: " + tf + " | Price: " + price, "", 0, Brushes.Black, BullBrush);

            if (confirmedBearFlip && !evShortEntry && AlertOnTrendFlips)
                Alert("TF_BEARFLIP" + CurrentBar, Priority.Medium, "BEAR FLIP | " + ticker + " | TF: " + tf + " | Price: " + price, "", 0, Brushes.White, BearBrush);

            if (lvnCross)
                Alert("TF_LVN" + CurrentBar, Priority.Medium, "LVN BREAK | " + ticker + " | TF: " + tf + " | Level: " + Fmt(lvnCrossLvl), "", 0, Brushes.Black, lvnBrush);
        }

        private bool ShouldPlotCurrentBar()
        {
            return PlotDays <= 0 || Time[0] >= plotCutoff;
        }

        private bool ShouldDrawEventObject()
        {
            if (State != State.Historical)
                return true;
            if (HistoricalSignalBars > 0 && Bars != null && Bars.Count > 0)
                return CurrentBar >= Math.Max(0, Bars.Count - HistoricalSignalBars);
            return ShouldPlotCurrentBar();
        }

        private bool ShouldDrawActiveObject()
        {
            if (State != State.Historical)
                return true;
            return CurrentBar >= Count - 2;
        }

        private void ClearProfileState()
        {
            lvnLevels.Clear();
            pocPrice = double.NaN;
            vahPrice = double.NaN;
            valPrice = double.NaN;
        }

        private void ClearProfileDrawings()
        {
            RemoveDrawObject("TF_ProfileVA");
            RemoveDrawObject("TF_ProfileVAH");
            RemoveDrawObject("TF_ProfileVAL");
            RemoveDrawObject("TF_ProfilePOC");
            RemoveDrawObject("TF_ProfilePOCLbl");
            int slots = Math.Max(profileObjectSlots, ProfileRows * 3 + 10);
            for (int i = 0; i < slots; i++)
            {
                RemoveDrawObject("TF_ProfileBin" + i);
                RemoveDrawObject("TF_ProfileHVN" + i);
                RemoveDrawObject("TF_ProfileLVN" + i);
                RemoveDrawObject("TF_ProfileLVNLbl" + i);
            }
        }

        private void ClearGuideDrawings()
        {
            RemoveDrawObject("TF_EntryLine");
            RemoveDrawObject("TF_SLLine");
            RemoveDrawObject("TF_TP1Line");
            RemoveDrawObject("TF_TP2Line");
            RemoveDrawObject("TF_TP3Line");
            RemoveDrawObject("TF_EntryLabel");
            RemoveDrawObject("TF_SLLabel");
            RemoveDrawObject("TF_TP1Label");
            RemoveDrawObject("TF_TP2Label");
            RemoveDrawObject("TF_TP3Label");
        }

        private double GetSource(int bip)
        {
            switch (Source)
            {
                case TrendFlowPriceSource.HL2:
                    return (Highs[bip][0] + Lows[bip][0]) / 2.0;
                case TrendFlowPriceSource.HLC3:
                    return (Highs[bip][0] + Lows[bip][0] + Closes[bip][0]) / 3.0;
                case TrendFlowPriceSource.OHLC4:
                    return (Opens[bip][0] + Highs[bip][0] + Lows[bip][0] + Closes[bip][0]) / 4.0;
                default:
                    return Closes[bip][0];
            }
        }

        private static int ScoreDepth(int depth)
        {
            switch (depth)
            {
                case 5: return 5;
                case 2: return 25;
                case 3: return 18;
                case 1: return 15;
                case 4: return 10;
                default: return depth >= 6 ? 2 : 0;
            }
        }

        private double EffectiveSlMultiplier()
        {
            switch (RiskPreset)
            {
                case TrendFlowRiskPreset.Conservative: return 2.5;
                case TrendFlowRiskPreset.Aggressive: return 1.0;
                case TrendFlowRiskPreset.Scalping: return 0.8;
                case TrendFlowRiskPreset.Custom: return SlMultiplier;
                default: return 1.5;
            }
        }

        private double EffectiveTp1Multiplier()
        {
            switch (RiskPreset)
            {
                case TrendFlowRiskPreset.Aggressive: return 1.5;
                case TrendFlowRiskPreset.Scalping: return 0.8;
                case TrendFlowRiskPreset.Custom: return Tp1Multiplier;
                default: return 1.0;
            }
        }

        private double EffectiveTp2Multiplier()
        {
            switch (RiskPreset)
            {
                case TrendFlowRiskPreset.Aggressive: return 2.5;
                case TrendFlowRiskPreset.Scalping: return 1.5;
                case TrendFlowRiskPreset.Custom: return Tp2Multiplier;
                default: return 2.0;
            }
        }

        private double EffectiveTp3Multiplier()
        {
            switch (RiskPreset)
            {
                case TrendFlowRiskPreset.Conservative: return 4.0;
                case TrendFlowRiskPreset.Aggressive: return 4.0;
                case TrendFlowRiskPreset.Scalping: return 2.0;
                case TrendFlowRiskPreset.Custom: return Tp3Multiplier;
                default: return 3.0;
            }
        }

        private string LastSignalText()
        {
            if (lastSignalBar < 0 || lastSignalStr == "-")
                return "-";
            string score = lastScore > 0 ? " " + lastScore : "";
            return lastSignalStr + score + " (" + (CurrentBar - lastSignalBar) + ")";
        }

        private string BuildGauge(double value, double maxVal, int width)
        {
            int filled = maxVal <= 0 ? 0 : (int)Math.Round(Clamp(value / maxVal, 0.0, 1.0) * width);
            return new string('▰', filled) + new string('▱', Math.Max(0, width - filled));
        }

        private string FormatPctFromEntry(double level, double entry)
        {
            if (!ShowPctOnLabels || double.IsNaN(level) || double.IsNaN(entry) || entry == 0)
                return "";
            double pct = (level - entry) / entry * 100.0;
            return " (" + Signed(pct) + "%)";
        }

        private string FmtOrDash(double price)
        {
            return double.IsNaN(price) ? "-" : Fmt(price);
        }

        private string Fmt(double price)
        {
            if (double.IsNaN(price) || double.IsInfinity(price))
                return "-";
            return Instrument != null
                ? Instrument.MasterInstrument.FormatPrice(Instrument.MasterInstrument.RoundToTickSize(price))
                : price.ToString("0.00");
        }

        private static string Signed(double value)
        {
            return (value >= 0 ? "+" : "") + value.ToString("0.##");
        }

        private bool IsDarkTheme()
        {
            if (Theme == TrendFlowTheme.Dark)
                return true;
            if (Theme == TrendFlowTheme.Light)
                return false;
            try
            {
                if (ChartControl != null)
                {
                    SolidColorBrush bg = ChartControl.Properties.ChartBackground as SolidColorBrush;
                    if (bg != null)
                        return bg.Color.R < 128;
                }
            }
            catch { }
            return true;
        }

        private void BuildBrushCache()
        {
            bullPlot1 = Faded(BullBrush, 77);
            bullPlot2 = Faded(BullBrush, 128);
            bullPlot3 = Faded(BullBrush, 191);
            bullPlot4 = Faded(BullBrush, 230);
            bullPlot5 = Faded(BullBrush, 242);
            bullPlot6 = Faded(BullBrush, 224);
            bullPlot7 = Faded(BullBrush, 232);
            bullPlot8 = Faded(BullBrush, 240);
            bullPlot9 = Faded(BullBrush, 247);
            bullPlot10 = BullBrush;
            bullHeat1 = Faded(BullBrush, 20);
            bullHeat2 = Faded(BullBrush, 46);
            bullHeat3 = Faded(BullBrush, 77);
            bullHeat4 = Faded(BullBrush, 102);
            bullHeat5 = Faded(BullBrush, 122);
            bullHeat6 = Faded(BullBrush, 138);
            bullHeat7 = Faded(BullBrush, 148);
            bullHeat8 = Faded(BullBrush, 158);
            bullHeat9 = Faded(BullBrush, 168);
            bearPlot1 = Faded(BearBrush, 77);
            bearPlot2 = Faded(BearBrush, 128);
            bearPlot3 = Faded(BearBrush, 191);
            bearPlot4 = Faded(BearBrush, 230);
            bearPlot5 = Faded(BearBrush, 242);
            bearPlot6 = Faded(BearBrush, 224);
            bearPlot7 = Faded(BearBrush, 232);
            bearPlot8 = Faded(BearBrush, 240);
            bearPlot9 = Faded(BearBrush, 247);
            bearPlot10 = BearBrush;
            bearHeat1 = Faded(BearBrush, 20);
            bearHeat2 = Faded(BearBrush, 46);
            bearHeat3 = Faded(BearBrush, 77);
            bearHeat4 = Faded(BearBrush, 102);
            bearHeat5 = Faded(BearBrush, 122);
            bearHeat6 = Faded(BearBrush, 138);
            bearHeat7 = Faded(BearBrush, 148);
            bearHeat8 = Faded(BearBrush, 158);
            bearHeat9 = Faded(BearBrush, 168);
            textBrush = FrozenBrush(0xE0, 0xE0, 0xE0);
            dashboardAreaBrush = Faded(FrozenBrush(0x13, 0x17, 0x22), 230);
            dashboardOutlineBrush = FrozenBrush(0x2A, 0x2E, 0x39);
            entryBrush = FrozenBrush(0x5C, 0x8A, 0xAE);
            slBrush = FrozenBrush(0xE5, 0x73, 0x73);
            tp1Brush = Faded(FrozenBrush(0x66, 0xBB, 0x6A), 175);
            tp2Brush = Faded(FrozenBrush(0x66, 0xBB, 0x6A), 150);
            tp3Brush = FrozenBrush(0x66, 0xBB, 0x6A);
            tpHitBrush = FrozenBrush(0x4D, 0xB6, 0xAC);
            beBrush = FrozenBrush(0xFF, 0xA7, 0x26);
            pocBrush = FrozenBrush(0xFF, 0xD5, 0x4F);
            vaBrush = FrozenBrush(0x90, 0xA4, 0xAE);
            lvnBrush = Faded(FrozenBrush(0xB0, 0xBE, 0xC5), 180);
        }

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Brush EnsureFrozen(Brush brush)
        {
            if (brush == null)
                return Brushes.Transparent;
            if (brush.CanFreeze && !brush.IsFrozen)
                brush.Freeze();
            return brush;
        }

        private static Brush Faded(Brush brush, byte alpha)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null)
                return brush;
            SolidColorBrush faded = new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
            faded.Freeze();
            return faded;
        }

        private static Brush GradientBrush(Brush baseBrush, double t, byte alphaLo, byte alphaHi)
        {
            t = Clamp(t, 0.0, 1.0);
            byte alpha = (byte)Math.Round(alphaLo + (alphaHi - alphaLo) * t);
            return Faded(baseBrush, alpha);
        }

        private static DashStyleHelper ToDashStyle(TrendFlowLineStyle style)
        {
            switch (style)
            {
                case TrendFlowLineStyle.Solid: return DashStyleHelper.Solid;
                case TrendFlowLineStyle.Dotted: return DashStyleHelper.Dot;
                default: return DashStyleHelper.Dash;
            }
        }

        private static TextPosition ToTextPosition(TrendFlowDashPosition pos)
        {
            switch (pos)
            {
                case TrendFlowDashPosition.TopLeft: return TextPosition.TopLeft;
                case TrendFlowDashPosition.BottomLeft: return TextPosition.BottomLeft;
                case TrendFlowDashPosition.BottomRight: return TextPosition.BottomRight;
                default: return TextPosition.TopRight;
            }
        }

        private static BarsPeriodType ToBarsPeriodType(TrendFlowTimeFrameMode mode)
        {
            switch (mode)
            {
                case TrendFlowTimeFrameMode.Day: return BarsPeriodType.Day;
                case TrendFlowTimeFrameMode.Week: return BarsPeriodType.Week;
                case TrendFlowTimeFrameMode.Month: return BarsPeriodType.Month;
                default: return BarsPeriodType.Minute;
            }
        }

        private static int FontSize(TrendFlowSize size)
        {
            switch (size)
            {
                case TrendFlowSize.Tiny: return 9;
                case TrendFlowSize.Normal: return 12;
                case TrendFlowSize.Large: return 15;
                case TrendFlowSize.Huge: return 18;
                default: return 10;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static int ClampInt(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Band Width Preset", Order = 1, GroupName = "01. Trend Engine")]
        public TrendFlowBandPreset BandPreset { get; set; }

        [Range(0.5, 15.0)]
        [NinjaScriptProperty]
        [Display(Name = "Base Multiplier", Order = 2, GroupName = "01. Trend Engine")]
        public double BaseMultiplier { get; set; }

        [Range(0.05, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Band Spacing", Order = 3, GroupName = "01. Trend Engine")]
        public double BandSpacing { get; set; }

        [Range(5, 100)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Length", Order = 4, GroupName = "01. Trend Engine")]
        public int AtrLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Source", Order = 5, GroupName = "01. Trend Engine")]
        public TrendFlowPriceSource Source { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Engine Timeframe Mode", Order = 6, GroupName = "01. Trend Engine")]
        public TrendFlowTimeFrameMode EngineTimeFrameMode { get; set; }

        [Range(1, 10000)]
        [NinjaScriptProperty]
        [Display(Name = "Engine Timeframe Value", Order = 7, GroupName = "01. Trend Engine")]
        public int EngineTimeFrameValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Band", Order = 8, GroupName = "01. Trend Engine")]
        public TrendFlowFlipBand FlipBand { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HTF Bias Mode", Order = 9, GroupName = "01. Trend Engine")]
        public TrendFlowTimeFrameMode HigherTimeFrameBiasMode { get; set; }

        [Range(1, 10000)]
        [NinjaScriptProperty]
        [Display(Name = "HTF Bias Value", Order = 10, GroupName = "01. Trend Engine")]
        public int HigherTimeFrameBiasValue { get; set; }

        [Range(0, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Min Retest Score", Order = 1, GroupName = "02. Signals")]
        public int MinRetestScore { get; set; }

        [Range(1, 30)]
        [NinjaScriptProperty]
        [Display(Name = "Retest Window", Order = 2, GroupName = "02. Signals")]
        public int RetestWindow { get; set; }

        [Range(0, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Signal Cooldown", Order = 3, GroupName = "02. Signals")]
        public int SignalCooldown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Segment Volume Profile", Order = 1, GroupName = "03. Volume Profile")]
        public bool ShowProfile { get; set; }

        [Range(10, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Profile Rows", Order = 2, GroupName = "03. Volume Profile")]
        public int ProfileRows { get; set; }

        [Range(10, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Profile Width Bars", Order = 3, GroupName = "03. Volume Profile")]
        public int ProfileWidthBars { get; set; }

        [Range(50, 2000)]
        [NinjaScriptProperty]
        [Display(Name = "Max Profile Bars", Order = 4, GroupName = "03. Volume Profile")]
        public int MaxProfileBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show POC", Order = 5, GroupName = "03. Volume Profile")]
        public bool ShowPoc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Value Area", Order = 6, GroupName = "03. Volume Profile")]
        public bool ShowValueArea { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show HVN/LVN", Order = 7, GroupName = "03. Volume Profile")]
        public bool ShowNodes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Theme", Order = 1, GroupName = "04. Visual")]
        public TrendFlowTheme Theme { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trail Bands", Order = 2, GroupName = "04. Visual")]
        public bool ShowBands { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 1", Order = 3, GroupName = "04. Visual")]
        public bool ShowBand1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 2", Order = 4, GroupName = "04. Visual")]
        public bool ShowBand2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 3", Order = 5, GroupName = "04. Visual")]
        public bool ShowBand3 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 4", Order = 6, GroupName = "04. Visual")]
        public bool ShowBand4 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 5", Order = 7, GroupName = "04. Visual")]
        public bool ShowBand5 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 6", Order = 8, GroupName = "04. Visual")]
        public bool ShowBand6 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 7", Order = 9, GroupName = "04. Visual")]
        public bool ShowBand7 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 8", Order = 10, GroupName = "04. Visual")]
        public bool ShowBand8 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 9", Order = 11, GroupName = "04. Visual")]
        public bool ShowBand9 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band 10", Order = 12, GroupName = "04. Visual")]
        public bool ShowBand10 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Heatmap Fill", Order = 13, GroupName = "04. Visual")]
        public bool ShowHeatmap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trend PnL Tracker", Order = 14, GroupName = "04. Visual")]
        public bool ShowTrendPnl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Retest Signals", Order = 15, GroupName = "04. Visual")]
        public bool ShowSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Flip Labels", Order = 16, GroupName = "04. Visual")]
        public bool ShowFlipLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Signal Label Size", Order = 17, GroupName = "04. Visual")]
        public TrendFlowSize SignalLabelSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL/TP Label Size", Order = 18, GroupName = "04. Visual")]
        public TrendFlowSize RiskLabelSize { get; set; }

        [Range(0, 60)]
        [NinjaScriptProperty]
        [Display(Name = "Plot Days", Order = 19, GroupName = "04. Visual", Description = "Historical drawing/plots window. Default 3 for fast chart loading. 0 = all loaded history.")]
        public int PlotDays { get; set; }

        [Range(0, 5000)]
        [NinjaScriptProperty]
        [Display(Name = "Historical Signal Bars", Order = 20, GroupName = "04. Visual", Description = "Optional bar-count cap for historical signal objects. 0 = use Plot Days.")]
        public int HistoricalSignalBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Risk Preset", Order = 1, GroupName = "05. Risk Management")]
        public TrendFlowRiskPreset RiskPreset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 2, GroupName = "05. Risk Management")]
        public TrendFlowSlMode SlMode { get; set; }

        [Range(5, 50)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Length Risk", Order = 3, GroupName = "05. Risk Management")]
        public int AtrLengthRisk { get; set; }

        [Range(0.5, 5.0)]
        [NinjaScriptProperty]
        [Display(Name = "SL Multiplier", Order = 4, GroupName = "05. Risk Management")]
        public double SlMultiplier { get; set; }

        [Range(0.5, 5.0)]
        [NinjaScriptProperty]
        [Display(Name = "TP1 Multiplier", Order = 5, GroupName = "05. Risk Management")]
        public double Tp1Multiplier { get; set; }

        [Range(1.0, 10.0)]
        [NinjaScriptProperty]
        [Display(Name = "TP2 Multiplier", Order = 6, GroupName = "05. Risk Management")]
        public double Tp2Multiplier { get; set; }

        [Range(1.5, 15.0)]
        [NinjaScriptProperty]
        [Display(Name = "TP3 Multiplier", Order = 7, GroupName = "05. Risk Management")]
        public double Tp3Multiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break-Even After TP1", Order = 8, GroupName = "05. Risk Management")]
        public bool BreakEvenAfterTp1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show SL/TP Lines", Order = 9, GroupName = "05. Risk Management")]
        public bool ShowSlTpLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show SL/TP Labels", Order = 10, GroupName = "05. Risk Management")]
        public bool ShowSlTpLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Percent On Labels", Order = 11, GroupName = "05. Risk Management")]
        public bool ShowPctOnLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Line Style", Order = 12, GroupName = "05. Risk Management")]
        public TrendFlowLineStyle EntryLineStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL Line Style", Order = 13, GroupName = "05. Risk Management")]
        public TrendFlowLineStyle SlLineStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TP Line Style", Order = 14, GroupName = "05. Risk Management")]
        public TrendFlowLineStyle TpLineStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Dashboard", Order = 1, GroupName = "06. Dashboard")]
        public bool ShowDashboard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Position", Order = 2, GroupName = "06. Dashboard")]
        public TrendFlowDashPosition DashboardPosition { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Font Size", Order = 3, GroupName = "06. Dashboard")]
        public TrendFlowSize DashboardFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Market Section", Order = 4, GroupName = "06. Dashboard")]
        public bool ShowMarketSection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile Section", Order = 5, GroupName = "06. Dashboard")]
        public bool ShowProfileSection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Section", Order = 6, GroupName = "06. Dashboard")]
        public bool ShowTradeSection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stats Section", Order = 7, GroupName = "06. Dashboard")]
        public bool ShowStatsSection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Order = 1, GroupName = "07. Alerts")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook JSON Format", Order = 2, GroupName = "07. Alerts")]
        public bool WebhookJsonFormat { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert On Trend Flips", Order = 3, GroupName = "07. Alerts")]
        public bool AlertOnTrendFlips { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert On SL/Reversal", Order = 4, GroupName = "07. Alerts")]
        public bool AlertOnSlHitOrReversal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert On TP/BE", Order = 5, GroupName = "07. Alerts")]
        public bool AlertOnTpOrBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert On LVN Break", Order = 6, GroupName = "07. Alerts")]
        public bool AlertOnLvnBreak { get; set; }

        [XmlIgnore]
        [Display(Name = "Bull", Order = 1, GroupName = "08. Colors")]
        public Brush BullBrush { get; set; }

        [Browsable(false)]
        public string BullBrushSerializable
        {
            get { return Serialize.BrushToString(BullBrush); }
            set { BullBrush = EnsureFrozen(Serialize.StringToBrush(value)); }
        }

        [XmlIgnore]
        [Display(Name = "Bear", Order = 2, GroupName = "08. Colors")]
        public Brush BearBrush { get; set; }

        [Browsable(false)]
        public string BearBrushSerializable
        {
            get { return Serialize.BrushToString(BearBrush); }
            set { BearBrush = EnsureFrozen(Serialize.StringToBrush(value)); }
        }

        [Range(0.2, 0.95)]
        [NinjaScriptProperty]
        [Display(Name = "HVN Threshold", Order = 1, GroupName = "09. Advanced")]
        public double HvnThreshold { get; set; }

        [Range(0.05, 0.6)]
        [NinjaScriptProperty]
        [Display(Name = "LVN Threshold", Order = 2, GroupName = "09. Advanced")]
        public double LvnThreshold { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band1
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band2
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band3
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band4
        {
            get { return Values[3]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band5
        {
            get { return Values[4]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band6
        {
            get { return Values[5]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band7
        {
            get { return Values[6]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band8
        {
            get { return Values[7]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band9
        {
            get { return Values[8]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Band10
        {
            get { return Values[9]; }
        }

        #endregion
    }
}
