using Easy2AcquireCom;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace NoraxonService.Services
{
    public class NoraxonService : Noraxon.NoraxonBase
    {
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
    }
}
