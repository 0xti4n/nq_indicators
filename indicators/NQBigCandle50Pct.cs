#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NQBigCandle50Pct : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NQBigCandle50Pct";
                Description = "Draws a 50% midpoint line when the active candle is greater than the configured point range.";
                IsOverlay = true;
                Calculate = Calculate.OnPriceChange;
                IsSuspendedWhileInactive = true;

                MinCandlePoints = 50;
                ExtendBarsRight = 10;
                KeepHistoricalLines = true;
                LineWidth = 2;
                MidlineBrush = Brushes.DarkMagenta;

                AddPlot(MidlineBrush, "BigCandleMidpoint");
            }
        }

        protected override void OnBarUpdate()
		{
		    if (State != State.Realtime)
		        return;
		
		    double candleRangePoints = High[0] - Low[0];
		
		    if (candleRangePoints <= MinCandlePoints)
		        return;
		
		    double midpoint = (High[0] + Low[0]) / 2.0;
		
		    Draw.Ray(
		        this,
		        "LastBigCandleMidpoint",
		        false,
		        0,
		        midpoint,
		        -1,
		        midpoint,
		        MidlineBrush,
		        DashStyleHelper.Dash,
		        LineWidth
		    );
		}

        [Range(0.01, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Min Candle Points", Order = 1, GroupName = "Parameters")]
        public double MinCandlePoints { get; set; }

        [Range(1, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Extend Bars Right", Order = 2, GroupName = "Parameters")]
        public int ExtendBarsRight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Keep Historical Lines", Order = 3, GroupName = "Parameters")]
        public bool KeepHistoricalLines { get; set; }

        [Range(1, 10)]
        [NinjaScriptProperty]
        [Display(Name = "Line Width", Order = 4, GroupName = "Parameters")]
        public int LineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Line Color", Order = 5, GroupName = "Parameters")]
        public Brush MidlineBrush { get; set; }

        [Browsable(false)]
        public string MidlineBrushSerializable
        {
            get { return Serialize.BrushToString(MidlineBrush); }
            set { MidlineBrush = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BigCandleMidpoint
        {
            get { return Values[0]; }
        }
    }
}
