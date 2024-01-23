# Durable Functions Orchestrator

Este é um exemplo de uma Azure Durable Function Orchestrator para processar pedidos com etapas como pedido, pagamento, aprovação, processamento e envio.

## Início Rápido

1. **Configuração Local**

   - Instale as ferramentas necessárias, como o [Azure Functions Core Tools](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=windows%2Ccsharp%2Cbash).
   - Adicione um arquivo local.settings.json com as variáveis da Azure Function criada, como no exemplo abaixo:
  
```
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "SUA-URL-AQUI",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet"
  }
}
```
2. **Testar com HTTP**

   Faça uma requisição com o seguinte cURL:
```
curl --location 'http://localhost:7281/api/DurableFunctionsOrchestration_HttpStart' \
--header 'Content-Type: application/json' \
--data '{
    "ProductName" : "ProdutoBonito",
    "Quantity" : 3,
    "UnitPrice" : 29.00
}'
```

## Enviando solicitação de aprovação do pedido

Durante o passo de Approval, a Durable Function aguarda por 1 minuto o envio de uma request via Webhook para o evento "ApprovalEvent", sendo possível pegar esse link utilizando o Webhook de "statusQueryGetUri" que está presente no retono da primeira requisição realizada para a DurableFunction.

![statusQueryGetUri](URL da imagem)

Exemplo do cURL recebido no momento em que a Durable Function espera pela resposta do evento:
```
curl -d 'true' http://localhost:7071/runtime/webhooks/durabletask/instances/{instancia}/raiseEvent/ApprovalEvent -H 'Content-Type: application/json'
```
