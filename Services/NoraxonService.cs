using Easy2AcquireCom;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Runtime.InteropServices;

namespace NoraxonService.Services
{
    public class NoraxonService : Noraxon.NoraxonBase
    {
        private readonly ILogger<NoraxonService> logger;


        public NoraxonService(ILogger<NoraxonService> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Launch Noraxon's Setup Screen.
        /// </summary>
        /// <param name="request">Request</param>
        /// <param name="context">Context</param>
        /// <returns>Nothing</returns>
        public override async Task<Empty> Setup(Empty request, ServerCallContext context)
        {
            OperateNoraxon<object>(() =>
            {
                NoraxonManager.Instance.LaunchSetup();
                return null;
            });
            return request;
        }

        /// <summary>
        /// Get serial numbers from Noraxon.
        /// </summary>
        /// <param name="request">Request</param>
        /// <param name="context">Context</param>
        /// <returns>Serial Numbers</returns>
        public override async Task<GetEmgSensorsResponse> GetSerialNumbers(Empty request, ServerCallContext context)
        {
            var serialNumbers = OperateNoraxon(NoraxonManager.Instance.GetEmgSerialNumbers);

            var response = new GetEmgSensorsResponse();
            response.SerialNumbers.AddRange(serialNumbers);
            return response;
        }

        public override async Task StreamData(StreamDataRequest request, IServerStreamWriter<StreamDataResponse> responseStream, ServerCallContext context)
        {
            OperateNoraxon<object>(() =>
            {
                NoraxonManager.Instance.StreamData(request.SerialNumbers.ToArray(), async (data) =>
                {
                    StreamDataResponse streamDataResponse = new();

                    foreach (var emg in data)
                    {
                        var emgSensor = new EmgSensor() { SerialNumber = emg.Key };
                        emgSensor.Readings.AddRange(emg.Value);

                        streamDataResponse.EmgSensors.Add(emg.Key, emgSensor);
                    }

                    await responseStream.WriteAsync(streamDataResponse, context.CancellationToken);
                }, context.CancellationToken);

                return null;
            });
        }

        private T? OperateNoraxon<T>(Func<T?> action)
        {
            try
            {
                return action();
            }
            catch (TimeoutException exception)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, exception.Message));
            }
            catch (COMException)
            {
                throw new RpcException(new Status(StatusCode.Internal, NoraxonManager.Instance.LastErrorMessage));
            }
        }
    }
}