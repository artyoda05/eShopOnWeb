using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace Microsoft.eShopWeb.ApplicationCore.Services
{
    public class OrderService : IOrderService
    {
        private readonly IAsyncRepository<Order> _orderRepository;
        private readonly IUriComposer _uriComposer;
        private readonly IAsyncRepository<Basket> _basketRepository;
        private readonly IAsyncRepository<CatalogItem> _itemRepository;
        private readonly HttpClient _http = new ();

        public OrderService(IAsyncRepository<Basket> basketRepository,
            IAsyncRepository<CatalogItem> itemRepository,
            IAsyncRepository<Order> orderRepository,
            IUriComposer uriComposer)
        {
            _orderRepository = orderRepository;
            _uriComposer = uriComposer;
            _basketRepository = basketRepository;
            _itemRepository = itemRepository;
        }

        public async Task CreateOrderAsync(int basketId, Address shippingAddress)
        {
            var basketSpec = new BasketWithItemsSpecification(basketId);
            var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

            Guard.Against.NullBasket(basketId, basket);
            Guard.Against.EmptyBasketOnCheckout(basket.Items);

            var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
            var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

            var items = basket.Items.Select(basketItem =>
            {
                var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
                var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
                return orderItem;
            }).ToList();

            var order = new Order(basket.BuyerId, shippingAddress, items);

            await _orderRepository.AddAsync(order);

            await ProcessOrder(order);
            await PlaceOrderInWarehouse(order);
        }

        private async Task PlaceOrderInWarehouse(Order order)
        {
            const string connectionString = "Endpoint=sb://orderitemsreserver.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=ASRiVSBcLovumQ9T4C/DM0D+yX5ZFGqbwKWa1NEpzDc=";
            const string queueName = "orderitemsreserver";

            await using var client = new ServiceBusClient(connectionString);
            await using var sender = client.CreateSender(queueName);

            var items = order.OrderItems.Select(x => new {x.ItemOrdered.CatalogItemId, x.Units});
            var content = JsonSerializer.Serialize(items);

            using var messageBatch = await sender.CreateMessageBatchAsync();

            messageBatch.TryAddMessage(new ServiceBusMessage(content));

            await sender.SendMessagesAsync(messageBatch);
        }

        private async Task ProcessOrder(Order order)
        {
            const string azureFunctionUrl = "https://deliveryorderprocessor20211130202723.azurewebsites.net/api/processOrder";

            var processedInfo = new
            {
                Items = order.OrderItems.Select(x => new {x.ItemOrdered.CatalogItemId, x.Units}),
                Total = order.Total(),
                Address = order.ShipToAddress
            };

            var content = new StringContent(JsonSerializer.Serialize(processedInfo));

            await _http.PostAsync(azureFunctionUrl, content);
        }
    }
}
