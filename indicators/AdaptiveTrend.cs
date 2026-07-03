#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public enum AdaptiveTrendTheme { Auto, Dark, Light }
    public enum AdaptiveTrendMtfMode { Auto, Manual }
    public enum AdaptiveTrendMtfStrictness { Loose, Moderate, Strict }
    public enum AdaptiveTrendSlMode { ATR, Band, FixedPercent }
    public enum AdaptiveTrendTrailMode { ATR, Band, AtrToBand }
    public enum AdaptiveTrendTimezone { UTC, NewYork, London, Tokyo }
    public enum AdaptiveTrendDashPosition { TopLeft, TopRight, BottomLeft, BottomRight }
    public enum AdaptiveTrendFontSize { Small, Normal, Large }
    public enum AdaptiveTrendLabelSize { Tiny, Small, Normal }

    public class AdaptiveTrend : Indicator
    {
        private const string IndicatorVersion = "v5.1.0";
        private const int PendingSlots = 5;

        // Indicator bank
        private static readonly int[] AtrLens = { 5, 8, 10, 13, 16, 21, 30, 40, 50 };
        private static readonly int[] RsiLens = { 5, 7, 9, 11, 13, 16, 20, 25, 30 };
        private static readonly int[] SmaAtrLens = { 20, 35, 50, 70, 100, 150, 200 };

        private ATR[] atrBank;
        private RSI[] rsiBank;
        private SMA[] smaAtrBank;
        private ATR atrProfileInd;
        private ATR atr20Ind;
        private SUM sumAbsChange;
        private EMA erEma;
        private EMA vcEma;
        private EMA emaClose12;
        private EMA emaClose26;
        private EMA macdSignalEma;
        private SMA volSma20Ind;
        private MAX volMax50Ind;
        private EMA htfEmaFast;
        private EMA htfEmaSlow;

        private Series<double> absChange;
        private Series<double> ret1;
        private Series<double> erSeries;
        private Series<double> vcSeries;
        private Series<double> macdSeries;
        private Series<double> rsiSeries;
        private Series<double> bandSeries;

        // SuperTrend state
        private int trendDir = 1;
        private double stBand = double.NaN;
        private int barsSinceFlip = 100;

        // ADX state (Wilder)
        private double smoothTR, smoothDMp, smoothDMm, adxVal;

        // Volume profile state
        private double hvBarPrice = double.NaN;
        private int hvBarAge;

        // Self-learning state
        private double adaptiveGate;
        private int totalSignals, winSignals;
        private readonly double[] pendPrice = new double[PendingSlots];
        private readonly int[] pendDir = new int[PendingSlots];
        private readonly int[] pendBar = new int[PendingSlots];
        private readonly bool[] pendActive = new bool[PendingSlots];
        private readonly double[] pendAtr = new double[PendingSlots];

        private string lastSignal = "-";
        private int barsSinceSignal;

        // Position state
        private int posDir;
        private int entryContracts = 1;
        private double moneyStopPrice = double.NaN;
        private double entryPrice = double.NaN;
        private double slPrice = double.NaN;
        private double entrySL = double.NaN;
        private double tp1Price = double.NaN;
        private double tp2Price = double.NaN;
        private double tp3Price = double.NaN;
        private bool tp1Hit, tp2Hit, tp3Hit;
        private int entryBarIdx = -1;
        private string exitReason = "";

        // Fill segment tracking
        private int segmentId;
        private int segmentStartBar;

        // Drawing window (performance)
        private DateTime plotCutoff = DateTime.MinValue;
        private int firstDrawBar = -1;

        private double usdPerPointEff = 2.0;

        // Cached visuals (created once in DataLoaded)
        private SimpleFont tpslFont;
        private SimpleFont signalFont;
        private SimpleFont dashFont;
        private Brush dashAreaDark;
        private Brush dashAreaLight;

        // Public output series
        private Series<double> trendSeries;
        private Series<double> mlScoreSeries;

        // Incremental autocorrelation state
        private double acCorrSum, acR1Sq, acR2Sq;

        // Redraw-on-change trackers
        private bool tpslLinesDrawn;
        private double lastSLDrawn = double.NaN;
        private double lastMoneyStopDrawn = double.NaN;
        private readonly double[] curAvgLvl = new double[10];
        private readonly int[] curAvgQty = new int[10];
        private readonly double[] lastAvgLvl = new double[10];
        private readonly int[] lastAvgQty = new int[10];
        private int lastAvgCount = -1;

        // HTF selection resolved at Configure
        private BarsPeriodType htfType = BarsPeriodType.Day;
        private int htfValue = 1;
        private string htfLabel = "D";

        private Brush bullBgBrush;
        private Brush bearBgBrush;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdaptiveTrend";
                Description = "Adaptive SuperTrend with ML signal filter, MTF confluence and TP/SL system.";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel = true;

                // Main
                AutoTune = true;
                AtrLength = 13;
                BaseMultiplier = 2.5;

                // Adaptive
                ProfileLookback = 100;
                RegimeSensitivity = 1.0;
                ShowProfile = true;

                // MTF
                UseMTF = true;
                MtfMode = AdaptiveTrendMtfMode.Auto;
                MtfManualType = BarsPeriodType.Minute;
                MtfManualValue = 240;
                MtfStrictness = AdaptiveTrendMtfStrictness.Moderate;

                // ML
                UseMLFilter = false;
                MlGate = 21.0;
                SelfLearn = true;
                EvalHorizon = 15;
                ShowMLDebug = false;

                // ML weights
                W1Momentum = 0.15; W2Volume = 0.08; W3Trend = 0.15; W4Volatility = -0.08;
                W5Distance = 0.10; W6Macd = 0.08; W7Structure = 0.08; W8Regime = 0.04;
                W9Mtf = 0.12; W10Adx = 0.10; W11Divergence = 0.08; W12VolProfile = 0.06;
                W13Session = 0.04; WBias = 0.0;

                // Filters
                Cushion = 0.15;
                Cooldown = 3;
                UseMomentum = true;
                RsiLength = 13;
                RsiThreshold = 45.0;
                UseVolumeFilter = false;
                VolMultiplier = 1.2;

                // Session
                UseSessionFilter = false;
                SessionTimezone = AdaptiveTrendTimezone.UTC;
                SessionKillStart = 21;
                SessionKillEnd = 1;

                // TP/SL
                UseTPSL = true;
                SlMode = AdaptiveTrendSlMode.Band;
                SlAtrMult = 1.5;
                SlFixedPct = 2.0;
                TpLevels = 3;
                Tp1Mult = 2.0;
                Tp2Mult = 4.0;
                Tp3Mult = 6.0;
                UseTrailing = true;
                TrailMode = AdaptiveTrendTrailMode.Band;
                TrailAtrMult = 1.2;
                ShowTPSLLines = true;
                TpslLabelSize = AdaptiveTrendLabelSize.Small;
                ShowRR = false;

                // Risk management / averaging
                ShowAvgEntries = true;
                UsdPerPoint = 0.0;
                RiskUsd = 250.0;
                ContractsPerEntry = 1;
                MinAvgSepPoints = 10.0;

                // Visual
                PlotDays = 5;
                Theme = AdaptiveTrendTheme.Auto;
                ShowSignals = true;
                ShowFiltered = false;
                ShowMLRejected = false;
                ShowBand = true;
                ShowFill = true;
                ShowStrengthBg = false;
                ShowRegimeBadge = false;
                ShowDivergence = false;

                // Dashboard
                ShowDashboard = true;
                DashPosition = AdaptiveTrendDashPosition.TopRight;
                DashFontSize = AdaptiveTrendFontSize.Small;

                // Alerts
                WebhookJson = false;

                // Colors
                BullColor = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)); BullColor.Freeze();
                BearColor = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)); BearColor.Freeze();

                // Advanced
                MinAdaptiveMult = 1.0;
                MaxAdaptiveMult = 5.0;

                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "BandUp");
                AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Line, "BandDown");
            }
            else if (State == State.Configure)
            {
                // State-machine logic (SuperTrend, ADX, position) requires one execution per bar
                Calculate = Calculate.OnBarClose;
                ResolveHtf();
                AddDataSeries(htfType, htfValue);
            }
            else if (State == State.DataLoaded)
            {
                atrBank = new ATR[AtrLens.Length];
                for (int i = 0; i < AtrLens.Length; i++)
                    atrBank[i] = ATR(AtrLens[i]);

                rsiBank = new RSI[RsiLens.Length];
                for (int i = 0; i < RsiLens.Length; i++)
                    rsiBank[i] = RSI(Close, RsiLens[i], 1);

                smaAtrBank = new SMA[SmaAtrLens.Length];
                for (int i = 0; i < SmaAtrLens.Length; i++)
                    smaAtrBank[i] = SMA(ATR(13), SmaAtrLens[i]);

                atrProfileInd = ATR(ProfileLookback);
                atr20Ind = ATR(20);

                absChange = new Series<double>(this);
                ret1 = new Series<double>(this);
                erSeries = new Series<double>(this);
                vcSeries = new Series<double>(this);
                macdSeries = new Series<double>(this);
                rsiSeries = new Series<double>(this);
                bandSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                trendSeries = new Series<double>(this);
                mlScoreSeries = new Series<double>(this);

                tpslFont = new SimpleFont("Arial", TpslFontSize());
                signalFont = new SimpleFont("Arial", 11);
                int dashFontSize = DashFontSize == AdaptiveTrendFontSize.Small ? 11 : DashFontSize == AdaptiveTrendFontSize.Normal ? 13 : 16;
                dashFont = new SimpleFont("Consolas", dashFontSize);
                dashAreaDark = new SolidColorBrush(Color.FromArgb(230, 0x13, 0x17, 0x22));
                dashAreaDark.Freeze();
                dashAreaLight = new SolidColorBrush(Color.FromArgb(230, 0xFF, 0xFF, 0xFF));
                dashAreaLight.Freeze();

                sumAbsChange = SUM(absChange, ProfileLookback);
                int emaSmoothLen = Math.Max(ProfileLookback / 3, 10);
                erEma = EMA(erSeries, emaSmoothLen);
                vcEma = EMA(vcSeries, emaSmoothLen);

                emaClose12 = EMA(Close, 12);
                emaClose26 = EMA(Close, 26);
                macdSignalEma = EMA(macdSeries, 9);

                volSma20Ind = SMA(Volume, 20);
                volMax50Ind = MAX(Volume, 50);

                htfEmaFast = EMA(Closes[1], 20);
                htfEmaSlow = EMA(Closes[1], 50);

                usdPerPointEff = UsdPerPoint > 0 ? UsdPerPoint : Instrument.MasterInstrument.PointValue;
                if (usdPerPointEff <= 0)
                    usdPerPointEff = 1.0;

                plotCutoff = PlotDays > 0 && Bars != null && Bars.Count > 0
                    ? Bars.GetTime(Bars.Count - 1).AddDays(-PlotDays)
                    : DateTime.MinValue;

                ResetState();

                Plots[0].Brush = BullColor;
                Plots[1].Brush = BearColor;

                SolidColorBrush bullSolid = BullColor as SolidColorBrush;
                SolidColorBrush bearSolid = BearColor as SolidColorBrush;
                Color bc = bullSolid != null ? bullSolid.Color : Colors.LimeGreen;
                Color rc = bearSolid != null ? bearSolid.Color : Colors.Red;
                bullBgBrush = new SolidColorBrush(Color.FromArgb(20, bc.R, bc.G, bc.B)); bullBgBrush.Freeze();
                bearBgBrush = new SolidColorBrush(Color.FromArgb(20, rc.R, rc.G, rc.B)); bearBgBrush.Freeze();
            }
        }

        private void ResetState()
        {
            trendDir = 1;
            stBand = double.NaN;
            barsSinceFlip = 100;

            smoothTR = 0; smoothDMp = 0; smoothDMm = 0; adxVal = 0;

            hvBarPrice = double.NaN;
            hvBarAge = 0;

            adaptiveGate = MlGate;
            totalSignals = 0;
            winSignals = 0;
            for (int i = 0; i < PendingSlots; i++)
            {
                pendActive[i] = false;
                pendPrice[i] = 0; pendDir[i] = 0; pendBar[i] = 0; pendAtr[i] = 0;
            }

            lastSignal = "-";
            barsSinceSignal = 0;

            posDir = 0;
            entryContracts = 1;
            moneyStopPrice = double.NaN;
            entryPrice = double.NaN;
            slPrice = double.NaN;
            entrySL = double.NaN;
            tp1Price = double.NaN; tp2Price = double.NaN; tp3Price = double.NaN;
            tp1Hit = false; tp2Hit = false; tp3Hit = false;
            entryBarIdx = -1;
            exitReason = "";

            segmentId = 0;
            segmentStartBar = 0;
            firstDrawBar = -1;

            acCorrSum = 0; acR1Sq = 0; acR2Sq = 0;

            tpslLinesDrawn = false;
            lastSLDrawn = double.NaN;
            lastMoneyStopDrawn = double.NaN;
            lastAvgCount = -1;
        }

        private void ResolveHtf()
        {
            if (MtfMode == AdaptiveTrendMtfMode.Manual)
            {
                htfType = MtfManualType;
                htfValue = MtfManualValue;
                htfLabel = FormatTfLabel(htfType, htfValue);
                return;
            }

            BarsPeriodType bpt = BarsPeriod.BarsPeriodType;
            int v = BarsPeriod.Value;

            if (bpt == BarsPeriodType.Second || bpt == BarsPeriodType.Tick || bpt == BarsPeriodType.Volume || bpt == BarsPeriodType.Range)
            {
                htfType = BarsPeriodType.Minute; htfValue = 5;
            }
            else if (bpt == BarsPeriodType.Minute)
            {
                if (v == 1 || v == 2 || v == 3) { htfType = BarsPeriodType.Minute; htfValue = 15; }
                else if (v == 5) { htfType = BarsPeriodType.Minute; htfValue = 30; }
                else if (v == 15) { htfType = BarsPeriodType.Minute; htfValue = 60; }
                else if (v == 30) { htfType = BarsPeriodType.Minute; htfValue = 120; }
                else if (v == 60 || v == 120) { htfType = BarsPeriodType.Minute; htfValue = 240; }
                else if (v == 240) { htfType = BarsPeriodType.Day; htfValue = 1; }
                else { htfType = BarsPeriodType.Day; htfValue = 1; }
            }
            else if (bpt == BarsPeriodType.Day) { htfType = BarsPeriodType.Week; htfValue = 1; }
            else if (bpt == BarsPeriodType.Week) { htfType = BarsPeriodType.Month; htfValue = 1; }
            else { htfType = BarsPeriodType.Day; htfValue = 1; }

            htfLabel = FormatTfLabel(htfType, htfValue);
        }

        private static string FormatTfLabel(BarsPeriodType type, int value)
        {
            switch (type)
            {
                case BarsPeriodType.Minute: return value.ToString() + "m";
                case BarsPeriodType.Day: return value == 1 ? "D" : value + "D";
                case BarsPeriodType.Week: return value == 1 ? "W" : value + "W";
                case BarsPeriodType.Month: return value == 1 ? "M" : value + "M";
                default: return type + " " + value;
            }
        }

        // ── Utilities ─────────────────────────────────────
        private static double SafeDiv(double num, double den, double fallback)
        {
            return den != 0 && !double.IsNaN(num) && !double.IsNaN(den) ? num / den : fallback;
        }

        private static double Clamp(double val, double lo, double hi)
        {
            return Math.Max(lo, Math.Min(hi, val));
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Clamp(t, 0.0, 1.0);
        }

        private static double Sigmoid100(double x, double center, double k)
        {
            return 100.0 / (1.0 + Math.Exp(-k * (x - center)));
        }

        private double BankValue(ISeries<double>[] bank, int[] lens, int len)
        {
            if (len <= lens[0])
                return bank[0][0];
            for (int i = 1; i < lens.Length; i++)
            {
                if (len <= lens[i])
                    return Lerp(bank[i - 1][0], bank[i][0], (len - lens[i - 1]) / (double)(lens[i] - lens[i - 1]));
            }
            return bank[lens.Length - 1][0];
        }

        private double GetAtrInterp(int len) { return BankValue(atrBank, AtrLens, len); }
        private double GetRsiInterp(int len) { return BankValue(rsiBank, RsiLens, len); }
        private double GetSmaAtrInterp(int len) { return BankValue(smaAtrBank, SmaAtrLens, len); }

        private double HighestVal(ISeries<double> s, int offset, int len)
        {
            double m = double.MinValue;
            for (int i = offset; i < offset + len; i++)
            {
                if (i > CurrentBar) break;
                m = Math.Max(m, s[i]);
            }
            return m == double.MinValue ? s[Math.Min(offset, CurrentBar)] : m;
        }

        private double LowestVal(ISeries<double> s, int offset, int len)
        {
            double m = double.MaxValue;
            for (int i = offset; i < offset + len; i++)
            {
                if (i > CurrentBar) break;
                m = Math.Min(m, s[i]);
            }
            return m == double.MaxValue ? s[Math.Min(offset, CurrentBar)] : m;
        }

        private string Fmt(double price)
        {
            return Instrument.MasterInstrument.FormatPrice(Instrument.MasterInstrument.RoundToTickSize(price));
        }

        private bool IsDarkTheme()
        {
            if (Theme == AdaptiveTrendTheme.Dark) return true;
            if (Theme == AdaptiveTrendTheme.Light) return false;
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

        private int TpslFontSize()
        {
            switch (TpslLabelSize)
            {
                case AdaptiveTrendLabelSize.Tiny: return 9;
                case AdaptiveTrendLabelSize.Normal: return 12;
                default: return 10;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar == 0)
                ResetState();

            // ── Base series updates ──
            absChange[0] = CurrentBar > 0 ? Math.Abs(Close[0] - Close[1]) : 0.0;
            ret1[0] = CurrentBar > 0 && Close[1] != 0 ? Close[0] / Close[1] - 1.0 : 0.0;

            // ── Instrument profiling ──
            double priceChange = CurrentBar >= ProfileLookback ? Math.Abs(Close[0] - Close[ProfileLookback]) : double.NaN;
            double totalPath = sumAbsChange[0];
            double efficiencyRatio = SafeDiv(priceChange, totalPath, 0.5);
            double atrProfile = atrProfileInd[0];
            double normVol = SafeDiv(atrProfile, Close[0], 0.01) * 100.0;
            double volCluster = SafeDiv(atr20Ind[0], atrProfile, 1.0);

            int acWindow = (int)Clamp(ProfileLookback, 50, 100);
            if (CurrentBar <= acWindow + 1 || CurrentBar % 500 == 0)
            {
                // Full recompute: warmup + periodic refresh to cancel floating-point drift
                acCorrSum = 0; acR1Sq = 0; acR2Sq = 0;
                int maxI = Math.Min(acWindow - 1, Math.Max(CurrentBar - 1, 0));
                for (int i = 0; i <= maxI; i++)
                {
                    double r1 = ret1[i];
                    double r2 = i + 1 <= CurrentBar ? ret1[i + 1] : 0.0;
                    acCorrSum += r1 * r2; acR1Sq += r1 * r1; acR2Sq += r2 * r2;
                }
            }
            else
            {
                // O(1) rolling update: add newest pair, drop the one leaving the window
                double rNew0 = ret1[0], rNew1 = ret1[1];
                double rOldW = ret1[acWindow], rOldW1 = ret1[acWindow + 1];
                acCorrSum += rNew0 * rNew1 - rOldW * rOldW1;
                acR1Sq += rNew0 * rNew0 - rOldW * rOldW;
                acR2Sq += rNew1 * rNew1 - rOldW1 * rOldW1;
            }
            double autoCorr = SafeDiv(acCorrSum, Math.Sqrt(Math.Max(acR1Sq, 0.0) * Math.Max(acR2Sq, 0.0)), 0.0);

            // ── Regime classification ──
            erSeries[0] = efficiencyRatio;
            vcSeries[0] = volCluster;
            double erSmooth = erEma[0];
            double vcSmooth = vcEma[0];

            double trendScoreR = erSmooth * RegimeSensitivity;
            double rangeScoreR = (1.0 - erSmooth) * (1.0 - Math.Abs(autoCorr)) * RegimeSensitivity;
            double volatScoreR = Clamp(vcSmooth - 1.0, 0.0, 2.0) * RegimeSensitivity;

            string regime;
            if (trendScoreR >= rangeScoreR && trendScoreR >= volatScoreR)
                regime = "TRENDING";
            else if (rangeScoreR >= trendScoreR && rangeScoreR >= volatScoreR)
                regime = "RANGING";
            else
                regime = "VOLATILE";

            double totalSR = trendScoreR + rangeScoreR + volatScoreR;
            double regimeConfidence = totalSR > 0 ? Math.Max(trendScoreR, Math.Max(rangeScoreR, volatScoreR)) / totalSR * 100.0 : 33.0;

            // ── Auto-tuned parameters ──
            double wT = trendScoreR / Math.Max(totalSR, 1e-10);
            double wR = rangeScoreR / Math.Max(totalSR, 1e-10);
            double wV = volatScoreR / Math.Max(totalSR, 1e-10);

            int effectiveAtrLen = AutoTune ? (int)Clamp((wT * 10.0 + wR * 16.0 + wV * 21.0) * Clamp(normVol / 1.5, 0.7, 1.8), 5, 50) : AtrLength;
            double effectiveBaseMult = AutoTune ? Clamp((wT * 2.0 + wR * 3.2 + wV * 3.8) * Clamp(normVol / 1.0, 0.8, 1.5), 1.2, 6.0) : BaseMultiplier;
            double effectiveCushion = AutoTune ? Clamp(wT * 0.05 + wR * 0.25 + wV * 0.15, 0.0, 0.5) : Cushion;
            int effectiveCooldown = AutoTune ? (int)Clamp(wT * 2.0 + wR * 5.0 + wV * 3.0, 1, 10) : Cooldown;
            double effectiveRsiThresh = AutoTune ? Clamp(wT * 40.0 + wR * 52.0 + wV * 45.0, 35.0, 58.0) : RsiThreshold;
            int effectiveRsiLen = AutoTune ? (int)Clamp(effectiveAtrLen * 0.9, 5, 30) : RsiLength;
            int adaptSmooth = AutoTune ? (int)Clamp(effectiveAtrLen * 4.0, 20, 200) : 55;

            // ── Core SuperTrend ──
            int warmupBars = Math.Max(ProfileLookback, 55);
            bool isWarmedUp = CurrentBar >= warmupBars;

            double atrVal = GetAtrInterp(effectiveAtrLen);
            if (double.IsNaN(atrVal) || atrVal <= 0)
                atrVal = Math.Max(High[0] - Low[0], TickSize);
            double atrSmaVal = GetSmaAtrInterp(adaptSmooth);
            if (double.IsNaN(atrSmaVal) || atrSmaVal <= 0)
                atrSmaVal = atrVal;
            double volRatio = atrSmaVal > 0 ? atrVal / atrSmaVal : 1.0;
            double adaptiveMult = Math.Max(MinAdaptiveMult, Math.Min(MaxAdaptiveMult, effectiveBaseMult * volRatio));

            double hl2 = (High[0] + Low[0]) / 2.0;
            double upperBand = hl2 + adaptiveMult * atrVal;
            double lowerBand = hl2 - adaptiveMult * atrVal;

            int prevTrendDir = trendDir;
            barsSinceFlip++;
            double prevBand = double.IsNaN(stBand) ? (trendDir == 1 ? lowerBand : upperBand) : stBand;
            double flipCushion = effectiveCushion * atrVal;

            if (trendDir == 1)
            {
                stBand = Math.Max(lowerBand, prevBand);
                if (Close[0] < stBand - flipCushion && barsSinceFlip >= effectiveCooldown)
                {
                    trendDir = -1; stBand = upperBand; barsSinceFlip = 0;
                }
            }
            else
            {
                stBand = Math.Min(upperBand, prevBand);
                if (Close[0] > stBand + flipCushion && barsSinceFlip >= effectiveCooldown)
                {
                    trendDir = 1; stBand = lowerBand; barsSinceFlip = 0;
                }
            }

            bool rawFlip = trendDir != prevTrendDir;
            if (rawFlip)
            {
                segmentId++;
                segmentStartBar = CurrentBar;
            }

            bandSeries[0] = stBand;

            bool canDraw = PlotDays <= 0 || Time[0] >= plotCutoff;
            if (canDraw && firstDrawBar < 0)
                firstDrawBar = CurrentBar;

            if (canDraw && ShowBand && trendDir == 1)
                Values[0][0] = stBand;
            else
                Values[0].Reset();

            if (canDraw && ShowBand && trendDir == -1)
                Values[1][0] = stBand;
            else
                Values[1].Reset();

            // ── MTF confluence ──
            int htfTrend = 1;
            if (CurrentBars[1] > 50)
                htfTrend = htfEmaFast[0] > htfEmaSlow[0] ? 1 : -1;
            bool mtfAligned = !UseMTF || trendDir == htfTrend;
            bool mtfHardBlock = UseMTF && MtfStrictness == AdaptiveTrendMtfStrictness.Strict && !mtfAligned;

            // ── ADX (Wilder) ──
            const double adxSmoothing = 14.0;
            double dmPlus = CurrentBar > 0 ? Math.Max(High[0] - High[1], 0.0) : 0.0;
            double dmMinus = CurrentBar > 0 ? Math.Max(Low[1] - Low[0], 0.0) : 0.0;
            double trueRange = CurrentBar > 0
                ? Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])))
                : High[0] - Low[0];
            smoothTR = smoothTR - smoothTR / adxSmoothing + trueRange;
            smoothDMp = smoothDMp - smoothDMp / adxSmoothing + (dmPlus > dmMinus && dmPlus > 0 ? dmPlus : 0.0);
            smoothDMm = smoothDMm - smoothDMm / adxSmoothing + (dmMinus > dmPlus && dmMinus > 0 ? dmMinus : 0.0);
            double diPlus = smoothTR > 0 ? smoothDMp / smoothTR * 100.0 : 0.0;
            double diMinus = smoothTR > 0 ? smoothDMm / smoothTR * 100.0 : 0.0;
            double dx = diPlus + diMinus > 0 ? Math.Abs(diPlus - diMinus) / (diPlus + diMinus) * 100.0 : 0.0;
            adxVal = adxVal + (dx - adxVal) / adxSmoothing;

            // ── RSI + divergence ──
            double rsiVal = GetRsiInterp(effectiveRsiLen);
            if (double.IsNaN(rsiVal)) rsiVal = 50.0;
            rsiSeries[0] = rsiVal;

            const int pivotLookback = 5;
            bool bullDivergence = false, bearDivergence = false;
            if (CurrentBar >= pivotLookback * 2)
            {
                double priceLow1 = LowestVal(Low, 0, pivotLookback);
                double priceLow2 = LowestVal(Low, pivotLookback, pivotLookback);
                double rsiLow1 = LowestVal(rsiSeries, 0, pivotLookback);
                double rsiLow2 = LowestVal(rsiSeries, pivotLookback, pivotLookback);
                double priceHigh1 = HighestVal(High, 0, pivotLookback);
                double priceHigh2 = HighestVal(High, pivotLookback, pivotLookback);
                double rsiHigh1 = HighestVal(rsiSeries, 0, pivotLookback);
                double rsiHigh2 = HighestVal(rsiSeries, pivotLookback, pivotLookback);
                bullDivergence = priceLow1 < priceLow2 && rsiLow1 > rsiLow2 && rsiVal < 40;
                bearDivergence = priceHigh1 > priceHigh2 && rsiHigh1 < rsiHigh2 && rsiVal > 60;
            }

            // ── Volume profile ──
            bool hasVolume = Volume[0] > 0;
            double volSma20 = volSma20Ind[0];
            double volMax50 = volMax50Ind[0];
            hvBarAge++;
            if (hasVolume && Volume[0] >= volMax50)
            {
                hvBarPrice = hl2; hvBarAge = 0;
            }
            if (hvBarAge > 50)
                hvBarPrice = double.NaN;
            double distToHV = double.IsNaN(hvBarPrice) ? 999.0 : Math.Abs(Close[0] - hvBarPrice) / Math.Max(atrVal, 1e-10);
            bool nearVolZone = distToHV < 1.5;

            // ── Session quality ──
            int currentHour = GetSessionHour();
            bool isDailyOrAbove = BarsPeriod.BarsPeriodType == BarsPeriodType.Day
                || BarsPeriod.BarsPeriodType == BarsPeriodType.Week
                || BarsPeriod.BarsPeriodType == BarsPeriodType.Month
                || BarsPeriod.BarsPeriodType == BarsPeriodType.Year;
            double sessionScore = 50.0;
            if (!isDailyOrAbove)
            {
                if (currentHour >= 13 && currentHour <= 17) sessionScore = 100.0;
                else if (currentHour >= 8 && currentHour <= 12) sessionScore = 80.0;
                else if (currentHour >= 18 && currentHour <= 20) sessionScore = 70.0;
                else if (currentHour >= 0 && currentHour <= 7) sessionScore = 30.0;
                else sessionScore = 40.0;
            }

            bool inKillZone = false;
            if (UseSessionFilter && !isDailyOrAbove)
            {
                if (SessionKillStart <= SessionKillEnd)
                    inKillZone = currentHour >= SessionKillStart && currentHour <= SessionKillEnd;
                else
                    inKillZone = currentHour >= SessionKillStart || currentHour <= SessionKillEnd;
            }

            // ── Classic filters ──
            bool momentumOk = !UseMomentum || (trendDir == 1 ? rsiVal >= effectiveRsiThresh : rsiVal <= 100.0 - effectiveRsiThresh);
            bool volumeOk = !UseVolumeFilter || !hasVolume || Volume[0] > volSma20 * VolMultiplier;
            bool sessionOk = !UseSessionFilter || !inKillZone;
            bool classicFiltersOk = momentumOk && volumeOk && sessionOk && !mtfHardBlock;

            // ── ML features + scoring ──
            double f1 = Clamp((trendDir == 1 ? (rsiVal - 50.0) * 2.0 : (50.0 - rsiVal) * 2.0) + 50.0, 0.0, 100.0);
            double f2 = hasVolume && volSma20 > 0 ? Clamp(SafeDiv(Volume[0], volSma20, 1.0) * 50.0, 0.0, 100.0) : 50.0;
            double f3 = erSmooth * 100.0;
            double f4 = Clamp((2.0 - volCluster) * 50.0, 0.0, 100.0);
            double bandDist = trendDir == 1
                ? SafeDiv(Close[0] - stBand, Math.Max(atrVal, 1e-10), 0.0)
                : SafeDiv(stBand - Close[0], Math.Max(atrVal, 1e-10), 0.0);
            double f5 = Sigmoid100(bandDist, 0.5, 3.0);
            macdSeries[0] = emaClose12[0] - emaClose26[0];
            double macdNorm = SafeDiv(macdSeries[0] - macdSignalEma[0], Math.Max(atrVal, 1e-10), 0.0);
            double f6 = Sigmoid100(trendDir == 1 ? macdNorm : -macdNorm, 0.0, 5.0);
            double hh = HighestVal(High, 0, 10), ll = LowestVal(Low, 0, 10);
            double hhPrev = HighestVal(High, 10, 10), llPrev = LowestVal(Low, 10, 10);
            double f7 = trendDir == 1
                ? 20.0 + (hh > hhPrev ? 30.0 : 0.0) + (ll > llPrev ? 30.0 : 0.0)
                : 20.0 + (ll < llPrev ? 30.0 : 0.0) + (hh < hhPrev ? 30.0 : 0.0);
            double f8 = regimeConfidence;
            double f9 = !UseMTF ? 50.0 : mtfAligned ? 100.0 : 0.0;
            double f10 = Clamp(adxVal * 2.5, 0.0, 100.0);
            double f11 = 50.0;
            if (trendDir == 1 && bullDivergence) f11 = 100.0;
            else if (trendDir == -1 && bearDivergence) f11 = 100.0;
            else if (trendDir == 1 && bearDivergence) f11 = 10.0;
            else if (trendDir == -1 && bullDivergence) f11 = 10.0;
            double f12 = nearVolZone ? 100.0 : Clamp((3.0 - distToHV) / 3.0 * 100.0, 0.0, 50.0);
            double f13 = isDailyOrAbove ? 50.0 : sessionScore;

            double rawMLScore = W1Momentum * f1 + W2Volume * f2 + W3Trend * f3 + W4Volatility * f4
                + W5Distance * f5 + W6Macd * f6 + W7Structure * f7 + W8Regime * f8 + W9Mtf * f9
                + W10Adx * f10 + W11Divergence * f11 + W12VolProfile * f12 + W13Session * f13 + WBias;

            double weightSum = Math.Abs(W1Momentum) + Math.Abs(W2Volume) + Math.Abs(W3Trend)
                + Math.Abs(W4Volatility) + Math.Abs(W5Distance) + Math.Abs(W6Macd)
                + Math.Abs(W7Structure) + Math.Abs(W8Regime) + Math.Abs(W9Mtf)
                + Math.Abs(W10Adx) + Math.Abs(W11Divergence) + Math.Abs(W12VolProfile)
                + Math.Abs(W13Session);

            double normalizedScore = weightSum > 0 ? rawMLScore / weightSum : 50.0;
            double mlScore = Sigmoid100(normalizedScore, 50.0, 0.08);

            trendSeries[0] = trendDir;
            mlScoreSeries[0] = mlScore;

            // ── Self-learning ──
            const double decayRate = 0.98;
            bool anyMatured = false;
            int newWins = 0, newTotal = 0;
            for (int i = 0; i < PendingSlots; i++)
            {
                if (!pendActive[i] || CurrentBar - pendBar[i] < EvalHorizon)
                    continue;
                double pm = pendDir[i] == 1 ? Close[0] - pendPrice[i] : pendPrice[i] - Close[0];
                bool win = pm > 0.5 * pendAtr[i];
                pendActive[i] = false;
                anyMatured = true;
                newTotal++;
                if (win) newWins++;
            }
            if (anyMatured)
            {
                totalSignals = (int)Math.Round(totalSignals * decayRate);
                winSignals = (int)Math.Round(winSignals * decayRate);
                totalSignals += newTotal;
                winSignals += newWins;
            }

            if (SelfLearn && totalSignals >= 8)
            {
                double wr = SafeDiv(winSignals, totalSignals, 0.5);
                if (wr > 0.70)
                    adaptiveGate = Math.Max(20.0, adaptiveGate - 1.5);
                else if (wr < 0.50)
                    adaptiveGate = Math.Min(85.0, adaptiveGate + 1.5);
            }
            double effectiveGate = SelfLearn ? adaptiveGate : MlGate;

            // ── Signal logic ──
            bool classicBuy = rawFlip && trendDir == 1 && classicFiltersOk && isWarmedUp;
            bool classicSell = rawFlip && trendDir == -1 && classicFiltersOk && isWarmedUp;
            bool mlPass = !UseMLFilter || mlScore >= effectiveGate;

            bool confirmedBuy = classicBuy && mlPass;
            bool confirmedSell = classicSell && mlPass;
            bool mlRejectedBuy = classicBuy && !mlPass;
            bool mlRejectedSell = classicSell && !mlPass;
            bool filteredFlip = rawFlip && !classicFiltersOk && isWarmedUp;

            if (confirmedBuy || confirmedSell)
            {
                for (int i = 0; i < PendingSlots; i++)
                {
                    if (pendActive[i]) continue;
                    pendPrice[i] = Close[0]; pendDir[i] = trendDir; pendBar[i] = CurrentBar;
                    pendAtr[i] = atrVal; pendActive[i] = true;
                    break;
                }
            }

            barsSinceSignal++;
            if (confirmedBuy) { lastSignal = "LONG"; barsSinceSignal = 0; }
            if (confirmedSell) { lastSignal = "SHORT"; barsSinceSignal = 0; }

            // ── Strength ──
            double distScore = Math.Min(SafeDiv(Math.Abs(Close[0] - stBand), Math.Max(atrVal, 1e-10), 0.0) * 20.0, 50.0);
            double momRaw = trendDir == 1 ? Math.Max(rsiVal - 50.0, 0.0) : Math.Max(50.0 - rsiVal, 0.0);
            double strengthVal = Math.Round(Math.Min(distScore + Math.Min(momRaw, 50.0), 100.0));
            string strengthStr = strengthVal >= 70 ? "Strong" : strengthVal >= 40 ? "Medium" : "Weak";

            // ── TP/SL position tracking ──
            bool posJustClosed = false;
            bool tp1JustHit = false, tp2JustHit = false;
            double slHitPrice = slPrice;

            if (UseTPSL && posDir != 0)
            {
                bool slHit = posDir == 1 ? Close[0] <= slPrice : Close[0] >= slPrice;

                if (slHit)
                {
                    exitReason = "SL";
                    posDir = 0;
                    posJustClosed = true;
                }
                else
                {
                    if (!tp1Hit && (posDir == 1 ? High[0] >= tp1Price : Low[0] <= tp1Price))
                    {
                        tp1Hit = true; tp1JustHit = true;
                        RemoveDrawObject("AT_TP1Lbl");
                        if (tpslLinesDrawn)
                        {
                            RemoveDrawObject("AT_TP1Ray");
                            Draw.Line(this, "AT_TP1Line", false, CurrentBar - Math.Max(entryBarIdx, firstDrawBar), tp1Price, 0, tp1Price, TpLineBrush(), DashStyleHelper.Solid, 1);
                        }
                    }
                    if (TpLevels >= 2 && !tp2Hit && !double.IsNaN(tp2Price) && (posDir == 1 ? High[0] >= tp2Price : Low[0] <= tp2Price))
                    {
                        tp2Hit = true; tp2JustHit = true;
                        RemoveDrawObject("AT_TP2Lbl");
                        if (tpslLinesDrawn)
                        {
                            RemoveDrawObject("AT_TP2Ray");
                            Draw.Line(this, "AT_TP2Line", false, CurrentBar - Math.Max(entryBarIdx, firstDrawBar), tp2Price, 0, tp2Price, TpLineBrush(), DashStyleHelper.Solid, 1);
                        }
                    }
                    if (TpLevels >= 3 && !tp3Hit && !double.IsNaN(tp3Price) && (posDir == 1 ? High[0] >= tp3Price : Low[0] <= tp3Price))
                    {
                        tp3Hit = true;
                        exitReason = "TP3";
                        posDir = 0;
                        posJustClosed = true;
                    }
                }

                if (posDir != 0 && UseTrailing)
                {
                    double atrTrail = posDir == 1 ? Close[0] - TrailAtrMult * atrVal : Close[0] + TrailAtrMult * atrVal;
                    double bandTrail = double.IsNaN(stBand) ? atrTrail : stBand;

                    double newTrail = atrTrail;
                    if (TrailMode == AdaptiveTrendTrailMode.Band)
                        newTrail = bandTrail;
                    else if (TrailMode == AdaptiveTrendTrailMode.AtrToBand)
                        newTrail = posDir == 1 ? Math.Max(atrTrail, bandTrail) : Math.Min(atrTrail, bandTrail);

                    if (posDir == 1)
                        slPrice = Math.Max(double.IsNaN(slPrice) ? newTrail : slPrice, newTrail);
                    else
                        slPrice = Math.Min(double.IsNaN(slPrice) ? newTrail : slPrice, newTrail);
                }
            }

            // ── Open / reverse position on signal ──
            if (UseTPSL && (confirmedBuy || confirmedSell))
            {
                if (posDir != 0)
                {
                    ClearTpslDrawings();
                    posJustClosed = true;
                    exitReason = "REVERSE";
                }

                int newDir = confirmedBuy ? 1 : -1;
                double newEntry = Close[0];
                double newAtr = atrVal;
                double newSL = CalcSL(newDir, newEntry, newAtr, stBand);

                posDir = newDir;
                entryPrice = newEntry;
                slPrice = newSL;
                entrySL = newSL;
                tp1Hit = false; tp2Hit = false; tp3Hit = false;
                exitReason = "";
                entryBarIdx = CurrentBar;

                tpslLinesDrawn = false;
                lastSLDrawn = double.NaN;
                lastMoneyStopDrawn = double.NaN;
                lastAvgCount = -1;

                double entryGap = Math.Abs(newEntry - newSL);
                entryContracts = entryGap > 0 && usdPerPointEff > 0
                    ? Math.Max(ContractsPerEntry, (int)Math.Floor(RiskUsd / (entryGap * usdPerPointEff)))
                    : ContractsPerEntry;

                double entryRisk = entryGap * entryContracts * usdPerPointEff;
                moneyStopPrice = double.NaN;
                if (entryRisk > RiskUsd && usdPerPointEff > 0)
                {
                    double maxPts = RiskUsd / (entryContracts * usdPerPointEff);
                    moneyStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                        newDir == 1 ? newEntry - maxPts : newEntry + maxPts);

                    if (canDraw)
                    {
                        double warnY = newDir == 1 ? High[0] + atrVal * 2.0 : Low[0] - atrVal * 2.0;
                        Draw.Text(this, "AT_RiskWarn", "RISK $" + entryRisk.ToString("0") + " > $" + RiskUsd.ToString("0"),
                            0, warnY, Brushes.OrangeRed);
                    }
                }

                tp1Price = newDir == 1 ? newEntry + Tp1Mult * newAtr : newEntry - Tp1Mult * newAtr;
                tp2Price = TpLevels >= 2 ? (newDir == 1 ? newEntry + Tp2Mult * newAtr : newEntry - Tp2Mult * newAtr) : double.NaN;
                tp3Price = TpLevels >= 3 ? (newDir == 1 ? newEntry + Tp3Mult * newAtr : newEntry - Tp3Mult * newAtr) : double.NaN;

                if (ShowTPSLLines && canDraw)
                {
                    ClearTpslDrawings();
                    double vertTop = TpLevels >= 3 && !double.IsNaN(tp3Price) ? tp3Price
                        : TpLevels >= 2 && !double.IsNaN(tp2Price) ? tp2Price : tp1Price;
                    Draw.Line(this, "AT_Vert", false, 0, newSL, 0, vertTop, EntryLineBrush(), DashStyleHelper.Solid, 1);
                }

                if (ShowRR && canDraw)
                {
                    double risk = Math.Abs(entryPrice - entrySL);
                    double reward = Math.Abs(tp1Price - entryPrice);
                    double rr = risk > 0 ? reward / risk : 0.0;
                    string rrStr = "R:R 1:" + rr.ToString("0.#");
                    Brush rrBrush = rr >= 2.0 ? BullColor : rr >= 1.0 ? Brushes.Gold : BearColor;
                    double y = confirmedBuy ? Low[0] - atrVal * 1.6 : High[0] + atrVal * 1.6;
                    Draw.Text(this, "AT_RR" + CurrentBar, rrStr, 0, y, rrBrush);
                }
            }

            // ── TP/SL/entry lines (rays drawn once, redrawn only on change) + labels ──
            if (UseTPSL && posDir != 0 && ShowTPSLLines && entryBarIdx >= 0 && canDraw)
            {
                int startAgo = CurrentBar - Math.Max(entryBarIdx, firstDrawBar);
                SimpleFont font = tpslFont;

                if (!tpslLinesDrawn)
                {
                    Draw.Ray(this, "AT_Entry", false, startAgo, entryPrice, -1, entryPrice, EntryLineBrush(), DashStyleHelper.Dot, 1);
                    if (!tp1Hit)
                        Draw.Ray(this, "AT_TP1Ray", false, startAgo, tp1Price, -1, tp1Price, TpLineBrush(), DashStyleHelper.Solid, 1);
                    if (TpLevels >= 2 && !double.IsNaN(tp2Price) && !tp2Hit)
                        Draw.Ray(this, "AT_TP2Ray", false, startAgo, tp2Price, -1, tp2Price, TpLineBrush(), DashStyleHelper.Solid, 1);
                    if (TpLevels >= 3 && !double.IsNaN(tp3Price) && !tp3Hit)
                        Draw.Ray(this, "AT_TP3Ray", false, startAgo, tp3Price, -1, tp3Price, TpLineBrush(), DashStyleHelper.Solid, 1);
                    tpslLinesDrawn = true;
                }

                if (!slPrice.Equals(lastSLDrawn))
                {
                    Draw.Ray(this, "AT_SL", false, startAgo, slPrice, -1, slPrice, SlLineBrush(), DashStyleHelper.Solid, 1);
                    lastSLDrawn = slPrice;
                }

                Draw.Text(this, "AT_EntryLbl", false, "Entry " + Fmt(entryPrice), -3, entryPrice, 0, EntryLineBrush(), font, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.Text(this, "AT_SLLbl", false, "TR SL " + Fmt(slPrice), -3, slPrice, 0, SlLineBrush(), font, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                if (!tp1Hit)
                    Draw.Text(this, "AT_TP1Lbl", false, "TP1 " + Fmt(tp1Price), -3, tp1Price, 0, TpLineBrush(), font, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                if (TpLevels >= 2 && !double.IsNaN(tp2Price) && !tp2Hit)
                    Draw.Text(this, "AT_TP2Lbl", false, "TP2 " + Fmt(tp2Price), -3, tp2Price, 0, TpLineBrush(), font, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                if (TpLevels >= 3 && !double.IsNaN(tp3Price) && !tp3Hit)
                    Draw.Text(this, "AT_TP3Lbl", false, "TP3 " + Fmt(tp3Price), -3, tp3Price, 0, TpLineBrush(), font, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }

            // ── Money stop line (informative, removed once trailing SL passes it) ──
            if (UseTPSL && posDir != 0 && !double.IsNaN(moneyStopPrice))
            {
                bool superseded = posDir == 1 ? slPrice >= moneyStopPrice : slPrice <= moneyStopPrice;
                if (superseded)
                {
                    moneyStopPrice = double.NaN;
                    RemoveDrawObject("AT_MoneyStop");
                    RemoveDrawObject("AT_MoneyStopLbl");
                    RemoveDrawObject("AT_RiskWarn");
                }
                else if (canDraw && entryBarIdx >= 0)
                {
                    if (!moneyStopPrice.Equals(lastMoneyStopDrawn))
                    {
                        int msStartAgo = CurrentBar - Math.Max(entryBarIdx, firstDrawBar);
                        Draw.Ray(this, "AT_MoneyStop", false, msStartAgo, moneyStopPrice, -1, moneyStopPrice, Brushes.Gray, DashStyleHelper.Dash, 1);
                        lastMoneyStopDrawn = moneyStopPrice;
                    }
                    Draw.Text(this, "AT_MoneyStopLbl", false, "MAX RISK " + Fmt(moneyStopPrice), -3, moneyStopPrice, 0,
                        Brushes.Gray, tpslFont, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                }
            }

            // ── Risk-based averaging levels (dynamic count + merge) ──
            if (UseTPSL && ShowAvgEntries && posDir != 0 && canDraw && entryBarIdx >= 0)
            {
                bool riskFree = posDir == 1 ? slPrice >= entryPrice : slPrice <= entryPrice;
                double gapPoints = Math.Abs(entryPrice - slPrice);
                double initialRisk = gapPoints * entryContracts * usdPerPointEff;
                double remainingRisk = RiskUsd - initialRisk;

                if (riskFree || remainingRisk <= 0 || usdPerPointEff <= 0)
                {
                    RemoveAvgDrawings();
                }
                else
                {
                    const int maxAvgLevels = 10;
                    double unitPoints = remainingRisk / usdPerPointEff;
                    double sep = Math.Max(MinAvgSepPoints, TickSize);

                    int n = 1;
                    for (int k = maxAvgLevels; k >= 1; k--)
                    {
                        if (2.0 * unitPoints / (k * (k + 1.0)) >= sep)
                        {
                            n = k;
                            break;
                        }
                    }

                    double denom = n * (n + 1) / 2.0;
                    double[] lvl = new double[n];
                    int[] qty = new int[n];
                    for (int j = 0; j < n; j++)
                    {
                        double dj = unitPoints * (n - j) / denom;
                        double price = posDir == 1 ? slPrice + dj : slPrice - dj;
                        if (posDir == 1 && price > entryPrice) price = entryPrice;
                        if (posDir == -1 && price < entryPrice) price = entryPrice;
                        lvl[j] = Instrument.MasterInstrument.RoundToTickSize(price);
                        qty[j] = 1;
                    }

                    int m = 0;
                    for (int j = 1; j < n; j++)
                    {
                        if (Math.Abs(lvl[j] - lvl[m]) < Math.Max(sep * 0.5, TickSize))
                            qty[m] += qty[j];
                        else
                        {
                            m++;
                            lvl[m] = lvl[j];
                            qty[m] = qty[j];
                        }
                    }
                    int levelCount = m + 1;

                    int avgStartAgo = CurrentBar - Math.Max(entryBarIdx, firstDrawBar);
                    double entryOverlapDist = Math.Max(sep * 0.5, TickSize * 2);

                    int drawn = 0;
                    for (int j = 0; j < levelCount; j++)
                    {
                        if (Math.Abs(lvl[j] - entryPrice) < entryOverlapDist)
                            continue;

                        curAvgLvl[drawn] = lvl[j];
                        curAvgQty[drawn] = qty[j];
                        drawn++;
                    }

                    bool avgChanged = drawn != lastAvgCount;
                    if (!avgChanged)
                        for (int j = 0; j < drawn; j++)
                            if (!curAvgLvl[j].Equals(lastAvgLvl[j]) || curAvgQty[j] != lastAvgQty[j])
                            {
                                avgChanged = true;
                                break;
                            }

                    if (avgChanged)
                    {
                        for (int j = 0; j < drawn; j++)
                        {
                            Draw.Ray(this, "AT_Avg" + (j + 1), false, avgStartAgo, curAvgLvl[j], -1, curAvgLvl[j], AvgLineBrush(), DashStyleHelper.Dash, 1);
                            lastAvgLvl[j] = curAvgLvl[j];
                            lastAvgQty[j] = curAvgQty[j];
                        }
                        for (int j = drawn + 1; j <= maxAvgLevels; j++)
                        {
                            RemoveDrawObject("AT_Avg" + j);
                            RemoveDrawObject("AT_AvgLbl" + j);
                        }
                        lastAvgCount = drawn;
                    }

                    for (int j = 0; j < drawn; j++)
                        Draw.Text(this, "AT_AvgLbl" + (j + 1), false, "AVG" + (j + 1) + " " + Fmt(curAvgLvl[j]) + " (+" + curAvgQty[j] + ")", -3, curAvgLvl[j], 0,
                            AvgLineBrush(), tpslFont, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                }
            }

            // ── TP/SL hit labels ──
            if (UseTPSL && canDraw)
            {
                if (tp1JustHit)
                    Draw.Text(this, "AT_TP1Hit" + CurrentBar, false, "TP1 X", 1, tp1Price, 0, Brushes.White, tpslFont, System.Windows.TextAlignment.Center, Brushes.Transparent, BullColor, 100);
                if (tp2JustHit)
                    Draw.Text(this, "AT_TP2Hit" + CurrentBar, false, "TP2 X", 1, tp2Price, 0, Brushes.White, tpslFont, System.Windows.TextAlignment.Center, Brushes.Transparent, BullColor, 100);
                if (posJustClosed && exitReason == "TP3")
                    Draw.Text(this, "AT_TP3Hit" + CurrentBar, false, "TP3 X", 1, tp3Price, 0, Brushes.White, tpslFont, System.Windows.TextAlignment.Center, Brushes.Transparent, BullColor, 100);
                if (posJustClosed && exitReason == "SL")
                    Draw.Text(this, "AT_SLHit" + CurrentBar, false, "SL X", 1, slHitPrice, 0, Brushes.White, tpslFont, System.Windows.TextAlignment.Center, Brushes.Transparent, BearColor, 100);

                if (posDir == 0 && posJustClosed)
                    ClearTpslDrawings();
            }

            // ── Visual drawings ──
            Brush bandBrush = trendDir == 1 ? BullColor : BearColor;

            if (canDraw)
            {
                if (confirmedBuy || confirmedSell)
                    Draw.Dot(this, "AT_Switch" + CurrentBar, false, 0, stBand, bandBrush);
                if (ShowFiltered && filteredFlip)
                    Draw.Dot(this, "AT_Filt" + CurrentBar, false, 0, stBand, Brushes.Gray);

                if (ShowSignals && confirmedBuy)
                    Draw.Text(this, "AT_Long" + CurrentBar, false, "Long", 0, High[0] + atrVal * 0.8, 0, Brushes.White,
                        signalFont, System.Windows.TextAlignment.Center, Brushes.Transparent, BullColor, 100);
                if (ShowSignals && confirmedSell)
                    Draw.Text(this, "AT_Short" + CurrentBar, false, "Short", 0, Low[0] - atrVal * 0.8, 0, Brushes.White,
                        signalFont, System.Windows.TextAlignment.Center, Brushes.Transparent, BearColor, 100);

                if (ShowMLDebug && confirmedBuy)
                    Draw.Text(this, "AT_MLDbg" + CurrentBar, mlScore.ToString("0.0"), 0, High[0] + atrVal * 1.6, BullColor);
                if (ShowMLDebug && confirmedSell)
                    Draw.Text(this, "AT_MLDbg" + CurrentBar, mlScore.ToString("0.0"), 0, Low[0] - atrVal * 1.6, BearColor);

                if (ShowMLRejected && mlRejectedBuy)
                    Draw.TriangleUp(this, "AT_MLRej" + CurrentBar, false, 0, Low[0] - atrVal * 0.5, Brushes.Gold);
                if (ShowMLRejected && mlRejectedSell)
                    Draw.TriangleDown(this, "AT_MLRej" + CurrentBar, false, 0, High[0] + atrVal * 0.5, Brushes.Gold);

                if (ShowDivergence && bullDivergence)
                    Draw.Dot(this, "AT_BullDiv" + CurrentBar, false, 0, Low[0] - atrVal * 0.3, BullColor);
                if (ShowDivergence && bearDivergence)
                    Draw.Dot(this, "AT_BearDiv" + CurrentBar, false, 0, High[0] + atrVal * 0.3, BearColor);

                int fillStartBar = Math.Max(segmentStartBar, firstDrawBar);
                if (ShowFill && CurrentBar > fillStartBar)
                    Draw.Region(this, "AT_Fill" + segmentId, CurrentBar - fillStartBar, 0, bandSeries, Close,
                        Brushes.Transparent, bandBrush, 15);

                if (ShowStrengthBg)
                    BackBrush = trendDir == 1 ? bullBgBrush : bearBgBrush;

                if (ShowRegimeBadge && AutoTune)
                {
                    Brush regBrush = regime == "TRENDING" ? Brushes.DodgerBlue : regime == "RANGING" ? Brushes.Orange : Brushes.Magenta;
                    Draw.Text(this, "AT_Regime", regime, -3, stBand, regBrush);
                }
            }

            // ── Dashboard ──
            if (ShowDashboard && (State == State.Realtime || CurrentBar >= Count - 3))
                DrawDashboard(trendDir, strengthVal, strengthStr, mlScore, effectiveGate, htfTrend, mtfAligned, regime, regimeConfidence);

            // ── Alerts ──
            if (State == State.Realtime)
                FireAlerts(confirmedBuy, confirmedSell, tp1JustHit, tp2JustHit, posJustClosed, mlScore, htfTrend, regime);
        }

        private double CalcSL(int dir, double entry, double atr, double band)
        {
            if (SlMode == AdaptiveTrendSlMode.ATR)
                return dir == 1 ? entry - SlAtrMult * atr : entry + SlAtrMult * atr;
            if (SlMode == AdaptiveTrendSlMode.Band)
                return band;
            return dir == 1 ? entry * (1.0 - SlFixedPct / 100.0) : entry * (1.0 + SlFixedPct / 100.0);
        }

        private void ClearTpslDrawings()
        {
            RemoveDrawObject("AT_Entry"); RemoveDrawObject("AT_EntryLbl");
            RemoveDrawObject("AT_SL"); RemoveDrawObject("AT_SLLbl");
            RemoveDrawObject("AT_TP1"); RemoveDrawObject("AT_TP1Ray"); RemoveDrawObject("AT_TP1Line"); RemoveDrawObject("AT_TP1Lbl");
            RemoveDrawObject("AT_TP2"); RemoveDrawObject("AT_TP2Ray"); RemoveDrawObject("AT_TP2Line"); RemoveDrawObject("AT_TP2Lbl");
            RemoveDrawObject("AT_TP3"); RemoveDrawObject("AT_TP3Ray"); RemoveDrawObject("AT_TP3Lbl");
            RemoveDrawObject("AT_Vert");
            RemoveDrawObject("AT_MoneyStop");
            RemoveDrawObject("AT_MoneyStopLbl");
            RemoveDrawObject("AT_RiskWarn");
            RemoveAvgDrawings();
        }

        private void RemoveAvgDrawings()
        {
            for (int j = 1; j <= 10; j++)
            {
                RemoveDrawObject("AT_Avg" + j);
                RemoveDrawObject("AT_AvgLbl" + j);
            }
            lastAvgCount = -1;
        }

        private Brush EntryLineBrush() { return Brushes.DodgerBlue; }
        private Brush SlLineBrush() { return BearColor; }
        private Brush TpLineBrush() { return BullColor; }
        private Brush AvgLineBrush() { return Brushes.Orange; }

        private int GetSessionHour()
        {
            DateTime t = Time[0];
            try
            {
                TimeZoneInfo tz;
                switch (SessionTimezone)
                {
                    case AdaptiveTrendTimezone.NewYork: tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); break;
                    case AdaptiveTrendTimezone.London: tz = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); break;
                    case AdaptiveTrendTimezone.Tokyo: tz = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"); break;
                    default: tz = TimeZoneInfo.Utc; break;
                }
                DateTime local = DateTime.SpecifyKind(t, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTime(local, TimeZoneInfo.Local, tz).Hour;
            }
            catch
            {
                return t.Hour;
            }
        }

        private void DrawDashboard(int trend, double strengthVal, string strengthStr, double mlScore, double effectiveGate,
            int htfTrend, bool mtfAligned, string regime, double regimeConfidence)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("AdaptiveTrend");
            sb.AppendLine("Trend: " + (trend == 1 ? "Bullish" : "Bearish"));
            sb.AppendLine("Signal: " + (lastSignal != "-" ? lastSignal + " (" + barsSinceSignal + ")" : "-"));
            sb.AppendLine("Strength: " + strengthVal.ToString("0") + " " + strengthStr);
            sb.AppendLine("ADX: " + adxVal.ToString("0.0") + (adxVal >= 25 ? " Trend" : adxVal >= 15 ? " Weak" : " None"));
            if (UseMTF)
                sb.AppendLine("HTF: " + (htfTrend == 1 ? "Bull" : "Bear") + " (" + htfLabel + ")" + (mtfAligned ? "" : " !"));

            sb.AppendLine("--- ML Engine ---");
            sb.AppendLine("ML Score: " + mlScore.ToString("0.0") + "/100");
            sb.AppendLine("Gate: " + effectiveGate.ToString("0.0") + (SelfLearn ? " auto" : ""));
            double winRate = totalSignals > 0 ? (double)winSignals / totalSignals * 100.0 : 0.0;
            sb.AppendLine("Win Rate: " + (totalSignals >= 5 ? winRate.ToString("0") + "% (" + totalSignals + ")" : "..."));

            if (UseTPSL)
            {
                sb.AppendLine("--- Position ---");
                sb.AppendLine("Status: " + (posDir == 1 ? "LONG" : posDir == -1 ? "SHORT" : "FLAT"));
                if (posDir != 0)
                {
                    string trailTag = UseTrailing ? (TrailMode == AdaptiveTrendTrailMode.AtrToBand ? " (hyb)" : TrailMode == AdaptiveTrendTrailMode.Band ? " (band)" : " (atr)") : "";
                    sb.AppendLine("Entry: " + Fmt(entryPrice));
                    sb.AppendLine("SL: " + Fmt(slPrice) + trailTag);
                    string tpStr = tp1Hit ? "OK" : Fmt(tp1Price);
                    if (TpLevels >= 2) tpStr += " | " + (tp2Hit ? "OK" : Fmt(tp2Price));
                    if (TpLevels >= 3) tpStr += " | " + (tp3Hit ? "OK" : Fmt(tp3Price));
                    sb.AppendLine("TP: " + tpStr);
                    double risk = Math.Abs(entryPrice - entrySL);
                    double reward = Math.Abs(tp1Price - entryPrice);
                    double rr = risk > 0 ? reward / risk : 0.0;
                    sb.AppendLine("R:R 1:" + rr.ToString("0.#"));

                    double batchPerPoint = entryContracts * usdPerPointEff;
                    double curRiskUsd = Math.Abs(entryPrice - slPrice) * batchPerPoint;
                    bool riskFree = posDir == 1 ? slPrice >= entryPrice : slPrice <= entryPrice;
                    sb.AppendLine("Qty: " + entryContracts);
                    sb.AppendLine("Risk: " + (riskFree ? "FREE" : "$" + curRiskUsd.ToString("0") + " / $" + RiskUsd.ToString("0")));
                }
            }

            if (ShowProfile && AutoTune)
            {
                sb.AppendLine("--- Regime ---");
                sb.AppendLine("Mode: " + regime + " " + regimeConfidence.ToString("0") + "%");
            }

            sb.AppendLine("TF: " + FormatTfLabel(BarsPeriod.BarsPeriodType, BarsPeriod.Value));
            sb.Append("Ver: " + IndicatorVersion);

            TextPosition pos;
            switch (DashPosition)
            {
                case AdaptiveTrendDashPosition.TopLeft: pos = TextPosition.TopLeft; break;
                case AdaptiveTrendDashPosition.BottomLeft: pos = TextPosition.BottomLeft; break;
                case AdaptiveTrendDashPosition.BottomRight: pos = TextPosition.BottomRight; break;
                default: pos = TextPosition.TopRight; break;
            }

            bool dark = IsDarkTheme();
            Brush textBrush = dark ? Brushes.Gainsboro : Brushes.Black;
            Brush areaBrush = dark ? dashAreaDark : dashAreaLight;

            Draw.TextFixed(this, "AT_Dash", sb.ToString(), pos, textBrush,
                dashFont, Brushes.Transparent, areaBrush, 90);
        }

        private void FireAlerts(bool confirmedBuy, bool confirmedSell, bool tp1JustHit, bool tp2JustHit,
            bool posJustClosed, double mlScore, int htfTrend, string regime)
        {
            string ticker = Instrument.FullName;
            string tf = FormatTfLabel(BarsPeriod.BarsPeriodType, BarsPeriod.Value);
            string priceStr = Fmt(Close[0]);
            string bandStr = double.IsNaN(stBand) ? "0" : Fmt(stBand);
            string mlStr = mlScore.ToString("0.0");
            string htfStr = htfTrend == 1 ? "bull" : "bear";
            string slStr = double.IsNaN(slPrice) ? "0" : Fmt(slPrice);
            string tp1Str = double.IsNaN(tp1Price) ? "0" : Fmt(tp1Price);

            if (confirmedBuy)
            {
                string msg = WebhookJson
                    ? "{\"action\":\"long\",\"ticker\":\"" + ticker + "\",\"price\":" + priceStr + ",\"tf\":\"" + tf + "\",\"band\":" + bandStr + ",\"ml\":" + mlStr + ",\"adx\":" + adxVal.ToString("0.0") + ",\"htf\":\"" + htfStr + "\",\"sl\":" + slStr + ",\"tp1\":" + tp1Str + ",\"regime\":\"" + regime + "\"}"
                    : "LONG | " + ticker + " | " + tf + " | $" + priceStr + " | ML:" + mlStr + " | SL:" + slStr;
                Alert("AT_Long" + CurrentBar, Priority.High, msg, "", 10, Brushes.Black, BullColor);
            }
            if (confirmedSell)
            {
                string msg = WebhookJson
                    ? "{\"action\":\"short\",\"ticker\":\"" + ticker + "\",\"price\":" + priceStr + ",\"tf\":\"" + tf + "\",\"band\":" + bandStr + ",\"ml\":" + mlStr + ",\"adx\":" + adxVal.ToString("0.0") + ",\"htf\":\"" + htfStr + "\",\"sl\":" + slStr + ",\"tp1\":" + tp1Str + ",\"regime\":\"" + regime + "\"}"
                    : "SHORT | " + ticker + " | " + tf + " | $" + priceStr + " | ML:" + mlStr + " | SL:" + slStr;
                Alert("AT_Short" + CurrentBar, Priority.High, msg, "", 10, Brushes.Black, BearColor);
            }
            if (UseTPSL && tp1JustHit)
                Alert("AT_TP1" + CurrentBar, Priority.Medium, WebhookJson
                    ? "{\"action\":\"tp1\",\"ticker\":\"" + ticker + "\",\"price\":" + priceStr + "}"
                    : "TP1 HIT | " + ticker + " | $" + priceStr, "", 10, Brushes.Black, BullColor);
            if (UseTPSL && tp2JustHit)
                Alert("AT_TP2" + CurrentBar, Priority.Medium, WebhookJson
                    ? "{\"action\":\"tp2\",\"ticker\":\"" + ticker + "\",\"price\":" + priceStr + "}"
                    : "TP2 HIT | " + ticker + " | $" + priceStr, "", 10, Brushes.Black, BullColor);
            if (UseTPSL && posJustClosed && exitReason == "TP3")
                Alert("AT_TP3" + CurrentBar, Priority.Medium, WebhookJson
                    ? "{\"action\":\"tp3\",\"ticker\":\"" + ticker + "\",\"price\":" + priceStr + "}"
                    : "TP3 HIT | " + ticker + " | $" + priceStr, "", 10, Brushes.Black, BullColor);
            if (UseTPSL && posJustClosed && exitReason == "SL")
                Alert("AT_SL" + CurrentBar, Priority.Medium, WebhookJson
                    ? "{\"action\":\"sl\",\"ticker\":\"" + ticker + "\",\"price\":" + priceStr + "}"
                    : "SL HIT | " + ticker + " | $" + priceStr, "", 10, Brushes.Black, BearColor);
        }

        #region Properties
        // ── Main Settings ──
        [NinjaScriptProperty]
        [Display(Name = "Auto-Tune Parameters", Order = 1, GroupName = "01. Main Settings")]
        public bool AutoTune { get; set; }

        [Range(1, 200)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Length (manual)", Order = 2, GroupName = "01. Main Settings")]
        public int AtrLength { get; set; }

        [Range(0.5, 8.0)]
        [NinjaScriptProperty]
        [Display(Name = "Base Multiplier (manual)", Order = 3, GroupName = "01. Main Settings")]
        public double BaseMultiplier { get; set; }

        // ── Adaptive Engine ──
        [Range(50, 500)]
        [NinjaScriptProperty]
        [Display(Name = "Profiling Lookback", Order = 1, GroupName = "02. Adaptive Engine")]
        public int ProfileLookback { get; set; }

        [Range(0.5, 2.0)]
        [NinjaScriptProperty]
        [Display(Name = "Regime Sensitivity", Order = 2, GroupName = "02. Adaptive Engine")]
        public double RegimeSensitivity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Instrument Profile", Order = 3, GroupName = "02. Adaptive Engine")]
        public bool ShowProfile { get; set; }

        // ── Multi-Timeframe ──
        [NinjaScriptProperty]
        [Display(Name = "Multi-Timeframe Confluence", Order = 1, GroupName = "03. Multi-Timeframe")]
        public bool UseMTF { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HTF Selection", Order = 2, GroupName = "03. Multi-Timeframe")]
        public AdaptiveTrendMtfMode MtfMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Manual HTF Type", Order = 3, GroupName = "03. Multi-Timeframe")]
        public BarsPeriodType MtfManualType { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Manual HTF Value", Order = 4, GroupName = "03. Multi-Timeframe")]
        public int MtfManualValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MTF Strictness", Order = 5, GroupName = "03. Multi-Timeframe")]
        public AdaptiveTrendMtfStrictness MtfStrictness { get; set; }

        // ── ML Signal Filter ──
        [NinjaScriptProperty]
        [Display(Name = "Enable ML Signal Filter", Order = 1, GroupName = "04. ML Signal Filter")]
        public bool UseMLFilter { get; set; }

        [Range(10.0, 90.0)]
        [NinjaScriptProperty]
        [Display(Name = "Confidence Gate", Order = 2, GroupName = "04. ML Signal Filter")]
        public double MlGate { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Self-Learning Gate", Order = 3, GroupName = "04. ML Signal Filter")]
        public bool SelfLearn { get; set; }

        [Range(5, 50)]
        [NinjaScriptProperty]
        [Display(Name = "Evaluation Horizon (bars)", Order = 4, GroupName = "04. ML Signal Filter")]
        public int EvalHorizon { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show ML Score on Signals", Order = 5, GroupName = "04. ML Signal Filter")]
        public bool ShowMLDebug { get; set; }

        // ── ML Weights ──
        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W1: Momentum (RSI)", Order = 1, GroupName = "05. ML Weights")]
        public double W1Momentum { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W2: Volume Surge", Order = 2, GroupName = "05. ML Weights")]
        public double W2Volume { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W3: Trend (ER)", Order = 3, GroupName = "05. ML Weights")]
        public double W3Trend { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W4: Vol Shock", Order = 4, GroupName = "05. ML Weights")]
        public double W4Volatility { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W5: Band Distance", Order = 5, GroupName = "05. ML Weights")]
        public double W5Distance { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W6: MACD", Order = 6, GroupName = "05. ML Weights")]
        public double W6Macd { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W7: Price Structure", Order = 7, GroupName = "05. ML Weights")]
        public double W7Structure { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W8: Regime", Order = 8, GroupName = "05. ML Weights")]
        public double W8Regime { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W9: MTF Confluence", Order = 9, GroupName = "05. ML Weights")]
        public double W9Mtf { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W10: ADX Strength", Order = 10, GroupName = "05. ML Weights")]
        public double W10Adx { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W11: RSI Divergence", Order = 11, GroupName = "05. ML Weights")]
        public double W11Divergence { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W12: Vol Profile Zone", Order = 12, GroupName = "05. ML Weights")]
        public double W12VolProfile { get; set; }

        [Range(-1.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "W13: Session Quality", Order = 13, GroupName = "05. ML Weights")]
        public double W13Session { get; set; }

        [Range(-50.0, 50.0)]
        [NinjaScriptProperty]
        [Display(Name = "Bias", Order = 14, GroupName = "05. ML Weights")]
        public double WBias { get; set; }

        // ── Filters ──
        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Flip Cushion xATR", Order = 1, GroupName = "06. Filters")]
        public double Cushion { get; set; }

        [Range(0, 20)]
        [NinjaScriptProperty]
        [Display(Name = "Signal Cooldown", Order = 2, GroupName = "06. Filters")]
        public int Cooldown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Momentum Filter (RSI)", Order = 3, GroupName = "06. Filters")]
        public bool UseMomentum { get; set; }

        [Range(2, 50)]
        [NinjaScriptProperty]
        [Display(Name = "RSI Length", Order = 4, GroupName = "06. Filters")]
        public int RsiLength { get; set; }

        [Range(30.0, 60.0)]
        [NinjaScriptProperty]
        [Display(Name = "RSI Threshold", Order = 5, GroupName = "06. Filters")]
        public double RsiThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Filter", Order = 6, GroupName = "06. Filters")]
        public bool UseVolumeFilter { get; set; }

        [Range(0.5, 3.0)]
        [NinjaScriptProperty]
        [Display(Name = "Volume Multiplier", Order = 7, GroupName = "06. Filters")]
        public double VolMultiplier { get; set; }

        // ── Session Filter ──
        [NinjaScriptProperty]
        [Display(Name = "Enable Session Filter", Order = 1, GroupName = "07. Session Filter")]
        public bool UseSessionFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Timezone", Order = 2, GroupName = "07. Session Filter")]
        public AdaptiveTrendTimezone SessionTimezone { get; set; }

        [Range(0, 23)]
        [NinjaScriptProperty]
        [Display(Name = "Kill Zone Start (hour)", Order = 3, GroupName = "07. Session Filter")]
        public int SessionKillStart { get; set; }

        [Range(0, 23)]
        [NinjaScriptProperty]
        [Display(Name = "Kill Zone End (hour)", Order = 4, GroupName = "07. Session Filter")]
        public int SessionKillEnd { get; set; }

        // ── TP / SL ──
        [NinjaScriptProperty]
        [Display(Name = "Enable TP/SL System", Order = 1, GroupName = "08. TP / SL")]
        public bool UseTPSL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Order = 2, GroupName = "08. TP / SL")]
        public AdaptiveTrendSlMode SlMode { get; set; }

        [Range(0.5, 5.0)]
        [NinjaScriptProperty]
        [Display(Name = "SL ATR Multiplier", Order = 3, GroupName = "08. TP / SL")]
        public double SlAtrMult { get; set; }

        [Range(0.1, 10.0)]
        [NinjaScriptProperty]
        [Display(Name = "SL Fixed %", Order = 4, GroupName = "08. TP / SL")]
        public double SlFixedPct { get; set; }

        [Range(1, 3)]
        [NinjaScriptProperty]
        [Display(Name = "Take Profit Levels", Order = 5, GroupName = "08. TP / SL")]
        public int TpLevels { get; set; }

        [Range(0.5, 10.0)]
        [NinjaScriptProperty]
        [Display(Name = "TP1 ATR Multiplier", Order = 6, GroupName = "08. TP / SL")]
        public double Tp1Mult { get; set; }

        [Range(1.0, 15.0)]
        [NinjaScriptProperty]
        [Display(Name = "TP2 ATR Multiplier", Order = 7, GroupName = "08. TP / SL")]
        public double Tp2Mult { get; set; }

        [Range(1.5, 20.0)]
        [NinjaScriptProperty]
        [Display(Name = "TP3 ATR Multiplier", Order = 8, GroupName = "08. TP / SL")]
        public double Tp3Mult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trailing Stop", Order = 9, GroupName = "08. TP / SL")]
        public bool UseTrailing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trail Mode", Order = 10, GroupName = "08. TP / SL")]
        public AdaptiveTrendTrailMode TrailMode { get; set; }

        [Range(0.3, 5.0)]
        [NinjaScriptProperty]
        [Display(Name = "Trail ATR Multiplier", Order = 11, GroupName = "08. TP / SL")]
        public double TrailAtrMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show TP/SL Lines", Order = 12, GroupName = "08. TP / SL")]
        public bool ShowTPSLLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TP/SL Label Size", Order = 13, GroupName = "08. TP / SL")]
        public AdaptiveTrendLabelSize TpslLabelSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show R:R on Entry", Order = 14, GroupName = "08. TP / SL")]
        public bool ShowRR { get; set; }

        // ── Risk Management ──
        [NinjaScriptProperty]
        [Display(Name = "Show Averaging Entries", Order = 1, GroupName = "08b. Risk Management")]
        public bool ShowAvgEntries { get; set; }

        [Range(0.0, 10000.0)]
        [NinjaScriptProperty]
        [Display(Name = "USD Per Point (0 = auto-detect)", Order = 2, GroupName = "08b. Risk Management")]
        public double UsdPerPoint { get; set; }

        [Range(1.0, 1000000.0)]
        [NinjaScriptProperty]
        [Display(Name = "Max Risk USD", Order = 3, GroupName = "08b. Risk Management")]
        public double RiskUsd { get; set; }

        [Range(1, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Min Contracts Per Entry", Order = 4, GroupName = "08b. Risk Management")]
        public int ContractsPerEntry { get; set; }

        [Range(0.25, 10000.0)]
        [NinjaScriptProperty]
        [Display(Name = "Min AVG Separation (points)", Order = 5, GroupName = "08b. Risk Management")]
        public double MinAvgSepPoints { get; set; }

        // ── Visual ──
        [Range(0, 3650)]
        [NinjaScriptProperty]
        [Display(Name = "Days To Plot (0 = all)", Order = 0, GroupName = "09. Visual")]
        public int PlotDays { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Theme", Order = 1, GroupName = "09. Visual")]
        public AdaptiveTrendTheme Theme { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Long/Short Signals", Order = 2, GroupName = "09. Visual")]
        public bool ShowSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Filtered Flips", Order = 3, GroupName = "09. Visual")]
        public bool ShowFiltered { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show ML-Rejected Signals", Order = 4, GroupName = "09. Visual")]
        public bool ShowMLRejected { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show SuperTrend Band", Order = 5, GroupName = "09. Visual")]
        public bool ShowBand { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Gradient Fill", Order = 6, GroupName = "09. Visual")]
        public bool ShowFill { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trend Background", Order = 7, GroupName = "09. Visual")]
        public bool ShowStrengthBg { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Regime Badge", Order = 8, GroupName = "09. Visual")]
        public bool ShowRegimeBadge { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Divergence Dots", Order = 9, GroupName = "09. Visual")]
        public bool ShowDivergence { get; set; }

        // ── Dashboard ──
        [NinjaScriptProperty]
        [Display(Name = "Show Dashboard", Order = 1, GroupName = "10. Dashboard")]
        public bool ShowDashboard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dashboard Position", Order = 2, GroupName = "10. Dashboard")]
        public AdaptiveTrendDashPosition DashPosition { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dashboard Font Size", Order = 3, GroupName = "10. Dashboard")]
        public AdaptiveTrendFontSize DashFontSize { get; set; }

        // ── Alerts ──
        [NinjaScriptProperty]
        [Display(Name = "Webhook JSON Format", Order = 1, GroupName = "11. Alerts")]
        public bool WebhookJson { get; set; }

        // ── Colors ──
        [XmlIgnore]
        [Display(Name = "Bull", Order = 1, GroupName = "12. Colors")]
        public Brush BullColor { get; set; }

        [Browsable(false)]
        public string BullColorSerializable
        {
            get { return Serialize.BrushToString(BullColor); }
            set { BullColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bear", Order = 2, GroupName = "12. Colors")]
        public Brush BearColor { get; set; }

        [Browsable(false)]
        public string BearColorSerializable
        {
            get { return Serialize.BrushToString(BearColor); }
            set { BearColor = Serialize.StringToBrush(value); }
        }

        // ── Advanced ──
        [Range(0.3, 5.0)]
        [NinjaScriptProperty]
        [Display(Name = "Min Adaptive Mult", Order = 1, GroupName = "13. Advanced")]
        public double MinAdaptiveMult { get; set; }

        [Range(2.0, 10.0)]
        [NinjaScriptProperty]
        [Display(Name = "Max Adaptive Mult", Order = 2, GroupName = "13. Advanced")]
        public double MaxAdaptiveMult { get; set; }

        // ── Public series ──
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BandUp
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BandDown
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> TrendDirection
        {
            get { return trendSeries; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> MLScore
        {
            get { return mlScoreSeries; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SuperTrendBand
        {
            get { return bandSeries; }
        }
        #endregion
    }
}
