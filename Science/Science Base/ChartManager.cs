﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiveCharts.Geared;
using LiveCharts.WinForms;
using Scarlet.Utilities;

namespace Science_Base
{
    public class ChartInstance
    {
        public CartesianChart Chart;
        public ListBox DataChooser;

        public ChartInstance(CartesianChart Chart, ListBox Chooser)
        {
            this.Chart = Chart;
            this.DataChooser = Chooser;

            this.Chart.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
            LiveCharts.Wpf.Axis X = new LiveCharts.Wpf.Axis()
            {
                LabelFormatter = value => new DateTime((long)value).ToString("T")
            };
            this.Chart.AxisX.Add(X);
            this.Chart.AxisY.Clear();
            this.Chart.DisableAnimations = true;
            this.Chart.AllowDrop = true;
        }

        public void AddSeries<T>(DataSeries<T> Series)
        {
            Log.Output(Log.Severity.INFO, Log.Source.GUI, "Adding series, current series count:" + this.Chart.Series.Count);
            LiveCharts.Wpf.Axis Y = new LiveCharts.Wpf.Axis()
            {
                Title = Series.AxisLabel
            };
            this.Chart.AxisY.Add(Y);

            GLineSeries ChartSeries = new GLineSeries(Series.GetMapper())
            {
                Values = Series.Data,
                Stroke = MainWindow.ScarletColour,
                Fill = MainWindow.ScarletBackColour,
                PointGeometry = null
            };
            this.Chart.Series.Add(ChartSeries);
        }

        // Thanks Sasha!
        public void AddByIndex(int Index)
        {
            object Series = DataHandler.GetSeries()[Index];
            Type type = Series.GetType();
            Type Generic = type.GetGenericArguments()[0];
            MethodInfo Info = this.GetType().GetMethod("AddSeries");
            Info = Info.MakeGenericMethod(Generic);
            Info.Invoke(this, new object[] { Series });
        }

        public void Clear()
        {
            this.Chart.Series.Clear();
            this.Chart.AxisY.Clear();
        }
    }

    public class ChartManager
    {
        public ChartInstance Left, Right;

        public ChartManager(CartesianChart ChartLeft, CartesianChart ChartRight, ListBox Chooser)
        {
            this.Left = new ChartInstance(ChartLeft, Chooser);
            this.Right = new ChartInstance(ChartRight, Chooser);
        }
    }
}