using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LiveCharts.Configurations;
using GSF.TimeSeries;
using LiveCharts;


namespace CustomCSVAdapter
{
    public partial class PlotOpenPDCData : UserControl, INotifyPropertyChanged
    {

        private double _axisMax;
        private double _axisMin;
        private double _trend;
        public PlotOpenPDCData()
        {
            InitializeComponent();

            var Mapper = Mappers.Xy<Measurement>()
                .X(value => value.Timestamp)
                .Y(value => value.AdjustedValue);

            //Save the mapper globally
            Charting.For<Measurement>(Mapper);


            //the values property will store our values array
            ChartValues = new ChartValues<Measurement>();

            //lets set how to display the X Labels
            AxisStep = TimeSpan.FromSeconds(1).Ticks;

            IsReading = false;

            DataContext = this;

        }


        public double AxisStep { get; set; }
        public double AxisUnit { get; set; }

        public double AxisMax
        {
            get { return _axisMax; }
            set
            {
                _axisMax = value;
                OnPropertyChanged("AxisMax");
            }
        }
        public double AxisMin
        {
            get { return _axisMin; }
            set
            {
                _axisMin = value;
                OnPropertyChanged("AxisMin");
            }
        }

        public bool IsReading { get; set; }

        private void Read()
        {
            var r = new Random();

            while (IsReading)
            {
                Thread.Sleep(150);
                var now = DateTime.Now;

                _trend += r.Next(-8, 10);

                ChartValues.Add(new Measurement
                {
                    Key = { },
                    Value = _trend
                });

                SetAxisLimits(now);

                //lets only use the last 150 values
                if (ChartValues.Count > 150) ChartValues.RemoveAt(0);
            }
        }

        private void SetAxisLimits(DateTime now)
        {
            AxisMax = now.Ticks + TimeSpan.FromSeconds(1).Ticks; // lets force the axis to be 1 second ahead
            AxisMin = now.Ticks - TimeSpan.FromSeconds(8).Ticks; // and 8 seconds behind
        }

        private void InjectStopOnClick(object sender, RoutedEventArgs e)
        {
            IsReading = !IsReading;
            if (IsReading) Task.Factory.StartNew(Read);
        }

        #region INotifyPropertyChanged implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        public ChartValues<Measurement> ChartValues { get; set; }

        private void PlotOpenPDCData_Load(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {

        }
    }
}