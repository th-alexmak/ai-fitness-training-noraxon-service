using Easy2AcquireCom;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace NoraxonService.Services
{
    public class NoraxonService : Noraxon.NoraxonBase
    {
        private readonly static object locker = new object();
        DeviceManager deviceManager;

        public NoraxonService()
        {
            deviceManager = new DeviceManager();
            deviceManager.Initialize("");
        }

        public override async Task<Empty> Setup(Empty request, ServerCallContext context)
        {
            deviceManager.Setup(0);
            return request;
        }

        public override async Task<GetEmgSensorsResponse> GetEmgSensors(Empty request, ServerCallContext context)
        {
            var result = new GetEmgSensorsResponse();
            lock (locker)
            {
                var device = deviceManager.GetCurrentDevice();
                device.SetComponentFilterTags("type.input.analog.emg");

                for (int i = 0; i < device.GetComponentCount(); i++)
                {
                    var component = device.GetComponent(i);
                    var tags = component.GetTags();

                    foreach (string tag in tags)
                    {
                        if (!tag.StartsWith("line.")) continue;

                        result.EmgSensors.Add(new EmgSensor() { SerialNumber = tag.Replace("line.", "") });
                        break;
                    }
                }
            }

            return result;
        }
    }
}