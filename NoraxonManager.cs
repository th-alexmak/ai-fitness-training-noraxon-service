using System.Runtime.InteropServices;
using Easy2AcquireCom;

namespace NoraxonService
{
    public class NoraxonManager
    {
        private const int LOCK_TIMEOUT = 500;

        public static NoraxonManager Instance
        {
            get
            {
                lock (instanceLock)
                {
                    instance ??= new NoraxonManager();
                }

                return instance;
            }
        }

        public string LastErrorMessage => deviceManager.GetLastErrorText();

        /// <summary>
        /// Launch Noraxon's hardware setup screen.
        /// </summary>
        public void LaunchSetup()
        {
            ThreadSafeOperation(() => deviceManager.Setup(0));
        }

        /// <summary>
        /// Retrieve available serial numbers of EMG sensors.
        /// </summary>
        /// <returns>Serial Numbers</returns>
        public string[] GetEmgSerialNumbers()
        {
            return ThreadSafeOperation(() =>
                                       {
                                           var serialNumbers = new List<string>();
                                           var device        = deviceManager.GetCurrentDevice();
                                           device.SetComponentFilterTags("type.input.analog.emg");

                                           for (var i = 0; i < device.GetComponentCount(); i++)
                                           {
                                               var component = device.GetComponent(i);
                                               serialNumbers.Add(ExtractSerialNumber(component));
                                           }

                                           return serialNumbers.ToArray();
                                       });
        }

        public Task StreamData(IEnumerable<string> serialNumbers, Func<Dictionary<string, double[]>, Task> dataCallback,
                               CancellationToken   cancellationToken)
        {
            return ThreadSafeOperation<Task>(async () =>
                                             {
                                                 var device = deviceManager.GetCurrentDevice();
                                                 var emgComponents = GetEmgComponents(device)
                                                    .Where(pair => serialNumbers
                                                              .Contains(pair
                                                                           .Key));

                                                 // Enable the emg sensors.
                                                 foreach (var emg in emgComponents)
                                                     while (emg.Value.Enabled() <= 0)
                                                         emg.Value.Enable();

                                                 try
                                                 {
                                                     device.Activate();
                                                 }
                                                 catch (COMException)
                                                 {
                                                     if (deviceManager.GetLastErrorText() ==
                                                         "Operation cannot be performed in state 'active'")
                                                     {
                                                         device.Stop();
                                                         device.Deactivate();
                                                     }
                                                     else
                                                         throw new COMException(deviceManager.GetLastErrorText());
                                                 }

                                                 while (!cancellationToken.IsCancellationRequested)
                                                 {
                                                     if ((device.Transfer() & (int)Transfer.TransferDataReady) != 0)
                                                     {
                                                         // Serial Number => Quants
                                                         Dictionary<string, double[]> emgReadings = new();
                                                         foreach (var emg in emgComponents)
                                                         {
                                                             var buffer    = new double[emg.Value.GetQuantCount()];
                                                             var bufferRef = (object)buffer;
                                                             emg.Value.GetQuants(0, buffer.Length, ref bufferRef, 0);
                                                             buffer = (double[])bufferRef;
                                                             emgReadings.Add(emg.Key, buffer);
                                                         }

                                                         await dataCallback.Invoke(emgReadings);
                                                     }
                                                     else
                                                         Thread.Sleep(25);
                                                 }

                                                 device.Stop();
                                                 device.Deactivate();

                                                 // Disable the emg sensors.
                                                 foreach (var emg in emgComponents)
                                                     while (emg.Value.Enabled() > 0)
                                                         emg.Value.Disable();
                                             });
        }

        private static readonly object          instanceLock  = new();
        private static readonly object          operationLock = new();
        private static          NoraxonManager? instance;
        private                 DeviceManager   deviceManager;

        private NoraxonManager()
        {
            deviceManager = new DeviceManager();
            try
            {
                deviceManager.Initialize("");
            }
            catch (COMException exception)
            {
                if (LastErrorMessage != "Unsupported operation: Multiple initialization not supported.")
                    throw new COMException(LastErrorMessage);
            }
        }

        /// <summary>
        /// Try to recover from error.
        /// </summary>
        private void TryRecover()
        {
            switch (LastErrorMessage)
            {
                case "Device Manager not initialized yet":
                {
                    // It is possible to get "Multiple initialization not supported" error here...
                    // So far it is not known how to solve other then reboot.
                    deviceManager.Initialize("");
                    break;
                }
                case "Operation cannot be performed in state 'active'":
                {
                    var device = deviceManager.GetCurrentDevice();
                    device.Stop();
                    device.Deactivate();
                    break;
                }
                case "Operation cannot be performed in state 'inactive'":
                {
                    var device = deviceManager.GetCurrentDevice();
                    device.Activate();
                    break;
                }
            }
        }

        /// <summary>
        /// Execute action in thread-safe manner, will try to recover from errors automatically.
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="action">Action</param>
        /// <param name="trials">Number of trials</param>
        /// <returns>Result</returns>
        /// <exception cref="TimeoutException">Timeout while waiting for lock</exception>
        private T ThreadSafeOperation<T>(Func<T> action, int trials = 0)
        {
            var lockTaken = false;
            try
            {
                Monitor.TryEnter(operationLock, LOCK_TIMEOUT, ref lockTaken);
                if (lockTaken)
                {
                    return action.Invoke();
                }
                else
                    throw new TimeoutException("Noraxon system is busy.");
            }
            catch (COMException)
            {
                if (trials < 3)
                {
                    TryRecover();

                    if (lockTaken)
                    {
                        Monitor.Exit(operationLock);
                        lockTaken = false;
                    }

                    return ThreadSafeOperation(action, trials + 1);
                }
                else
                {
                    throw new COMException(deviceManager.GetLastErrorText());
                }
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(operationLock);
            }
        }

        /// <summary>
        /// Extract serial number from component.
        /// </summary>
        /// <param name="component"></param>
        /// <returns></returns>
        private static string ExtractSerialNumber(IComponent component)
        {
#if DEBUG
            const string prefix = "line.";
#else
            const string prefix = "line.noraxon_g3_";
#endif

            var tags = component.GetTags();
            foreach (string tag in tags)
            {
                if (!tag.StartsWith(prefix)) continue;
                return tag.Replace(prefix, "");
            }

            return "";
        }

        /// <summary>
        /// Get Emg Components.
        /// </summary>
        /// <param name="device">Device</param>
        /// <returns>Emg Components</returns>
        private Dictionary<string, IAnalogInput> GetEmgComponents(Device device)
        {
            var result = new Dictionary<string, IAnalogInput>();
            device.SetComponentFilterTags("type.input.analog");
            for (var i = 0; i < device.GetComponentCount(); i++)
            {
                var component    = (IAnalogInput)device.GetComponent(i);
                var serialNumber = ExtractSerialNumber(component);
                result.Add(serialNumber, component);
            }

            return result;
        }
    }
}