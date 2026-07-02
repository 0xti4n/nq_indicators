#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public enum CompositeEmaPriceSource
    {
        Close,
        Open,
        High,
        Low
    }

    public class CompositeMtfEmaDominance : Indicator
    {
        private const int TimeframeCount = 5;

        private EMA[] ema50;
        private EMA[] ema233;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CompositeMtfEmaDominance";
                Description = "Composite MTF EMA 50 + 233.";
                IsOverlay = true;
                Calculate = Calculate.OnPriceChange;
                IsSuspendedWhileInactive = true;

                EmaSource = CompositeEmaPriceSource.Close;
                UseConfirmedBars = true;
                ShowCompositeEma233 = true;
                ShowCompositeEma50 = true;

                UseTimeframe1 = true;
                UseTimeframe2 = true;
                UseTimeframe3 = true;
                UseTimeframe4 = true;
                UseTimeframe5 = true;

                Timeframe1Type = BarsPeriodType.Minute;
                Timeframe1Value = 240;
                Timeframe2Type = BarsPeriodType.Minute;
                Timeframe2Value = 60;
                Timeframe3Type = BarsPeriodType.Minute;
                Timeframe3Value = 15;
                Timeframe4Type = BarsPeriodType.Minute;
                Timeframe4Value = 5;
                Timeframe5Type = BarsPeriodType.Minute;
                Timeframe5Value = 3;

                AddPlot(Brushes.Red, "CompositeEMA233");
                AddPlot(Brushes.Blue, "CompositeEMA50");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(Timeframe1Type, Timeframe1Value);
                AddDataSeries(Timeframe2Type, Timeframe2Value);
                AddDataSeries(Timeframe3Type, Timeframe3Value);
                AddDataSeries(Timeframe4Type, Timeframe4Value);
                AddDataSeries(Timeframe5Type, Timeframe5Value);
            }
            else if (State == State.DataLoaded)
            {
                ema50 = new EMA[TimeframeCount];
                ema233 = new EMA[TimeframeCount];

                for (int slot = 0; slot < TimeframeCount; slot++)
                {
                    int seriesIndex = slot + 1;
                    ema50[slot] = EMA(GetPriceSeries(seriesIndex), 50);
                    ema233[slot] = EMA(GetPriceSeries(seriesIndex), 233);
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            int barsAgo = UseConfirmedBars ? 1 : 0;

            double composite233 = GetComposite233(barsAgo);
            double composite50 = GetComposite50(barsAgo);

            Values[0][0] = ShowCompositeEma233 && !double.IsNaN(composite233) ? composite233 : double.NaN;
            Values[1][0] = ShowCompositeEma50 && !double.IsNaN(composite50) ? composite50 : double.NaN;
        }

        private ISeries<double> GetPriceSeries(int seriesIndex)
        {
            switch (EmaSource)
            {
                case CompositeEmaPriceSource.Open:
                    return Opens[seriesIndex];

                case CompositeEmaPriceSource.High:
                    return Highs[seriesIndex];

                case CompositeEmaPriceSource.Low:
                    return Lows[seriesIndex];

                default:
                    return Closes[seriesIndex];
            }
        }

        private double GetComposite50(int barsAgo)
        {
            double sum = 0;
            int count = 0;

            for (int slot = 0; slot < TimeframeCount; slot++)
                AddComponent(ema50[slot], IsTimeframeEnabled(slot), slot + 1, barsAgo, 50, ref sum, ref count);

            return count > 0 ? sum / count : double.NaN;
        }

        private double GetComposite233(int barsAgo)
        {
            double sum = 0;
            int count = 0;

            for (int slot = 0; slot < TimeframeCount; slot++)
                AddComponent(ema233[slot], IsTimeframeEnabled(slot), slot + 1, barsAgo, 233, ref sum, ref count);

            return count > 0 ? sum / count : double.NaN;
        }

        private bool IsTimeframeEnabled(int slot)
        {
            switch (slot)
            {
                case 0:
                    return UseTimeframe1;

                case 1:
                    return UseTimeframe2;

                case 2:
                    return UseTimeframe3;

                case 3:
                    return UseTimeframe4;

                case 4:
                    return UseTimeframe5;

                default:
                    return false;
            }
        }

        private void AddComponent(EMA ema, bool enabled, int seriesIndex, int barsAgo, int length, ref double sum, ref int count)
        {
            if (!enabled || ema == null)
                return;

            if (CurrentBars[seriesIndex] < length + barsAgo)
                return;

            double value = ema[barsAgo];

            if (double.IsNaN(value) || double.IsInfinity(value))
                return;

            sum += value;
            count++;
        }

        [NinjaScriptProperty]
        [Display(Name = "EMA Source", Order = 1, GroupName = "Parameters")]
        public CompositeEmaPriceSource EmaSource { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Confirmed Bars", Order = 2, GroupName = "Parameters")]
        public bool UseConfirmedBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Composite EMA 233", Order = 3, GroupName = "Parameters")]
        public bool ShowCompositeEma233 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Composite EMA 50", Order = 4, GroupName = "Parameters")]
        public bool ShowCompositeEma50 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Timeframe 1", Order = 1, GroupName = "Timeframe 1")]
        public bool UseTimeframe1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Type", Order = 2, GroupName = "Timeframe 1")]
        public BarsPeriodType Timeframe1Type { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Value", Order = 3, GroupName = "Timeframe 1")]
        public int Timeframe1Value { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Timeframe 2", Order = 1, GroupName = "Timeframe 2")]
        public bool UseTimeframe2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Type", Order = 2, GroupName = "Timeframe 2")]
        public BarsPeriodType Timeframe2Type { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Value", Order = 3, GroupName = "Timeframe 2")]
        public int Timeframe2Value { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Timeframe 3", Order = 1, GroupName = "Timeframe 3")]
        public bool UseTimeframe3 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Type", Order = 2, GroupName = "Timeframe 3")]
        public BarsPeriodType Timeframe3Type { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Value", Order = 3, GroupName = "Timeframe 3")]
        public int Timeframe3Value { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Timeframe 4", Order = 1, GroupName = "Timeframe 4")]
        public bool UseTimeframe4 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Type", Order = 2, GroupName = "Timeframe 4")]
        public BarsPeriodType Timeframe4Type { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Value", Order = 3, GroupName = "Timeframe 4")]
        public int Timeframe4Value { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Timeframe 5", Order = 1, GroupName = "Timeframe 5")]
        public bool UseTimeframe5 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Type", Order = 2, GroupName = "Timeframe 5")]
        public BarsPeriodType Timeframe5Type { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Value", Order = 3, GroupName = "Timeframe 5")]
        public int Timeframe5Value { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CompositeEMA233
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CompositeEMA50
        {
            get { return Values[1]; }
        }
    }
}
