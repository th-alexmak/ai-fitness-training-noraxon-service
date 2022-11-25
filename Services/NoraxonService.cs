using Easy2AcquireCom;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Runtime.InteropServices;

namespace NoraxonService.Services
{
    public class NoraxonService : Noraxon.NoraxonBase
    {
        private readonly static object locker = new object();
        DeviceManager deviceManager;

        public NoraxonService()
        {
            var lockTaken = false;

            try
            {
                Monitor.TryEnter(locker, 3000, ref lockTaken);
                if (lockTaken)
                {
                    deviceManager = new DeviceManager();
                    deviceManager.Initialize("");
                }
            }
            finally
            {
                if (lockTaken) { Monitor.Exit(locker); }
            }
        }

        public override async Task<Empty> Setup(Empty request, ServerCallContext context)
        {
            var lockTaken = false;
            try
            {
                Monitor.TryEnter(locker, 3000, ref lockTaken);
                if (lockTaken)
                {
                    deviceManager.Setup(0);
                }
            }
            catch (COMException)
            {
                throw new RpcException(new Status(StatusCode.Internal, deviceManager.GetLastErrorText()));
            }
            finally
            {
                if (lockTaken) { Monitor.Exit(locker); }
            }
            return request;
        }

        public override async Task<GetEmgSensorsResponse> GetSerialNumbers(Empty request, ServerCallContext context)
        {
            var lockTaken = false;
            var result = new GetEmgSensorsResponse();
            try
            {
                Monitor.TryEnter(locker, 3000, ref lockTaken);
                if (lockTaken)
                {
                    var device = deviceManager.GetCurrentDevice();
                    device.SetComponentFilterTags("type.input.analog.emg");

                    for (int i = 0; i < device.GetComponentCount(); i++)
                    {
                        var component = device.GetComponent(i);
                        result.SerialNumbers.Add(ExtractSerialNumber(component));
                    }
                }
            }
            catch (COMException)
            {
                throw new RpcException(new Status(StatusCode.Internal, deviceManager.GetLastErrorText()));
            }
            finally
            {
                if (lockTaken) { Monitor.Exit(locker); }
            }

            return result;
        }

        public override async Task StreamData(StreamDataRequest request, IServerStreamWriter<StreamDataResponse> responseStream, ServerCallContext context)
        {
            var lockTaken = false;
            try
            {
                Monitor.TryEnter(locker, 3000, ref lockTaken);

                if (lockTaken)
                {
                    var device = deviceManager.GetCurrentDevice();
                    var emgComponents = GetEmgComponents(device).Where((pair) =>
                    {
                        return request.SerialNumbers.Contains(pair.Key);
                    });

                    // Enable the emg sensors.
                    foreach (var emg in emgComponents)
                    {
                        while (emg.Value.Enabled() <= 0)
                        {
                            emg.Value.Enable();
                        }
                    }

                    try
                    {
                        device.Activate();
                    }
                    catch (COMException exception)
                    {
                        if (deviceManager.GetLastErrorText() == "Operation cannot be performed in state 'active'")
                        {
                            device.Stop();
                            device.Deactivate();
                        }
                        else
                        {
                            throw exception;
                        }
                    }

                    while (!context.CancellationToken.IsCancellationRequested)
                    {
                        if ((device.Transfer() & (int)Transfer.TransferDataReady) != 0)
                        {
                            StreamDataResponse streamDataResponse = new StreamDataResponse();
                            foreach (var emg in emgComponents)
                            {
                                var buffer = new double[emg.Value.GetQuantCount()];
                                emg.Value.GetQuants(0, buffer.Length, buffer, 0);

                                var sensor = new EmgSensor() { SerialNumber = emg.Key };
                                sensor.Readings.AddRange(buffer);
                                streamDataResponse.EmgSensors.Add(emg.Key, sensor);
                            }

                            await responseStream.WriteAsync(streamDataResponse, context.CancellationToken);
                        }
                        else
                        {
                            Thread.Sleep(1000);
                        }
                    }
                    device.Stop();
                    device.Deactivate();

                    // Disable the emg sensors.
                    foreach (var emg in emgComponents)
                    {
                        emg.Value.Disable();
                    }
                }
                else
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "The Noraxon System is busy."));
                }
            }
            catch (COMException)
            {
                throw new RpcException(new Status(StatusCode.Internal, deviceManager.GetLastErrorText()));
            }
            finally
            {
                if (lockTaken) { Monitor.Exit(locker); }
            }
        }

        private string ExtractSerialNumber(IComponent component)
        {
            var tags = component.GetTags();

            foreach (string tag in tags)
            {
                if (!tag.StartsWith("line.")) continue;

                return tag.Replace("line.", "");
            }

            return "";
        }

        private Dictionary<string, IAnalogInput> GetEmgComponents(Device device)
        {
            var result = new Dictionary<string, IAnalogInput>();
            device.SetComponentFilterTags("type.input.analog");
            for (int i = 0; i < device.GetComponentCount(); i++)
            {
                IAnalogInput component = (IAnalogInput)device.GetComponent(i);
                string serialNumber = ExtractSerialNumber(component);
                result.Add(serialNumber, component);
            }

            return result;
        }
    }
}