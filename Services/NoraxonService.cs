using System.Runtime.InteropServices;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

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
            await OperateNoraxon(() =>
            {
                NoraxonManager.Instance.LaunchSetup();
                return Task.CompletedTask;
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
            var serialNumbers =
                await OperateNoraxon(() => Task.FromResult(NoraxonManager.Instance.GetEmgSerialNumbers()));

            var response = new GetEmgSensorsResponse();
            response.SerialNumbers.AddRange(serialNumbers);
            return response;
        }

        public override async Task StreamData(StreamDataRequest                       request,
                                              IServerStreamWriter<StreamDataResponse> responseStream,
                                              ServerCallContext                       context)
        {
            logger.LogInformation("Started streaming data.");

            Task DataCallback() => NoraxonManager.Instance.StreamData(
                request.SerialNumbers.ToArray(),
                async data =>
                {
                    var totalReadings = data.Sum(emg => emg.Value.Readings.Count);

                    logger.LogInformation($"Read {totalReadings} samples from {data.Keys.Count} emg sensors.");

                    StreamDataResponse streamDataResponse = new();
                    foreach (var emg in data)
                    {
                        streamDataResponse.EmgSensors.Add(emg.Key, emg.Value);
                    }

                    await responseStream.WriteAsync(streamDataResponse, context.CancellationToken);
                }, context.CancellationToken);

            await OperateNoraxon(DataCallback);
            logger.LogInformation("Stopped streaming data.");
        }

        private Task OperateNoraxon(Func<Task> action)
        {
            try
            {
                return action.Invoke();
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

        private Task<T> OperateNoraxon<T>(Func<Task<T>> action)
        {
            try
            {
                return action.Invoke();
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