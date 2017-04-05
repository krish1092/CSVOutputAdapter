using GSF;
using GSF.TimeSeries;
using GSF.TimeSeries.Adapters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace CustomCSVAdapter
{
    [Description("CustomCSV: Krishnan")]
    public class CustomCSVOutputAdapter : OutputAdapterBase
    {

        #region [ Members ]

        //Fields
        private string m_fileName;
        private StreamWriter m_outStream;
        private int m_measurementCount;
        private bool header_written = false;

        private double ThresholdTriggerValue;
        private double ChangingParameter1, ChangingParameter2;



        private Dictionary<Guid, Queue<double>> measurement_dictionary;
        private Dictionary<Guid, Queue<double>> measurement_log_voltage_diff_dictionary;
        private double percentage_trigger = 1.0;
        private Dictionary<Guid, double> measurement_average_dictionary;
        private Dictionary<Guid, double> measurement_initial_average_dictionary;
        private Dictionary<Guid, double> measurement_LE_initial_dictionary;
        private Dictionary<Guid, double> measurement_LE_dictionary;
        private Dictionary<Guid, double> LE_iteration_dictionary;
        private Dictionary<Guid, bool> measurement_average_flag_dictionary;
        private Dictionary<Guid, bool> measurement_LE_start_flag_dictionary;
        private Dictionary<Guid, bool> measurement_LE_window_over_flag_dictionary;
        private int window_length = 25;
        private double delta_time = 1 / 60;

        #endregion


        #region [ Constructors ]

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomCSVOutputAdapter"/> class.
        /// </summary>
        public CustomCSVOutputAdapter()
        {
            m_measurementCount = 0;
            measurement_dictionary = new Dictionary<Guid, Queue<double>>(2 * window_length);
            measurement_log_voltage_diff_dictionary = new Dictionary<Guid, Queue<double>>(2 * window_length);
            measurement_average_dictionary = new Dictionary<Guid, double>(2 * window_length);
            measurement_initial_average_dictionary = new Dictionary<Guid, double>(2 * window_length);
            measurement_LE_initial_dictionary = new Dictionary<Guid, double>(2 * window_length);
            measurement_LE_dictionary = new Dictionary<Guid, double>(2 * window_length);
            LE_iteration_dictionary = new Dictionary<Guid, double>(2 * window_length);
            measurement_average_flag_dictionary = new Dictionary<Guid, bool>();
            measurement_LE_start_flag_dictionary = new Dictionary<Guid, bool>();
            measurement_LE_window_over_flag_dictionary = new Dictionary<Guid, bool>();

        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the name of the CSV file.
        /// </summary>
        [ConnectionStringParameter,
       Description("Define the name of the CSV file to which measurements will be archived."),
       DefaultValue("measurements_test.csv"),
       CustomConfigurationEditor("GSF.TimeSeries.UI.WPF.dll", "GSF.TimeSeries.UI.Editors.FileDialogEditor", "type=save; defaultExt=.csv; filter=CSV files|*.csv|All files|*.*")]

        public string FileName
        {
            get
            {
                return m_fileName;
            }
            set
            {
                m_fileName = value;
            }
        }

        /// <summary>
        /// Returns a flag that determines if measurements sent to this
        /// <see cref="CustomCSVOutputAdapter"/> are destined for archival.
        /// </summary>

        public override bool OutputIsForArchive
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Gets a flag that determines if this <see cref="CustomCSVOutputAdapter"/>
        /// uses an asynchronous connection.
        /// </summary>

        protected override bool UseAsyncConnect
        {
            get
            {
                return false;
            }
        }


        /// <summary>
        /// Gets a short one-line status of this <see cref="CsvOutputAdapter"/>.
        /// </summary>
        /// <param name="maxLength">Maximum length of the status message.</param>
        /// <returns>Text of the status message.</returns>
        public override string GetShortStatus(int maxLength)
        {
            return string.Format("Archived {0} measurements to File : {1} in the custom format", m_measurementCount, m_fileName).CenterText(maxLength);

        }
        #endregion

        #region [ Methods ]


        /// <summary>
        /// Initializes this <see cref="CustomCSVOutputAdapter"/>.
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();

            Dictionary<string, string> settings = Settings;
            string setting, PythonExecutableLocation, PythonFileName;
            string Temporary;

            // Load optional parameters

            if (settings.TryGetValue("FileName", out setting))
                m_fileName = setting;

            if (settings.TryGetValue("PythonExecutableLocation", out PythonExecutableLocation))
                PythonExecutable = @PythonExecutableLocation;

            if (settings.TryGetValue("PythonFileLocation", out PythonFileName))
                PythonFileToCall = PythonFileName;

            if (settings.TryGetValue("TriggerValue", out Temporary))
                //If the TriggerValue is not given b the user, it is null and will throw an exception when converitng to double.
                ThresholdTriggerValue = (Temporary.Equals("") || Temporary == null) ? 0 : Convert.ToDouble(Temporary);

            if (settings.TryGetValue("ChangingParameter1Value", out Temporary))
                //If the TriggerValue is not given b the user, it is null and will throw an exception when converitng to double.
                ChangingParameter1 = (Temporary.Equals("") || Temporary == null) ? 0 : Convert.ToDouble(Temporary);

            if (settings.TryGetValue("ChangingParameter2Value", out Temporary))
                //If the TriggerValue is not given b the user, it is null and will throw an exception when converitng to double.
                ChangingParameter2 = (Temporary.Equals("") || Temporary == null) ? 0 : Convert.ToDouble(Temporary);

        }

        /// <summary>
        /// Attempts to connect to this <see cref="CustomCSVOutputAdapter"/>.
        /// </summary>

        protected override void AttemptConnection()
        {
            m_outStream = new StreamWriter(m_fileName);
        }

        // <summary>
        /// Attempts to disconnect from this <see cref="CustomCSVOutputAdapter"/>.
        /// </summary>
        protected override void AttemptDisconnection()
        {
            m_outStream.Close();
        }

        // First Measurement Key
        private Guid FirstMeasurementKey;

        protected override void ProcessMeasurements(IMeasurement[] measurements)
        {
            if ((object)measurements != null)
            {

                StringBuilder builder = new StringBuilder();
                StringBuilder manipulated_builder = new StringBuilder();
                if (!header_written)
                    builder.Append(WriteHeader(measurements));

                FirstMeasurementKey = measurements[0].ID; // Get the first measurement key

                int number_of_measurement_keys = measurements.MeasurementKeys().Length;
                for (int i = 0; i < measurements.Length; i = i + number_of_measurement_keys)
                {
                    builder.Append((long)measurements[i].Timestamp);
                    for (int j = 0; j < measurements.Select(m => m.Key).Distinct().ToArray().Length; j++)
                    {
                        builder.Append(',').Append(measurements[i + j].AdjustedValue);
                        manipulated_builder.Append(CalculateMovingAverageInMeasurement(measurements[i + j], window_length));
                        manipulated_builder.Append(CalculateLEInMeasurement(measurements[i + j], window_length, delta_time));
                    }
                    builder.Append(manipulated_builder.ToString());

                    builder.Append(Environment.NewLine);
                }

                m_outStream.Write(builder.ToString());
                m_measurementCount += measurements.Length;
            }
        }


        /// <summary>
        /// Calculates moving average of <paramref name="measurement"/> and archives it.
        /// </summary>
        /// <param name="measurement">Measurements to be worked upon archived.</param>
        /// <param name="window_length">Window length</param>

        //Krishnan Edit
        public String CalculateMovingAverageInMeasurement(IMeasurement measurement, int window_length)
        {

            StringBuilder builder = new StringBuilder();
            Queue<double> measurement_queue;
            double moving_average_value;
            double measurement_initial_average_value;
            double measurement_LE_value;
            bool measurement_average_flag_value = false;
            if (!measurement_dictionary.TryGetValue(measurement.ID, out measurement_queue))
            {
                measurement_queue = new Queue<double>();
                measurement_dictionary.Add(measurement.ID, measurement_queue);
            }

            if (!measurement_average_dictionary.TryGetValue(measurement.ID, out moving_average_value))
            {
                measurement_average_dictionary.Add(measurement.ID, moving_average_value);
            }
            if (!measurement_initial_average_dictionary.TryGetValue(measurement.ID, out measurement_initial_average_value))
            {
                measurement_initial_average_dictionary.Add(measurement.ID, measurement_initial_average_value);
            }
            if (!measurement_LE_dictionary.TryGetValue(measurement.ID, out measurement_LE_value))
            {
                measurement_LE_dictionary.Add(measurement.ID, measurement_LE_value);
            }
            if (!measurement_average_flag_dictionary.TryGetValue(measurement.ID, out measurement_average_flag_value))
            {
                measurement_average_flag_dictionary.Add(measurement.ID, measurement_average_flag_value);
            }

            measurement_queue.Enqueue(measurement.AdjustedValue);
            if (measurement_queue.Count < window_length)
            {
                builder.Append(",");
                builder.Append("NA - Less");
                measurement_average_dictionary[measurement.ID] = 0;
                measurement_initial_average_dictionary[measurement.ID] = 0;
                measurement_LE_dictionary[measurement.ID] = 0;
                measurement_average_flag_dictionary[measurement.ID] = false;
                //measurement_average_flag_value = false;

            }
            if (measurement_queue.Count == window_length)
            {
                if (measurement_average_flag_dictionary[measurement.ID] == false)
                {
                    measurement_average_dictionary[measurement.ID] = measurement_queue.ToArray().Average();
                    measurement_initial_average_dictionary[measurement.ID] = measurement_average_dictionary[measurement.ID];
                    measurement_average_flag_dictionary[measurement.ID] = true;
                    measurement_LE_dictionary[measurement.ID] = 0;
                    builder.Append(',');
                    //builder.Append(measurement_LE_dictionary[measurement.ID]);//appending the LE to the string
                    builder.Append(measurement_average_dictionary[measurement.ID]);// appending the moving average to the string
                    //measurement_queue.Dequeue();
                }
            }
            if (measurement_queue.Count == window_length + 1)// the '+1' is important to ensure we do not discard the old value before subtracting
            {
                if (measurement_average_flag_dictionary[measurement.ID] == true)
                {
                    //Moving average getting calulcated here
                    measurement_average_dictionary[measurement.ID] = measurement_average_dictionary[measurement.ID] + (measurement.AdjustedValue - measurement_queue.Dequeue()) / window_length;
                    measurement_LE_dictionary[measurement.ID] = measurement_average_dictionary[measurement.ID] - measurement_initial_average_dictionary[measurement.ID];
                    builder.Append(',');
                    //builder.Append(measurement_LE_dictionary[measurement.ID]);//appending the LE to the string
                    builder.Append(measurement_average_dictionary[measurement.ID]);// appending the moving average to the string

                    //Trigger only if the measurement value is < the threshold.
                    //First Measurement Key
                    //measurement.Key.ID.Equals(FirstMeasurementKey)
                    if (measurement_average_dictionary[FirstMeasurementKey] < ThresholdTriggerValue)
                        builder.Append(',').Append(callPythonScript(ChangingParameter1, ChangingParameter2));


                }
                else
                {
                    builder.Append(',');
                    builder.Append("test");
                }


            }
            if (measurement_queue.Count > window_length + 1)
            {
                builder.Append(",");
                builder.Append("NA - More");
            }

            return builder.ToString();
        }

        //Amar Edit
        public String CalculateLEInMeasurement(IMeasurement measurement, int window_length, double delta_time)
        {

            StringBuilder builder = new StringBuilder();
            Queue<double> measurement_queue;
            Queue<double> measurement_log_voltage_diff_queue;
            double moving_average_value;
            double measurement_LE_initial_value;
            double measurement_LE_value;
            double LE_iteration_value = 1;
            double Appending_term = 0;
            bool measurement_LE_start_flag_value = false;
            bool measurement_LE_window_over_flag_value = false;
            if (!measurement_dictionary.TryGetValue(measurement.ID, out measurement_queue))
            {
                measurement_queue = new Queue<double>();
                measurement_dictionary.Add(measurement.ID, measurement_queue);
            }
            if (!measurement_log_voltage_diff_dictionary.TryGetValue(measurement.ID, out measurement_log_voltage_diff_queue))
            {
                measurement_log_voltage_diff_queue = new Queue<double>();
                measurement_log_voltage_diff_dictionary.Add(measurement.ID, measurement_log_voltage_diff_queue);
            }

            if (!measurement_average_dictionary.TryGetValue(measurement.ID, out moving_average_value))
            {
                measurement_average_dictionary.Add(measurement.ID, moving_average_value);
            }
            if (!measurement_LE_initial_dictionary.TryGetValue(measurement.ID, out measurement_LE_initial_value))
            {
                measurement_LE_initial_dictionary.Add(measurement.ID, measurement_LE_initial_value);
            }
            if (!measurement_LE_dictionary.TryGetValue(measurement.ID, out measurement_LE_value))
            {
                measurement_LE_dictionary.Add(measurement.ID, measurement_LE_value);
            }
            if (!LE_iteration_dictionary.TryGetValue(measurement.ID, out LE_iteration_value))
            {
                LE_iteration_dictionary.Add(measurement.ID, LE_iteration_value);
            }
            if (!measurement_LE_start_flag_dictionary.TryGetValue(measurement.ID, out measurement_LE_start_flag_value))
            {
                measurement_LE_start_flag_dictionary.Add(measurement.ID, measurement_LE_start_flag_value);
            }
            if (!measurement_LE_window_over_flag_dictionary.TryGetValue(measurement.ID, out measurement_LE_window_over_flag_value))
            {
                measurement_LE_window_over_flag_dictionary.Add(measurement.ID, measurement_LE_window_over_flag_value);
            }

            // The numbers 10000 and 0.001 may be changed
            if (measurement_queue.Count == window_length)
            {
                //measurement_log_voltage_diff_queue.Enqueue((Math.Log(Math.Abs(measurement_queue.Peek()-measurement_queue.ElementAt(2))/10000+0.001)));
                if (((measurement_queue.ElementAt(window_length) - measurement_queue.ElementAt(window_length - 1)) > 4000) & measurement_LE_start_flag_dictionary[measurement.ID] == false)
                {
                    measurement_LE_start_flag_dictionary[measurement.ID] = true;
                }
                else
                {
                    measurement_LE_start_flag_dictionary[measurement.ID] = false;

                }
            }
            if (measurement_queue.Count == window_length && measurement_LE_start_flag_dictionary[measurement.ID] == true)
            {
                Appending_term = ((Math.Log(Math.Abs(measurement_queue.ElementAt(window_length) - measurement_queue.ElementAt(window_length - 1)) / 10000 + 0.001))); ;
                if (((measurement_queue.ElementAt(2) - measurement_queue.ElementAt(1)) > 4000) && measurement_LE_window_over_flag_dictionary[measurement.ID] == false)
                {
                    measurement_LE_window_over_flag_dictionary[measurement.ID] = true;
                }
                else
                {
                    measurement_LE_window_over_flag_dictionary[measurement.ID] = false;
                    LE_iteration_dictionary[measurement.ID] = 1;
                }

                if (measurement_log_voltage_diff_queue.Count < window_length)
                {
                    builder.Append(",");
                    builder.Append("LE Window - Small");
                    measurement_log_voltage_diff_queue.Enqueue(Appending_term);
                    if (measurement_log_voltage_diff_queue.Count == window_length)
                    {
                        measurement_LE_initial_dictionary[measurement.ID] = measurement_log_voltage_diff_queue.ToArray().Average();
                        measurement_LE_dictionary[measurement.ID] = measurement_LE_initial_dictionary[measurement.ID];
                    }
                    else
                    {
                        measurement_LE_initial_dictionary[measurement.ID] = 0;
                        measurement_LE_dictionary[measurement.ID] = 0;
                    }

                    //measurement_average_flag_value = false;

                }

                else if (measurement_log_voltage_diff_queue.Count == window_length)
                {
                    measurement_log_voltage_diff_queue.Enqueue(Appending_term);
                    if (measurement_LE_window_over_flag_dictionary[measurement.ID] == false)
                    {
                        //Moving LE initial window getting updated here
                        measurement_LE_initial_dictionary[measurement.ID] = measurement_LE_initial_dictionary[measurement.ID] + (Appending_term - measurement_log_voltage_diff_queue.Dequeue()) / window_length;
                        measurement_LE_dictionary[measurement.ID] = measurement_LE_initial_dictionary[measurement.ID];
                        builder.Append(",");
                        builder.Append("LE not activated");
                        //builder.Append(measurement_LE_dictionary[measurement.ID]);//appending the LE to the string
                        //builder.Append(measurement_average_dictionary[measurement.ID]);// appending the moving average to the string

                        //Trigger only if the measurement value is < the threshold.
                        //First Measurement Key
                        //measurement.Key.ID.Equals(FirstMeasurementKey)
                        //if ( measurement_average_dictionary[FirstMeasurementKey] < ThresholdTriggerValue)
                        //    builder.Append(',').Append(callPythonScript(ChangingParameter1, ChangingParameter2));


                    }
                    else
                    {
                        //Moving LE calculation window getting updated here
                        measurement_LE_dictionary[measurement.ID] = (measurement_LE_dictionary[measurement.ID] + (Appending_term - measurement_log_voltage_diff_queue.Dequeue()) / window_length) - measurement_LE_initial_dictionary[measurement.ID];
                        builder.Append(',');
                        builder.Append(measurement_LE_dictionary[measurement.ID] / delta_time / LE_iteration_dictionary[measurement.ID]);//appending the LE to the string
                        LE_iteration_dictionary[measurement.ID] = LE_iteration_dictionary[measurement.ID] + 1;
                        //builder.Append(measurement_average_dictionary[measurement.ID]);//
                        //builder.Append(',');
                        //builder.Append("test");
                    }


                }
                else if (measurement_log_voltage_diff_queue.Count > window_length + 1)
                {
                    builder.Append(",");
                    builder.Append("NA - More");
                }

            }
            if (LE_iteration_dictionary[measurement.ID] > 4000)
            {
                measurement_log_voltage_diff_queue.Clear();
                measurement_LE_start_flag_dictionary[measurement.ID] = false;
                LE_iteration_dictionary[measurement.ID] = 0;
                measurement_LE_dictionary[measurement.ID] = 0;
                measurement_LE_initial_dictionary[measurement.ID] = 0;
            }
            return builder.ToString();
        }
        /// <summary>
        ///
        /// </summary>
        /// <param name="measurements"></param>
        /// <returns></returns>
        private string WriteHeader(IMeasurement[] measurements)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Timestamp");

            MeasurementKey[] unique_measurement_keys = measurements.Select(m => m.Key).Distinct().ToArray();

            foreach (MeasurementKey key in unique_measurement_keys)
                builder.Append(",").Append(key);

            //To be included-

            foreach (MeasurementKey key in unique_measurement_keys)
                builder.Append(",").Append(key).Append("-").Append("Moving Average").Append(",").Append(key).Append("-").Append("LE Calculated");


            builder.Append(Environment.NewLine);
            header_written = true;

            return builder.ToString();

        }

        //Location of Python
        private string PythonExecutable;// = @"C:\Python27\python.exe";

        //Location of Python File
        private string PythonFileToCall; // = "E:\\Sample.py";

        private Process PythonProcess;

        public string callPythonScript(double x, double y)
        {

            //Arguments to be passed to the python script- x,y

            //Process Start Information for the Python Process
            ProcessStartInfo PythonProcessStartInfo = new ProcessStartInfo(PythonExecutable);

            //we need to access the standard output
            PythonProcessStartInfo.UseShellExecute = false;
            PythonProcessStartInfo.RedirectStandardOutput = true;
            PythonProcessStartInfo.RedirectStandardError = true;

            //For later implementation
            /*PythonProcessStartInfo.Verb = "runas";
 
            PythonProcessStartInfo.UserName = "";
 
            PythonProcessStartInfo.Password = new SecureString({'d','s'}, 4)*/


            //Arguments
            PythonProcessStartInfo.Arguments = PythonFileToCall + " " + x + " " + y;

            if (PythonProcess != null)
            {
                if (!PythonProcess.HasExited)
                    return "The previous execution is yet to complete";
                else if (PythonProcess.HasExited)
                {
                    string Output = PythonProcess.StandardOutput.ReadLine();
                    PythonProcess.Close(); //Free the memory in C# context
                    PythonProcess.Dispose(); // Free the memory in System context
                    PythonProcess = null;
                    return Output;
                }
            }

            //The process
            PythonProcess = new Process();

            PythonProcess.StartInfo = PythonProcessStartInfo;

            //Start The process
            PythonProcess.Start();

            return "Python Script Called";
        }

        #endregion
    }
}