using DataProcessingOrchestrator;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Company.Function
{
    public static class DurableFunctionsOrchestration
    {
        [FunctionName("DurableFunctionsOrchestration_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string durableUrl = req.RequestUri.Authority;

            string body = await req.Content.ReadAsStringAsync();
            InputOrderModel data = JsonConvert.DeserializeObject<InputOrderModel>(body);

            Tuple<string, InputOrderModel> tuple = new Tuple<string, InputOrderModel>(durableUrl, data);

            string instanceId = await starter.StartNewAsync("DurableFunctionsOrchestration", tuple);

            log.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("DurableFunctionsOrchestration")]
        public static async Task<string> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log
            )
        {
            Tuple<string, InputOrderModel> inputTuple = context.GetInput<Tuple<string, InputOrderModel>>();

            InputOrderModel inputData = inputTuple.Item2;

            string durableUrl = inputTuple.Item1;

            if (!ValidateOrder(inputData))
            {
                return "Order is not valid";
            }

            var processingData = new ProcessingData(inputData);

            foreach (ProcessingStepEnum step in ProcessingSteps.Steps)
            {
                processingData.Step = step;

                switch (step)
                {
                    case ProcessingStepEnum.OrderRequest:
                        processingData = await context.CallActivityAsync<ProcessingData>(nameof(OrderRequestActivity), processingData);
                        break;

                    case ProcessingStepEnum.Payment:
                        processingData = await context.CallActivityAsync<ProcessingData>(nameof(PaymentActivity), processingData);
                        break;

                    case ProcessingStepEnum.Approval:

                        using (CancellationTokenSource timeoutCts = new CancellationTokenSource())
                        {
                            DateTime dueTime = context.CurrentUtcDateTime.AddMinutes(1);
                            Task durableTimeout = context.CreateTimer(dueTime, timeoutCts.Token);

                            Task<bool> approvalEvent = context.WaitForExternalEvent<bool>("ApprovalEvent");

                            var approvalStatus = new
                            {
                                Name = "DurableFunctionsOrchestration - Awaiting Approval:",
                                Url = @$"curl -d 'true' http://{durableUrl}/runtime/webhooks/durabletask/instances/{context.InstanceId}/raiseEvent/ApprovalEvent -H 'Content-Type: application/json'"
                            };

                            context.SetCustomStatus(approvalStatus);

                            if (approvalEvent == await Task.WhenAny(approvalEvent, durableTimeout) && approvalEvent.Result)
                            {
                                timeoutCts.Cancel();
                                processingData.IsApproved = true;
                                log.LogInformation($"Approval received for order {processingData.OrderNumber}");
                            }
                            else
                            {
                                processingData.IsApproved = false;
                                log.LogInformation($"Order {processingData.OrderNumber} approval window expired");
                            }
                        }

                        Task.WaitAll();
                        break;

                    case ProcessingStepEnum.ProcessOrder:
                        processingData = await context.CallActivityAsync<ProcessingData>(nameof(ProcessOrderActivity), processingData);
                        break;

                    case ProcessingStepEnum.SendOrder:
                        processingData = await context.CallActivityAsync<ProcessingData>(nameof(SendOrderActivity), processingData);
                        break;
                }
            }

            return processingData.ToString();
        }

        [FunctionName(nameof(OrderRequestActivity))]
        public static async Task<ProcessingData> OrderRequestActivity(
            [ActivityTrigger] ProcessingData processingData,
            ILogger log)
        {
            log.LogInformation($"Order request received: {processingData.ProductName} - {processingData.Quantity} - {processingData.UnitPrice}");
            processingData.OrderNumber = new Random().Next(1000, 9999);
            return processingData;
        }

        [FunctionName(nameof(PaymentActivity))]
        public static async Task<ProcessingData> PaymentActivity(
            [ActivityTrigger] ProcessingData processingData,
            ILogger log)
        {
            log.LogInformation($"Payment received for order {processingData.OrderNumber}");
            processingData.TotalPaid = processingData.Quantity * processingData.UnitPrice;
            return processingData;
        }

        [FunctionName(nameof(ProcessOrderActivity))]
        public static async Task<ProcessingData> ProcessOrderActivity(
            [ActivityTrigger] ProcessingData processingData,
            ILogger log)
        {
            log.LogInformation($"Order {processingData.OrderNumber} is being processed");
            processingData.IsOrderProcessed = true;
            processingData.LastUpdate = DateTime.Now;
            return processingData;
        }

        [FunctionName(nameof(SendOrderActivity))]
        public static async Task<ProcessingData> SendOrderActivity(
            [ActivityTrigger] ProcessingData processingData,
            ILogger log)
        {
            log.LogInformation($"Order {processingData.OrderNumber} is being sent");
            processingData.IsOrderSent = true;
            processingData.LastUpdate = DateTime.Now;
            return processingData;
        }

        public static bool ValidateOrder(InputOrderModel inputData)
        {
            if (string.IsNullOrEmpty(inputData.ProductName) || inputData.Quantity <= 0 || inputData.UnitPrice <= 0)
            {
                return false;
            }

            return true;
        }
    }
}
