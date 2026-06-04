#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class SuperTrend : Indicator
	{
		private ATR atr;
		private Series<double> finalUpper;
		private Series<double> finalLower;
		private Series<double> trendDirection;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"SuperTrend indicator based on ATR.";
				Name						= "SuperTrend";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				DrawHorizontalGridLines		= true;
				DrawVerticalGridLines		= true;
				PaintPriceMarkers			= true;
				ScaleJustification			= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive	= true;

				AtrPeriod					= 84;
				Multiplier					= 4.0;

				AddPlot(Brushes.Blue, "UpTrend");
				AddPlot(Brushes.Red, "DownTrend");
			}
			else if (State == State.DataLoaded)
			{
				atr = ATR(AtrPeriod);
				finalUpper = new Series<double>(this);
				finalLower = new Series<double>(this);
				trendDirection = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			double hl2 = (High[0] + Low[0]) / 2.0;
			double basicUpper = hl2 + Multiplier * atr[0];
			double basicLower = hl2 - Multiplier * atr[0];

			if (CurrentBar == 0)
			{
				finalUpper[0] = basicUpper;
				finalLower[0] = basicLower;
				trendDirection[0] = 1;

				UpTrend[0] = finalLower[0];
				DownTrend.Reset();
				return;
			}

			finalUpper[0] = basicUpper < finalUpper[1] || Close[1] > finalUpper[1]
				? basicUpper
				: finalUpper[1];

			finalLower[0] = basicLower > finalLower[1] || Close[1] < finalLower[1]
				? basicLower
				: finalLower[1];

			if (trendDirection[1] < 0)
				trendDirection[0] = Close[0] > finalUpper[0] ? 1 : -1;
			else
				trendDirection[0] = Close[0] < finalLower[0] ? -1 : 1;

			if (trendDirection[0] > 0)
			{
				UpTrend[0] = finalLower[0];
				DownTrend.Reset();
			}
			else
			{
				DownTrend[0] = finalUpper[0];
				UpTrend.Reset();
			}
		}

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR Period", GroupName = "Parameters", Order = 0)]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, double.MaxValue)]
		[Display(Name = "Multiplier", GroupName = "Parameters", Order = 1)]
		public double Multiplier { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpTrend
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DownTrend
		{
			get { return Values[1]; }
		}
	}
}
