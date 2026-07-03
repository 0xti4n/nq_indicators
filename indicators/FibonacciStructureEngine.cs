#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public enum FibStructLineStyle
    {
        Solid,
        Dashed,
        Dotted
    }

    public enum FibStructDashPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Fibonacci Structure Engine v1.5.2 (NinjaTrader 8 port).
    /// Swing structure (BOS/CHoCH), direction-aware Fibonacci retracement engine,
    /// EQH/EQL liquidity levels, liquidity sweeps, engulfing patterns,
    /// weighted confluence scoring, signals, dashboard and alerts.
    /// </summary>
    public class FibonacciStructureEngine : Indicator
    {
        private const string IndicatorVersion = "v1.5.2";

        private ATR atr;
        private Series<double> bodySeries;
        private EMA bodyEma;
        private SimpleFont signalFont;
        private SimpleFont dashboardFont;
        private Brush eqFadedBrush;
        private Brush fibRefBrush;
        private Brush fib236Brush;
        private Brush fib382Brush;
        private Brush fib500Brush;
        private Brush fib618Brush;
        private Brush fib786Brush;
        private Brush bullTargetFadedBrush;
        private Brush bearTargetFadedBrush;

        // ── Swings ──
        private double swHigh1 = double.NaN, swHigh2 = double.NaN;
        private int swHigh1Idx = -1, swHigh2Idx = -1;
        private double swLow1 = double.NaN, swLow2 = double.NaN;
        private int swLow1Idx = -1, swLow2Idx = -1;

        // ── EQH / EQL ──
        private bool eqhActive, eqlActive;
        private double eqhPrice = double.NaN, eqlPrice = double.NaN;
        private string eqhLineTag, eqhLblTag, eqlLineTag, eqlLblTag;
        private int eqhStartIdx = -1, eqhEndIdx = -1, eqlStartIdx = -1, eqlEndIdx = -1;
        private int eqCounter;

        // ── Sweep tracking ──
        private int lastSweptHighIdx = int.MinValue;
        private int lastSweptLowIdx = int.MinValue;

        // ── Structure ──
        private int structureBias;
        private int lastBrokenHighIdx = int.MinValue;
        private int lastBrokenLowIdx = int.MinValue;
        private int lastChochDir;

        // ── Fibonacci engine ──
        private double fibSwingHigh = double.NaN, fibSwingLow = double.NaN;
        private int fibSwingHighIdx = -1, fibSwingLowIdx = -1;
        private bool fibHighIsLive, fibLowIsLive;
        private int fibDirection;
        private int fibDrawStartIdx = -1;
        private bool fibDrawingsExist;

        private double fib236Price = double.NaN, fib382Price = double.NaN, fib500Price = double.NaN;
        private double fib618Price = double.NaN, fib786Price = double.NaN;
        private double fibTargetPrice = double.NaN, fibTgt50Price = double.NaN;

        // ── Signals ──
        private int barsSinceLastSignal = 999;

        // ── Dashboard state (last computed values) ──
        private double lastAtrValue;
        private double lastConfluenceScore;
        private string lastConfluenceStr = "None";
        private bool lastInPremium, lastInDiscount;
        private bool lastBuy, lastSell;
        private string lastNearFibStr = "—";

        private static readonly string[] FibTags =
        {
            "FSE_FibRef", "FSE_FibGZ", "FSE_FibTZ",
            "FSE_FibL236", "FSE_FibT236", "FSE_FibL382", "FSE_FibT382",
            "FSE_FibL500", "FSE_FibT500", "FSE_FibL618", "FSE_FibT618",
            "FSE_FibL786", "FSE_FibT786", "FSE_FibLTgt", "FSE_FibTTgt",
            "FSE_FibLTgt50", "FSE_FibTTgt50"
        };

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "FibonacciStructureEngine";
                Description = "Fibonacci Structure Engine " + IndicatorVersion + " — structure (BOS/CHoCH), direction-aware Fib retracements, EQH/EQL liquidity, sweeps, engulfing, confluence scoring, signals and alerts.";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;

                // Main
                SwingLength = 10;
                AtrSwingFilter = true;
                AtrFilterMultiplier = 0.5;
                SignalCooldown = 5;

                // Fibonacci
                ShowFibLevels = true;
                FibExtensionBars = 20;
                ShowFib236 = false;
                ShowFib382 = true;
                ShowFib500 = true;
                ShowFib618 = true;
                ShowFib786 = true;
                ShowFibTarget = true;
                ShowFibTgt50 = true;
                ConfluenceAtrTolerance = 0.3;

                // Structure
                ShowStructure = true;
                ShowSwingLabels = true;
                ShowEngulfing = true;
                StrictEngulfing = true;

                // Liquidity
                ShowEqLevels = true;
                EqAtrTolerance = 0.1;
                EqExtensionBars = 50;
                ShowSweeps = true;
                SweepsBoostConfluence = true;

                // Visual
                ShowSignals = false;
                StructureLineStyle = FibStructLineStyle.Dashed;
                StructureLineWidth = 2;
                HistoricalPlotBars = 300;

                // Dashboard
                ShowDashboard = true;
                DashboardPosition = FibStructDashPosition.TopRight;

                // Alerts
                EnableAlerts = false;
                WebhookJsonFormat = false;

                // Colors
                BullBrush = FrozenBrush(0x00, 0xE6, 0x76);
                BearBrush = FrozenBrush(0xFF, 0x52, 0x52);
                FibBrush = FrozenBrush(0x42, 0xA5, 0xF5);
                ConfluenceBrush = FrozenBrush(0xFF, 0xD6, 0x00);
                EqBrush = FrozenBrush(0xB3, 0x88, 0xFF);
                SweepBrush = FrozenBrush(0xFF, 0x91, 0x00);
            }
            else if (State == State.DataLoaded)
            {
                // Freeze user-selected brushes so worker threads can access them
                BullBrush = EnsureFrozen(BullBrush);
                BearBrush = EnsureFrozen(BearBrush);
                FibBrush = EnsureFrozen(FibBrush);
                ConfluenceBrush = EnsureFrozen(ConfluenceBrush);
                EqBrush = EnsureFrozen(EqBrush);
                SweepBrush = EnsureFrozen(SweepBrush);

                atr = ATR(14);
                bodySeries = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                bodyEma = EMA(bodySeries, 14);

                signalFont = new SimpleFont("Arial", 11);
                dashboardFont = new SimpleFont("Consolas", 11);
                eqFadedBrush = Faded(EqBrush, 128);
                fibRefBrush = Faded(FibBrush, 102);
                fib236Brush = Faded(FibBrush, 153);
                fib382Brush = Faded(FibBrush, 178);
                fib500Brush = Faded(FibBrush, 204);
                fib618Brush = Faded(FibBrush, 229);
                fib786Brush = Faded(FibBrush, 178);
                bullTargetFadedBrush = Faded(BullBrush, 153);
                bearTargetFadedBrush = Faded(BearBrush, 153);
            }
        }

        protected override void OnBarUpdate()
        {
            bodySeries[0] = Math.Abs(Close[0] - Open[0]);

            int minBars = Math.Max(SwingLength * 2 + 1, 20);
            if (CurrentBar < minBars)
                return;

            double atrValue = atr[0];
            if (double.IsNaN(atrValue))
                atrValue = 0.0;
            lastAtrValue = atrValue;

            int warmupBars = Math.Max(SwingLength * 3, 50);
            bool isWarmedUp = CurrentBar >= warmupBars;
            bool drawEventObjects = ShouldDrawEventObject();
            bool drawActiveObjects = ShouldDrawActiveObject();

            barsSinceLastSignal++;

            // ══════════════════════════════════════
            // PIVOT DETECTION (ATR-filtered)
            // ══════════════════════════════════════
            bool newSwingHigh = false;
            bool newSwingLow = false;
            double atrMinSize = AtrSwingFilter ? atrValue * AtrFilterMultiplier : 0.0;

            double pivotHigh = GetPivotHigh();
            double pivotLow = GetPivotLow();

            if (!double.IsNaN(pivotHigh))
            {
                bool passFilter = double.IsNaN(swLow1) || (pivotHigh - swLow1) >= atrMinSize;
                if (passFilter)
                {
                    swHigh2 = swHigh1;
                    swHigh2Idx = swHigh1Idx;
                    swHigh1 = pivotHigh;
                    swHigh1Idx = CurrentBar - SwingLength;
                    newSwingHigh = true;
                }
            }

            if (!double.IsNaN(pivotLow))
            {
                bool passFilter = double.IsNaN(swHigh1) || (swHigh1 - pivotLow) >= atrMinSize;
                if (passFilter)
                {
                    swLow2 = swLow1;
                    swLow2Idx = swLow1Idx;
                    swLow1 = pivotLow;
                    swLow1Idx = CurrentBar - SwingLength;
                    newSwingLow = true;
                }
            }

            // ── Swing Labels ──
            if (newSwingHigh && !double.IsNaN(swHigh2) && isWarmedUp && ShowSwingLabels && drawEventObjects)
            {
                string swLbl = swHigh1 > swHigh2 ? "HH" : "LH";
                Brush swClr = swHigh1 > swHigh2 ? BullBrush : BearBrush;
                Draw.Text(this, "FSE_SwH" + CurrentBar, swLbl, CurrentBar - swHigh1Idx, swHigh1 + atrValue * 0.35, swClr);
            }

            if (newSwingLow && !double.IsNaN(swLow2) && isWarmedUp && ShowSwingLabels && drawEventObjects)
            {
                string swLbl = swLow1 > swLow2 ? "HL" : "LL";
                Brush swClr = swLow1 > swLow2 ? BullBrush : BearBrush;
                Draw.Text(this, "FSE_SwL" + CurrentBar, swLbl, CurrentBar - swLow1Idx, swLow1 - atrValue * 0.35, swClr);
            }

            // ══════════════════════════════════════
            // EQH / EQL DETECTION
            // ══════════════════════════════════════
            double eqTolerance = atrValue > 0 ? atrValue * EqAtrTolerance : 0.0;

            if (newSwingHigh && !double.IsNaN(swHigh2) && Math.Abs(swHigh1 - swHigh2) <= eqTolerance && isWarmedUp)
            {
                if (eqhActive)
                {
                    SafeRemove(eqhLineTag);
                    SafeRemove(eqhLblTag);
                }
                eqhActive = true;
                eqhPrice = (swHigh1 + swHigh2) / 2.0;
                eqhStartIdx = swHigh2Idx;
                eqhEndIdx = swHigh1Idx + EqExtensionBars;
                eqCounter++;
                eqhLineTag = "FSE_EQHLine" + eqCounter;
                eqhLblTag = "FSE_EQHLbl" + eqCounter;
                if (ShowEqLevels && drawEventObjects)
                {
                    Draw.Line(this, eqhLineTag, false, CurrentBar - eqhStartIdx, eqhPrice, CurrentBar - eqhEndIdx, eqhPrice, EqBrush, DashStyleHelper.Dash, 1);
                    Draw.Text(this, eqhLblTag, "EQH", CurrentBar - swHigh1Idx, eqhPrice + atrValue * 0.35, EqBrush);
                }
            }

            if (newSwingLow && !double.IsNaN(swLow2) && Math.Abs(swLow1 - swLow2) <= eqTolerance && isWarmedUp)
            {
                if (eqlActive)
                {
                    SafeRemove(eqlLineTag);
                    SafeRemove(eqlLblTag);
                }
                eqlActive = true;
                eqlPrice = (swLow1 + swLow2) / 2.0;
                eqlStartIdx = swLow2Idx;
                eqlEndIdx = swLow1Idx + EqExtensionBars;
                eqCounter++;
                eqlLineTag = "FSE_EQLLine" + eqCounter;
                eqlLblTag = "FSE_EQLLbl" + eqCounter;
                if (ShowEqLevels && drawEventObjects)
                {
                    Draw.Line(this, eqlLineTag, false, CurrentBar - eqlStartIdx, eqlPrice, CurrentBar - eqlEndIdx, eqlPrice, EqBrush, DashStyleHelper.Dash, 1);
                    Draw.Text(this, eqlLblTag, "EQL", CurrentBar - swLow1Idx, eqlPrice - atrValue * 0.35, EqBrush);
                }
            }

            // ══════════════════════════════════════
            // LIQUIDITY SWEEP DETECTION
            // ══════════════════════════════════════
            bool sweepHigh = false;
            bool sweepLow = false;
            double sweepHighRef = 0.0;
            double sweepLowRef = 0.0;

            if (isWarmedUp)
            {
                double refHigh = eqhActive ? eqhPrice : swHigh1;
                double refLow = eqlActive ? eqlPrice : swLow1;
                int refHighIdx = eqhActive ? swHigh2Idx : swHigh1Idx;
                int refLowIdx = eqlActive ? swLow2Idx : swLow1Idx;

                bool canSweepHigh = !double.IsNaN(refHigh) && refHighIdx != lastSweptHighIdx;
                bool canSweepLow = !double.IsNaN(refLow) && refLowIdx != lastSweptLowIdx;

                if (canSweepHigh && High[0] > refHigh && Close[0] < refHigh && Open[0] < refHigh)
                {
                    sweepHigh = true;
                    sweepHighRef = refHigh;
                    lastSweptHighIdx = refHighIdx;
                    if (eqhActive)
                    {
                        eqhActive = false;
                        if (!string.IsNullOrEmpty(eqhLineTag) && drawEventObjects)
                            Draw.Line(this, eqhLineTag, false, CurrentBar - eqhStartIdx, eqhPrice, 0, eqhPrice, eqFadedBrush, DashStyleHelper.Dash, 1);
                    }
                }

                if (canSweepLow && Low[0] < refLow && Close[0] > refLow && Open[0] > refLow)
                {
                    sweepLow = true;
                    sweepLowRef = refLow;
                    lastSweptLowIdx = refLowIdx;
                    if (eqlActive)
                    {
                        eqlActive = false;
                        if (!string.IsNullOrEmpty(eqlLineTag) && drawEventObjects)
                            Draw.Line(this, eqlLineTag, false, CurrentBar - eqlStartIdx, eqlPrice, 0, eqlPrice, eqFadedBrush, DashStyleHelper.Dash, 1);
                    }
                }
            }

            if (ShowSweeps && sweepHigh && drawEventObjects)
                Draw.Text(this, "FSE_SwpH" + CurrentBar, "✗", 0, High[0] + atrValue * 0.35, SweepBrush);

            if (ShowSweeps && sweepLow && drawEventObjects)
                Draw.Text(this, "FSE_SwpL" + CurrentBar, "✗", 0, Low[0] - atrValue * 0.35, SweepBrush);

            // ══════════════════════════════════════
            // STRUCTURE DETECTION (BOS / CHoCH)
            // ══════════════════════════════════════
            bool isBOS = false;
            bool isCHoCH = false;
            bool isBullBreak = false;
            bool isBearBreak = false;

            bool bullBreakCond = !double.IsNaN(swHigh1) && isWarmedUp && Close[0] > swHigh1 && swHigh1Idx != lastBrokenHighIdx;
            bool bearBreakCond = !double.IsNaN(swLow1) && isWarmedUp && Close[0] < swLow1 && swLow1Idx != lastBrokenLowIdx;

            if (bullBreakCond && bearBreakCond)
            {
                if (structureBias <= 0)
                    bearBreakCond = false;
                else
                    bullBreakCond = false;
            }

            if (bullBreakCond)
            {
                if (structureBias <= 0)
                {
                    isCHoCH = true;
                    lastChochDir = 1;
                    lastSweptHighIdx = int.MinValue;
                    lastSweptLowIdx = int.MinValue;
                }
                else
                    isBOS = true;
                structureBias = 1;
                isBullBreak = true;
                lastBrokenHighIdx = swHigh1Idx;
            }

            if (bearBreakCond)
            {
                if (structureBias >= 0)
                {
                    isCHoCH = true;
                    lastChochDir = -1;
                    lastSweptHighIdx = int.MinValue;
                    lastSweptLowIdx = int.MinValue;
                }
                else
                    isBOS = true;
                structureBias = -1;
                isBearBreak = true;
                lastBrokenLowIdx = swLow1Idx;
            }

            // ── Draw Structure Lines & Labels ──
            if (ShowStructure && (isBOS || isCHoCH) && drawEventObjects)
            {
                double breakPrice = isBullBreak ? (double.IsNaN(swHigh1) ? Close[0] : swHigh1) : (double.IsNaN(swLow1) ? Close[0] : swLow1);
                Brush breakColor = isBullBreak ? BullBrush : BearBrush;
                string breakText = isCHoCH ? "CHoCH" : "BOS";
                int startIdx = isBullBreak
                    ? (swHigh1Idx >= 0 ? swHigh1Idx : CurrentBar - 10)
                    : (swLow1Idx >= 0 ? swLow1Idx : CurrentBar - 10);
                int midIdx = (int)Math.Round((startIdx + CurrentBar) / 2.0);
                Draw.Line(this, "FSE_StructL" + CurrentBar, false, CurrentBar - startIdx, breakPrice, 0, breakPrice, breakColor, ToDashStyle(StructureLineStyle), StructureLineWidth);
                double lblY = isBullBreak ? breakPrice + atrValue * 0.5 : breakPrice - atrValue * 0.5;
                Draw.Text(this, "FSE_StructT" + CurrentBar, breakText, CurrentBar - midIdx, lblY, breakColor);
            }

            // ══════════════════════════════════════
            // FIBONACCI RETRACEMENT ENGINE
            // ══════════════════════════════════════
            bool fibNeedsRedraw = false;

            if (isCHoCH && isBullBreak)
            {
                fibDirection = 1;
                fibSwingHigh = High[0];
                fibSwingHighIdx = CurrentBar;
                fibSwingLow = swLow1;
                fibSwingLowIdx = swLow1Idx >= 0 ? swLow1Idx : CurrentBar - 10;
                fibHighIsLive = true;
                fibLowIsLive = false;
                fibNeedsRedraw = true;
            }

            if (isCHoCH && isBearBreak)
            {
                fibDirection = -1;
                fibSwingLow = Low[0];
                fibSwingLowIdx = CurrentBar;
                fibSwingHigh = swHigh1;
                fibSwingHighIdx = swHigh1Idx >= 0 ? swHigh1Idx : CurrentBar - 10;
                fibLowIsLive = true;
                fibHighIsLive = false;
                fibNeedsRedraw = true;
            }

            if (isBOS && isBullBreak)
            {
                fibSwingHigh = High[0];
                fibSwingHighIdx = CurrentBar;
                fibSwingLow = swLow1;
                fibSwingLowIdx = swLow1Idx >= 0 ? swLow1Idx : CurrentBar - 10;
                fibHighIsLive = true;
                fibLowIsLive = false;
                fibNeedsRedraw = true;
            }

            if (isBOS && isBearBreak)
            {
                fibSwingLow = Low[0];
                fibSwingLowIdx = CurrentBar;
                fibSwingHigh = swHigh1;
                fibSwingHighIdx = swHigh1Idx >= 0 ? swHigh1Idx : CurrentBar - 10;
                fibLowIsLive = true;
                fibHighIsLive = false;
                fibNeedsRedraw = true;
            }

            // ── Trail live edge as price extends ──
            if (!isBullBreak && !isBearBreak)
            {
                if (fibHighIsLive && !double.IsNaN(fibSwingHigh) && High[0] > fibSwingHigh)
                {
                    fibSwingHigh = High[0];
                    fibSwingHighIdx = CurrentBar;
                    fibNeedsRedraw = true;
                }
                if (fibLowIsLive && !double.IsNaN(fibSwingLow) && Low[0] < fibSwingLow)
                {
                    fibSwingLow = Low[0];
                    fibSwingLowIdx = CurrentBar;
                    fibNeedsRedraw = true;
                }
            }

            // ── Lock live edge when confirmed pivot arrives ──
            if (newSwingHigh && fibHighIsLive && !double.IsNaN(swHigh1))
            {
                fibSwingHigh = swHigh1;
                fibSwingHighIdx = swHigh1Idx;
                fibHighIsLive = false;
                fibNeedsRedraw = true;
            }

            if (newSwingLow && fibLowIsLive && !double.IsNaN(swLow1))
            {
                fibSwingLow = swLow1;
                fibSwingLowIdx = swLow1Idx;
                fibLowIsLive = false;
                fibNeedsRedraw = true;
            }

            // ── Update locked anchors on new confirmed swings ──
            if (newSwingHigh && !fibHighIsLive && !double.IsNaN(swHigh1) && !double.IsNaN(fibSwingHigh) && swHigh1 != fibSwingHigh)
            {
                fibSwingHigh = swHigh1;
                fibSwingHighIdx = swHigh1Idx;
                fibNeedsRedraw = true;
            }

            if (newSwingLow && !fibLowIsLive && !double.IsNaN(swLow1) && !double.IsNaN(fibSwingLow) && swLow1 != fibSwingLow)
            {
                fibSwingLow = swLow1;
                fibSwingLowIdx = swLow1Idx;
                fibNeedsRedraw = true;
            }

            // ══════════════════════════════════════
            // FIBONACCI LEVEL CALCULATION
            // ══════════════════════════════════════
            bool fibRangeValid = !double.IsNaN(fibSwingHigh) && !double.IsNaN(fibSwingLow) && fibSwingHigh > fibSwingLow && fibDirection != 0;

            if (fibRangeValid)
            {
                double fibRange = fibSwingHigh - fibSwingLow;
                if (fibDirection == 1)
                {
                    fib236Price = fibSwingHigh - fibRange * 0.236;
                    fib382Price = fibSwingHigh - fibRange * 0.382;
                    fib500Price = fibSwingHigh - fibRange * 0.500;
                    fib618Price = fibSwingHigh - fibRange * 0.618;
                    fib786Price = fibSwingHigh - fibRange * 0.786;
                    fibTgt50Price = fibSwingHigh + fibRange * 0.5;
                    fibTargetPrice = fibSwingHigh + fibRange * 0.618;
                }
                else
                {
                    fib236Price = fibSwingLow + fibRange * 0.236;
                    fib382Price = fibSwingLow + fibRange * 0.382;
                    fib500Price = fibSwingLow + fibRange * 0.500;
                    fib618Price = fibSwingLow + fibRange * 0.618;
                    fib786Price = fibSwingLow + fibRange * 0.786;
                    fibTgt50Price = fibSwingLow - fibRange * 0.5;
                    fibTargetPrice = fibSwingLow - fibRange * 0.618;
                }
            }
            else
            {
                fib236Price = double.NaN;
                fib382Price = double.NaN;
                fib500Price = double.NaN;
                fib618Price = double.NaN;
                fib786Price = double.NaN;
                fibTgt50Price = double.NaN;
                fibTargetPrice = double.NaN;
            }

            // ── Draw / Extend Fib Drawings ──
            if (ShowFibLevels && fibRangeValid)
            {
                if (fibNeedsRedraw || fibDrawStartIdx < 0)
                    fibDrawStartIdx = CurrentBar;

                if (drawActiveObjects)
                    DrawFibLevels();
            }
            else if (fibDrawingsExist && drawActiveObjects)
            {
                RemoveFibDrawings();
            }

            // ══════════════════════════════════════
            // PREMIUM / DISCOUNT ZONES
            // ══════════════════════════════════════
            bool inPremium = false;
            bool inDiscount = false;

            if (!double.IsNaN(fib500Price) && fibRangeValid)
            {
                if (fibDirection == 1)
                {
                    inPremium = Close[0] > fib500Price;
                    inDiscount = Close[0] <= fib500Price;
                }
                else
                {
                    inPremium = Close[0] < fib500Price;
                    inDiscount = Close[0] >= fib500Price;
                }
            }

            // ══════════════════════════════════════
            // CONFLUENCE SCORING (Weighted)
            // ══════════════════════════════════════
            double confTolerance = atrValue > 0 ? atrValue * ConfluenceAtrTolerance : 0.001;
            double confluenceWeight = 0.0;

            if (ShowFib236 && IsNearLevel(fib236Price, confTolerance))
                confluenceWeight += 1.0;
            if (IsNearLevel(fib382Price, confTolerance))
                confluenceWeight += 1.5;
            if (IsNearLevel(fib500Price, confTolerance))
                confluenceWeight += 2.0;
            if (IsNearLevel(fib618Price, confTolerance))
                confluenceWeight += 2.5;
            if (IsNearLevel(fib786Price, confTolerance))
                confluenceWeight += 1.5;
            if (!double.IsNaN(swHigh1) && IsNearLevel(swHigh1, confTolerance))
                confluenceWeight += 1.0;
            if (!double.IsNaN(swLow1) && IsNearLevel(swLow1, confTolerance))
                confluenceWeight += 1.0;

            if (SweepsBoostConfluence && (sweepHigh || sweepLow))
                confluenceWeight += 2.0;

            double confluenceScore = Math.Min(confluenceWeight * 10.0, 100.0);
            string confluenceStr = confluenceScore >= 60 ? "Strong" : confluenceScore >= 30 ? "Moderate" : confluenceScore > 0 ? "Weak" : "None";

            // ══════════════════════════════════════
            // ENGULFING PATTERN DETECTION
            // ══════════════════════════════════════
            double body = bodySeries[0];
            double bodyAvg = double.IsNaN(bodyEma[0]) ? body : bodyEma[0];
            bool isLongBody = body > bodyAvg;
            double prevBody = CurrentBar > 0 ? bodySeries[1] : 0.0;
            double prevBodyAvg = CurrentBar > 0 && !double.IsNaN(bodyEma[1]) ? bodyEma[1] : bodyAvg;
            bool isSmallPrev = prevBody < prevBodyAvg;
            bool bodyIsBigger = body > prevBody;

            bool bearishEngulf, bullishEngulf;

            if (StrictEngulfing)
            {
                bearishEngulf = Close[0] < Open[0] && isLongBody && bodyIsBigger && Close[1] > Open[1] && isSmallPrev && Open[0] > Close[1] && Close[0] < Open[1];
                bullishEngulf = Close[0] > Open[0] && isLongBody && bodyIsBigger && Close[1] < Open[1] && isSmallPrev && Open[0] < Close[1] && Close[0] > Open[1];
            }
            else
            {
                bearishEngulf = Close[0] < Open[0] && isLongBody && bodyIsBigger && Close[1] > Open[1] && isSmallPrev && Close[0] <= Open[1] && Open[0] >= Close[1] && (Close[0] < Open[1] || Open[0] > Close[1]);
                bullishEngulf = Close[0] > Open[0] && isLongBody && bodyIsBigger && Close[1] < Open[1] && isSmallPrev && Close[0] >= Open[1] && Open[0] <= Close[1] && (Close[0] > Open[1] || Open[0] < Close[1]);
            }

            bool bearEngulfCtx = bearishEngulf && isWarmedUp && (inPremium || confluenceWeight >= 1.5);
            bool bullEngulfCtx = bullishEngulf && isWarmedUp && (inDiscount || confluenceWeight >= 1.5);

            if (ShowEngulfing && bearEngulfCtx && drawEventObjects)
                Draw.Text(this, "FSE_EngB" + CurrentBar, "▼", 0, High[0] + atrValue * 0.35, BearBrush);

            if (ShowEngulfing && bullEngulfCtx && drawEventObjects)
                Draw.Text(this, "FSE_EngU" + CurrentBar, "▲", 0, Low[0] - atrValue * 0.35, BullBrush);

            // ══════════════════════════════════════
            // SIGNAL LOGIC (unified cooldown + mutual exclusion)
            // ══════════════════════════════════════
            bool sweepBuySignal = sweepLow && isWarmedUp && (inDiscount || confluenceWeight >= 2.0);
            bool sweepSellSignal = sweepHigh && isWarmedUp && (inPremium || confluenceWeight >= 2.0);

            bool buyEngulf = bullEngulfCtx && structureBias == 1 && confluenceWeight >= 1.5;
            bool buyChoch = isCHoCH && isBullBreak;
            bool buySweep = sweepBuySignal;

            bool sellEngulf = bearEngulfCtx && structureBias == -1 && confluenceWeight >= 1.5;
            bool sellChoch = isCHoCH && isBearBreak;
            bool sellSweep = sweepSellSignal;

            bool buyRaw = buyEngulf || buyChoch || buySweep;
            bool sellRaw = sellEngulf || sellChoch || sellSweep;

            if (buyRaw && sellRaw)
            {
                buyRaw = false;
                sellRaw = false;
            }

            bool confirmedBuy = buyRaw && barsSinceLastSignal >= SignalCooldown && isWarmedUp;
            bool confirmedSell = sellRaw && barsSinceLastSignal >= SignalCooldown && isWarmedUp;

            string buyTrigger = "";
            if (confirmedBuy)
            {
                buyTrigger = buyChoch ? "choch" : "";
                if (buySweep)
                    buyTrigger = buyTrigger.Length == 0 ? "sweep" : buyTrigger + "+sweep";
                if (buyEngulf)
                    buyTrigger = buyTrigger.Length == 0 ? "engulf" : buyTrigger + "+engulf";
            }

            string sellTrigger = "";
            if (confirmedSell)
            {
                sellTrigger = sellChoch ? "choch" : "";
                if (sellSweep)
                    sellTrigger = sellTrigger.Length == 0 ? "sweep" : sellTrigger + "+sweep";
                if (sellEngulf)
                    sellTrigger = sellTrigger.Length == 0 ? "engulf" : sellTrigger + "+engulf";
            }

            if (confirmedBuy || confirmedSell)
                barsSinceLastSignal = 0;

            // ── Signal Visuals ──
            if (ShowSignals && confirmedBuy && drawEventObjects)
                Draw.Text(this, "FSE_Buy" + CurrentBar, false, "BUY", 0, Low[0] - atrValue * 0.8, 0, Brushes.White,
                    signalFont, TextAlignment.Center, Brushes.Transparent, BullBrush, 100);

            if (ShowSignals && confirmedSell && drawEventObjects)
                Draw.Text(this, "FSE_Sell" + CurrentBar, false, "SELL", 0, High[0] + atrValue * 0.8, 0, Brushes.White,
                    signalFont, TextAlignment.Center, Brushes.Transparent, BearBrush, 100);

            // ══════════════════════════════════════
            // NEAREST FIB (for dashboard)
            // ══════════════════════════════════════
            string nearestFibName = "—";
            double nearestFibDist = double.NaN;

            if (fibRangeValid)
            {
                TrackNearestFib("0.236", fib236Price, ref nearestFibName, ref nearestFibDist);
                TrackNearestFib("0.382", fib382Price, ref nearestFibName, ref nearestFibDist);
                TrackNearestFib("0.500", fib500Price, ref nearestFibName, ref nearestFibDist);
                TrackNearestFib("0.618", fib618Price, ref nearestFibName, ref nearestFibDist);
                TrackNearestFib("0.786", fib786Price, ref nearestFibName, ref nearestFibDist);
                TrackNearestFib("-0.5", fibTgt50Price, ref nearestFibName, ref nearestFibDist);
                TrackNearestFib("-0.618", fibTargetPrice, ref nearestFibName, ref nearestFibDist);
            }

            lastNearFibStr = "—";
            if (!double.IsNaN(nearestFibDist) && atrValue > 0)
                lastNearFibStr = nearestFibName + " (" + (nearestFibDist / atrValue).ToString("0.00") + " ATR)";

            lastConfluenceScore = confluenceScore;
            lastConfluenceStr = confluenceStr;
            lastInPremium = inPremium;
            lastInDiscount = inDiscount;
            lastBuy = confirmedBuy;
            lastSell = confirmedSell;

            // ══════════════════════════════════════
            // DASHBOARD
            // ══════════════════════════════════════
            if (ShowDashboard && drawActiveObjects)
                DrawDashboard();

            // ══════════════════════════════════════
            // ALERTS (realtime only)
            // ══════════════════════════════════════
            if (EnableAlerts && State == State.Realtime)
                FireAlerts(confirmedBuy, confirmedSell, buyTrigger, sellTrigger, isBOS, isCHoCH, isBullBreak,
                    sweepHigh, sweepLow, sweepHighRef, sweepLowRef, atrValue, confluenceScore);
        }

        // ══════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════

        private double GetPivotHigh()
        {
            int center = SwingLength;
            double candidate = High[center];
            for (int i = 0; i <= SwingLength * 2; i++)
            {
                if (i == center)
                    continue;
                if (High[i] >= candidate)
                    return double.NaN;
            }
            return candidate;
        }

        private double GetPivotLow()
        {
            int center = SwingLength;
            double candidate = Low[center];
            for (int i = 0; i <= SwingLength * 2; i++)
            {
                if (i == center)
                    continue;
                if (Low[i] <= candidate)
                    return double.NaN;
            }
            return candidate;
        }

        private bool IsNearLevel(double level, double tolerance)
        {
            if (double.IsNaN(level))
                return false;
            return Math.Abs(Close[0] - level) <= tolerance
                || (Low[0] <= level + tolerance && High[0] >= level - tolerance);
        }

        private bool ShouldDrawEventObject()
        {
            if (State != State.Historical)
                return true;
            if (HistoricalPlotBars <= 0 || Bars == null || Bars.Count <= 0)
                return false;

            int firstDrawableBar = Math.Max(0, Bars.Count - HistoricalPlotBars);
            return CurrentBar >= firstDrawableBar;
        }

        private bool ShouldDrawActiveObject()
        {
            if (State != State.Historical)
                return true;
            if (Bars == null || Bars.Count <= 0)
                return false;

            return CurrentBar >= Bars.Count - 1;
        }

        private void TrackNearestFib(string name, double price, ref string nearestName, ref double nearestDistance)
        {
            if (double.IsNaN(price))
                return;

            double distance = Math.Abs(Close[0] - price);
            if (!double.IsNaN(nearestDistance) && distance >= nearestDistance)
                return;

            nearestName = name;
            nearestDistance = distance;
        }

        private void DrawFibLevels()
        {
            int startAgo = CurrentBar - fibDrawStartIdx;
            int endAgo = -FibExtensionBars;
            Brush targetColor = fibDirection == 1 ? BullBrush : BearBrush;
            Brush targetFadedColor = fibDirection == 1 ? bullTargetFadedBrush : bearTargetFadedBrush;

            // Reference line from swing low to swing high
            Draw.Line(this, "FSE_FibRef", false, CurrentBar - fibSwingLowIdx, fibSwingLow, CurrentBar - fibSwingHighIdx, fibSwingHigh,
                fibRefBrush, DashStyleHelper.Dot, 1);

            // Golden zone (0.5–0.786)
            double gzTop = Math.Max(fib500Price, fib786Price);
            double gzBot = Math.Min(fib500Price, fib786Price);
            Draw.Rectangle(this, "FSE_FibGZ", false, startAgo, gzTop, endAgo, gzBot, Brushes.Transparent, ConfluenceBrush, 12);

            DrawFibLine("FSE_FibL236", "FSE_FibT236", ShowFib236, fib236Price, "0.236", fib236Brush, DashStyleHelper.Dot, 1, endAgo, startAgo);
            DrawFibLine("FSE_FibL382", "FSE_FibT382", ShowFib382, fib382Price, "0.382", fib382Brush, DashStyleHelper.Dot, 1, endAgo, startAgo);
            DrawFibLine("FSE_FibL500", "FSE_FibT500", ShowFib500, fib500Price, "0.500", fib500Brush, DashStyleHelper.Dash, 2, endAgo, startAgo);
            DrawFibLine("FSE_FibL618", "FSE_FibT618", ShowFib618, fib618Price, "0.618", fib618Brush, DashStyleHelper.Solid, 2, endAgo, startAgo);
            DrawFibLine("FSE_FibL786", "FSE_FibT786", ShowFib786, fib786Price, "0.786", fib786Brush, DashStyleHelper.Dot, 1, endAgo, startAgo);

            string tgtLabel = fibDirection == 1 ? "Target ↑" : "Target ↓";
            string tgt50Label = fibDirection == 1 ? "-0.5 ↑" : "-0.5 ↓";
            DrawFibLine("FSE_FibLTgt", "FSE_FibTTgt", ShowFibTarget, fibTargetPrice, tgtLabel, targetColor, DashStyleHelper.Dash, 2, endAgo, startAgo);
            DrawFibLine("FSE_FibLTgt50", "FSE_FibTTgt50", ShowFibTgt50, fibTgt50Price, tgt50Label, targetFadedColor, DashStyleHelper.Dot, 1, endAgo, startAgo);

            // Target zone (-0.5 to -0.618)
            if (ShowFibTarget || ShowFibTgt50)
            {
                double tzTop = Math.Max(fibTgt50Price, fibTargetPrice);
                double tzBot = Math.Min(fibTgt50Price, fibTargetPrice);
                Draw.Rectangle(this, "FSE_FibTZ", false, startAgo, tzTop, endAgo, tzBot, Brushes.Transparent, targetColor, 12);
            }
            else
                SafeRemove("FSE_FibTZ");

            fibDrawingsExist = true;
        }

        private void DrawFibLine(string lineTag, string textTag, bool show, double price, string text, Brush brush, DashStyleHelper style, int width, int endAgo, int startAgo)
        {
            if (show && !double.IsNaN(price))
            {
                Draw.Line(this, lineTag, false, startAgo, price, endAgo, price, brush, style, width);
                Draw.Text(this, textTag, text, endAgo, price, brush);
            }
            else
            {
                SafeRemove(lineTag);
                SafeRemove(textTag);
            }
        }

        private void RemoveFibDrawings()
        {
            foreach (string tag in FibTags)
                SafeRemove(tag);
            fibDrawingsExist = false;
        }

        private void SafeRemove(string tag)
        {
            if (!string.IsNullOrEmpty(tag))
                RemoveDrawObject(tag);
        }

        private void DrawDashboard()
        {
            string trendStr = structureBias > 0 ? "Bullish" : structureBias < 0 ? "Bearish" : "Neutral";
            string fibDirStr = lastChochDir > 0 ? "Long ↑" : lastChochDir < 0 ? "Short ↓" : "—";
            string sigStr = lastBuy ? "BUY" : lastSell ? "SELL" : "—";
            string zoneStr = lastInPremium ? "Premium" : lastInDiscount ? "Discount" : "—";
            string liqStr = eqhActive && eqlActive ? "EQH+EQL" : eqhActive ? "EQH ↑" : eqlActive ? "EQL ↓" : "—";
            string fib618Str = double.IsNaN(fib618Price) ? "—" : FormatPrice(fib618Price);

            string text =
                "FibStruct\n" +
                "Trend:      " + trendStr + "\n" +
                "Fib Dir:    " + fibDirStr + "\n" +
                "Signal:     " + sigStr + "\n" +
                "Confluence: " + lastConfluenceStr + " (" + lastConfluenceScore.ToString("0") + ")\n" +
                "Zone:       " + zoneStr + "\n" +
                "Liquidity:  " + liqStr + "\n" +
                "Fib .618:   " + fib618Str + "\n" +
                "ATR(14):    " + FormatPrice(lastAtrValue) + "\n" +
                "Near Fib:   " + lastNearFibStr + "\n" +
                "TF:         " + BarsPeriod + "\n" +
                IndicatorVersion;

            Draw.TextFixed(this, "FSE_Dash", text, ToTextPosition(DashboardPosition), Brushes.Gainsboro,
                dashboardFont, Brushes.DimGray, Brushes.Black, 60);
        }

        private void FireAlerts(bool confirmedBuy, bool confirmedSell, string buyTrigger, string sellTrigger,
            bool isBOS, bool isCHoCH, bool isBullBreak, bool sweepHigh, bool sweepLow,
            double sweepHighRef, double sweepLowRef, double atrValue, double confluenceScore)
        {
            string tickerStr = Instrument.FullName;
            string tfStr = BarsPeriod.ToString();
            string priceStr = FormatPrice(Close[0]);
            string confAlertStr = confluenceScore.ToString("0");

            if (confirmedBuy)
            {
                string slStr = FormatPrice(Close[0] - atrValue * 1.5);
                string tpStr = FormatPrice(Close[0] + atrValue * 2.5);
                string jMsg = "{\"action\":\"buy\",\"ticker\":\"" + tickerStr + "\",\"price\":" + Close[0] + ",\"tf\":\"" + tfStr + "\",\"trigger\":\"" + buyTrigger + "\",\"confluence\":" + confAlertStr + ",\"sl\":" + slStr + ",\"tp\":" + tpStr + "}";
                string tMsg = "🟢 BUY [" + buyTrigger + "] | " + tickerStr + " | TF: " + tfStr + " | Price: " + priceStr + " | Conf: " + confAlertStr + " | SL: " + slStr + " | TP: " + tpStr;
                Alert("FSE_Buy" + CurrentBar, Priority.High, WebhookJsonFormat ? jMsg : tMsg, "", 0, Brushes.Black, BullBrush);
            }

            if (confirmedSell)
            {
                string slStr = FormatPrice(Close[0] + atrValue * 1.5);
                string tpStr = FormatPrice(Close[0] - atrValue * 2.5);
                string jMsg = "{\"action\":\"sell\",\"ticker\":\"" + tickerStr + "\",\"price\":" + Close[0] + ",\"tf\":\"" + tfStr + "\",\"trigger\":\"" + sellTrigger + "\",\"confluence\":" + confAlertStr + ",\"sl\":" + slStr + ",\"tp\":" + tpStr + "}";
                string tMsg = "🔴 SELL [" + sellTrigger + "] | " + tickerStr + " | TF: " + tfStr + " | Price: " + priceStr + " | Conf: " + confAlertStr + " | SL: " + slStr + " | TP: " + tpStr;
                Alert("FSE_Sell" + CurrentBar, Priority.High, WebhookJsonFormat ? jMsg : tMsg, "", 0, Brushes.Black, BearBrush);
            }

            if (isBOS)
            {
                string dirStr = isBullBreak ? "bull" : "bear";
                string jMsg = "{\"action\":\"bos\",\"ticker\":\"" + tickerStr + "\",\"price\":" + Close[0] + ",\"direction\":\"" + dirStr + "\"}";
                string tMsg = "🔵 BOS | " + tickerStr + " | TF: " + tfStr + " | Price: " + priceStr + " | Dir: " + dirStr;
                Alert("FSE_BOS" + CurrentBar, Priority.Medium, WebhookJsonFormat ? jMsg : tMsg, "", 0, Brushes.Black, FibBrush);
            }

            if (isCHoCH)
            {
                string dirStr = isBullBreak ? "bull" : "bear";
                string jMsg = "{\"action\":\"choch\",\"ticker\":\"" + tickerStr + "\",\"price\":" + Close[0] + ",\"direction\":\"" + dirStr + "\"}";
                string tMsg = "🟡 CHoCH | " + tickerStr + " | TF: " + tfStr + " | Price: " + priceStr + " | Dir: " + dirStr;
                Alert("FSE_CHoCH" + CurrentBar, Priority.Medium, WebhookJsonFormat ? jMsg : tMsg, "", 0, Brushes.Black, ConfluenceBrush);
            }

            if (sweepHigh)
            {
                string lvlStr = FormatPrice(sweepHighRef);
                string jMsg = "{\"action\":\"sweep\",\"ticker\":\"" + tickerStr + "\",\"price\":" + Close[0] + ",\"direction\":\"high\",\"level\":" + lvlStr + "}";
                string tMsg = "🟠 SWEEP HIGH | " + tickerStr + " | TF: " + tfStr + " | Level: " + lvlStr + " | Close: " + priceStr;
                Alert("FSE_SwpH" + CurrentBar, Priority.Medium, WebhookJsonFormat ? jMsg : tMsg, "", 0, Brushes.Black, SweepBrush);
            }

            if (sweepLow)
            {
                string lvlStr = FormatPrice(sweepLowRef);
                string jMsg = "{\"action\":\"sweep\",\"ticker\":\"" + tickerStr + "\",\"price\":" + Close[0] + ",\"direction\":\"low\",\"level\":" + lvlStr + "}";
                string tMsg = "🟠 SWEEP LOW | " + tickerStr + " | TF: " + tfStr + " | Level: " + lvlStr + " | Close: " + priceStr;
                Alert("FSE_SwpL" + CurrentBar, Priority.Medium, WebhookJsonFormat ? jMsg : tMsg, "", 0, Brushes.Black, SweepBrush);
            }
        }

        private string FormatPrice(double price)
        {
            return Instrument != null ? Instrument.MasterInstrument.FormatPrice(price) : price.ToString("0.00");
        }

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Brush EnsureFrozen(Brush brush)
        {
            if (brush == null || brush.IsFrozen)
                return brush;
            if (brush.CanFreeze)
            {
                brush.Freeze();
                return brush;
            }
            var clone = brush.Clone();
            clone.Freeze();
            return clone;
        }

        private static Brush Faded(Brush brush, byte alpha)
        {
            var solid = brush as SolidColorBrush;
            if (solid == null)
                return brush;
            var faded = new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
            faded.Freeze();
            return faded;
        }

        private static DashStyleHelper ToDashStyle(FibStructLineStyle style)
        {
            switch (style)
            {
                case FibStructLineStyle.Solid:
                    return DashStyleHelper.Solid;
                case FibStructLineStyle.Dotted:
                    return DashStyleHelper.Dot;
                default:
                    return DashStyleHelper.Dash;
            }
        }

        private static TextPosition ToTextPosition(FibStructDashPosition pos)
        {
            switch (pos)
            {
                case FibStructDashPosition.TopLeft:
                    return TextPosition.TopLeft;
                case FibStructDashPosition.BottomLeft:
                    return TextPosition.BottomLeft;
                case FibStructDashPosition.BottomRight:
                    return TextPosition.BottomRight;
                default:
                    return TextPosition.TopRight;
            }
        }

        // ══════════════════════════════════════
        // PROPERTIES
        // ══════════════════════════════════════

        // ── Main Settings ──
        [Range(3, 50)]
        [NinjaScriptProperty]
        [Display(Name = "Swing Detection Length", Order = 1, GroupName = "01. Main Settings",
            Description = "Lookback for pivot detection. Higher = larger swings, fewer structures. Recommended: 8-20")]
        public int SwingLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATR Swing Filter", Order = 2, GroupName = "01. Main Settings",
            Description = "Filter out minor swings smaller than ATR threshold")]
        public bool AtrSwingFilter { get; set; }

        [Range(0.1, 3.0)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Filter Multiplier", Order = 3, GroupName = "01. Main Settings",
            Description = "Minimum swing size as multiple of ATR. Recommended: 0.3-1.0")]
        public double AtrFilterMultiplier { get; set; }

        [Range(1, 50)]
        [NinjaScriptProperty]
        [Display(Name = "Signal Cooldown (bars)", Order = 4, GroupName = "01. Main Settings",
            Description = "Minimum bars between consecutive BUY or SELL signals. Recommended: 3-10")]
        public int SignalCooldown { get; set; }

        // ── Fibonacci Levels ──
        [NinjaScriptProperty]
        [Display(Name = "Show Fibonacci Levels", Order = 1, GroupName = "02. Fibonacci Levels")]
        public bool ShowFibLevels { get; set; }

        [Range(5, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Fib Extension Bars", Order = 2, GroupName = "02. Fibonacci Levels",
            Description = "How many bars to extend Fibonacci lines to the right")]
        public int FibExtensionBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show 0.236", Order = 3, GroupName = "02. Fibonacci Levels")]
        public bool ShowFib236 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show 0.382", Order = 4, GroupName = "02. Fibonacci Levels")]
        public bool ShowFib382 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show 0.500", Order = 5, GroupName = "02. Fibonacci Levels")]
        public bool ShowFib500 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show 0.618", Order = 6, GroupName = "02. Fibonacci Levels")]
        public bool ShowFib618 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show 0.786", Order = 7, GroupName = "02. Fibonacci Levels")]
        public bool ShowFib786 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Target (-0.618)", Order = 8, GroupName = "02. Fibonacci Levels")]
        public bool ShowFibTarget { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show -0.5", Order = 9, GroupName = "02. Fibonacci Levels")]
        public bool ShowFibTgt50 { get; set; }

        [Range(0.05, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Confluence ATR Tolerance", Order = 10, GroupName = "02. Fibonacci Levels",
            Description = "Max distance (in ATR) between price and Fib level to count as confluence. Recommended: 0.2-0.5")]
        public double ConfluenceAtrTolerance { get; set; }

        // ── Structure ──
        [NinjaScriptProperty]
        [Display(Name = "Show BOS / CHoCH", Order = 1, GroupName = "03. Structure")]
        public bool ShowStructure { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Swing Labels", Order = 2, GroupName = "03. Structure",
            Description = "Show HH/HL/LH/LL swing point labels on confirmed pivots")]
        public bool ShowSwingLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Engulfing Signals", Order = 3, GroupName = "03. Structure")]
        public bool ShowEngulfing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strict Engulfing", Order = 4, GroupName = "03. Structure",
            Description = "Strict (classic): current body must strictly exceed previous body on both sides. Recommended: ON")]
        public bool StrictEngulfing { get; set; }

        // ── Liquidity ──
        [NinjaScriptProperty]
        [Display(Name = "Show EQH / EQL", Order = 1, GroupName = "04. Liquidity")]
        public bool ShowEqLevels { get; set; }

        [Range(0.01, 0.5)]
        [NinjaScriptProperty]
        [Display(Name = "EQ ATR Tolerance", Order = 2, GroupName = "04. Liquidity",
            Description = "Max distance (in ATR) between two swings to count as equal. Recommended: 0.05-0.2")]
        public double EqAtrTolerance { get; set; }

        [Range(5, 500)]
        [NinjaScriptProperty]
        [Display(Name = "EQ Line Extension (bars)", Order = 3, GroupName = "04. Liquidity",
            Description = "How many bars to extend EQH/EQL lines to the right. Line auto-trims at sweep bar")]
        public int EqExtensionBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Liquidity Sweeps", Order = 4, GroupName = "04. Liquidity",
            Description = "Detect wick-through + close-back-inside patterns at swing highs/lows or EQH/EQL")]
        public bool ShowSweeps { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sweeps Boost Confluence", Order = 5, GroupName = "04. Liquidity",
            Description = "Add weight to confluence score when a liquidity sweep occurs")]
        public bool SweepsBoostConfluence { get; set; }

        // ── Visual ──
        [NinjaScriptProperty]
        [Display(Name = "Show Buy/Sell Signals", Order = 1, GroupName = "05. Visual",
            Description = "Show confirmed entry signals based on structure + Fibonacci confluence")]
        public bool ShowSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Structure Line Style", Order = 2, GroupName = "05. Visual")]
        public FibStructLineStyle StructureLineStyle { get; set; }

        [Range(1, 4)]
        [NinjaScriptProperty]
        [Display(Name = "Structure Line Width", Order = 3, GroupName = "05. Visual")]
        public int StructureLineWidth { get; set; }

        [Range(0, 5000)]
        [NinjaScriptProperty]
        [Display(Name = "Historical Plot Bars", Order = 4, GroupName = "05. Visual",
            Description = "Limit historical drawing objects. 0 = no historical event markers; higher values draw only the most recent historical bars.")]
        public int HistoricalPlotBars { get; set; }

        // ── Dashboard ──
        [NinjaScriptProperty]
        [Display(Name = "Show Dashboard", Order = 1, GroupName = "06. Dashboard")]
        public bool ShowDashboard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Position", Order = 2, GroupName = "06. Dashboard")]
        public FibStructDashPosition DashboardPosition { get; set; }

        // ── Alerts ──
        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Order = 1, GroupName = "07. Alerts",
            Description = "Fire NinjaTrader alerts on signals, BOS/CHoCH and sweeps (realtime only)")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook JSON Format", Order = 2, GroupName = "07. Alerts",
            Description = "Alert messages in JSON format for webhook/bot integrations")]
        public bool WebhookJsonFormat { get; set; }

        // ── Colors ──
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

        [XmlIgnore]
        [Display(Name = "Fib Lines", Order = 3, GroupName = "08. Colors")]
        public Brush FibBrush { get; set; }

        [Browsable(false)]
        public string FibBrushSerializable
        {
            get { return Serialize.BrushToString(FibBrush); }
            set { FibBrush = EnsureFrozen(Serialize.StringToBrush(value)); }
        }

        [XmlIgnore]
        [Display(Name = "Confluence", Order = 4, GroupName = "08. Colors")]
        public Brush ConfluenceBrush { get; set; }

        [Browsable(false)]
        public string ConfluenceBrushSerializable
        {
            get { return Serialize.BrushToString(ConfluenceBrush); }
            set { ConfluenceBrush = EnsureFrozen(Serialize.StringToBrush(value)); }
        }

        [XmlIgnore]
        [Display(Name = "EQH/EQL", Order = 5, GroupName = "08. Colors")]
        public Brush EqBrush { get; set; }

        [Browsable(false)]
        public string EqBrushSerializable
        {
            get { return Serialize.BrushToString(EqBrush); }
            set { EqBrush = EnsureFrozen(Serialize.StringToBrush(value)); }
        }

        [XmlIgnore]
        [Display(Name = "Sweep", Order = 6, GroupName = "08. Colors")]
        public Brush SweepBrush { get; set; }

        [Browsable(false)]
        public string SweepBrushSerializable
        {
            get { return Serialize.BrushToString(SweepBrush); }
            set { SweepBrush = EnsureFrozen(Serialize.StringToBrush(value)); }
        }
    }
}
