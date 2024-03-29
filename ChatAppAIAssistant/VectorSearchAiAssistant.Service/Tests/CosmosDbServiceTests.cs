using NUnit.Framework;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using VectorSearchAiAssistant.Service.Interfaces;
using VectorSearchAiAssistant.Service.Models.Chat;
using VectorSearchAiAssistant.Service.Utils;

namespace VectorSearchAiAssistant.Service.Tests
{
    [TestFixture]
    public class CosmosDbServiceTests
    {
        private Mock<ICosmosDbService> _cosmosDbServiceMock;

        [SetUp]
        public void Setup()
        {
            _cosmosDbServiceMock = new Mock<ICosmosDbService>();
        }

        [Test]
        public async Task GetSessionsAsync_ShouldReturnListOfSessions()
        {
            const string tenantId = "abcd";
            const string userId = "93c578d9";
            const string sessionId = "93c578d9-d039-4b31-96b5-8992cb40e96d";

            PartitionManager.RecordQueryParams rParams = new PartitionManager.RecordQueryParams(false, tenantId, userId, sessionId, sessionId);


            // Arrange
            var expectedSessions = new List<Session> {
                new Session(rParams.tenantId,rParams.userId)
                {
                    Id = "93c578d9-d039-4b31-96b5-8992cb40e96d",
                    SessionId = "93c578d9-d039-4b31-96b5-8992cb40e96d",
                    Type = "Session",
                    TokensUsed = 5000,
                    Name = "Socks available"
                },
                new Session(rParams.tenantId,rParams.userId)
                {
                    Id = "ef7c2ee9-6679-4c5f-9007-814c82a8b615",
                    SessionId = "ef7c2ee9-6679-4c5f-9007-814c82a8b615",
                    Type = "Session",
                    TokensUsed = 1500,
                    Name = "Bike inventory"
                }
            };
            _cosmosDbServiceMock.Setup(s => s.GetSessionsAsync(rParams))
                .ReturnsAsync(expectedSessions);

            // Act
            var service = _cosmosDbServiceMock.Object;
            var sessions = await service.GetSessionsAsync(rParams);

            // Assert
            Assert.AreEqual(expectedSessions.Count, sessions.Count);
            CollectionAssert.AreEqual(expectedSessions, sessions);
        }

        [Test]
        public async Task GetSessionExistingMessagesAsync_ShouldReturnListOfMessagesForSession()
        {
            // Arrange
            const string tenantId = "abcd";
            const string userId = "93c578d9";
            const string sessionId = "93c578d9-d039-4b31-96b5-8992cb40e96d";


            PartitionManager.RecordQueryParams rParams = new PartitionManager.RecordQueryParams(false,tenantId, userId, sessionId, string.Empty);

            var expectedMessages = new List<Message>
            {
                new Message(rParams.tenantId,rParams.userId,rParams.documentId, "User", 1536, "What kind of socks do you have available?", null, null),
                new Message(rParams.tenantId,rParams.userId,rParams.documentId, "Assistant", 26, "We have two types of socks available: Racing Socks and Mountain Bike Socks. Both are available in sizes L and M.", null, null),
            };

            //PartitionManager.RecordQueryParams rParams= new PartitionManager.RecordQueryParams(false,tenantId,userId,sessionId,sessionId);

            _cosmosDbServiceMock.Setup(s => s.GetSessionExistingMessagesAsync(rParams, System.DateTime.Now))
                .ReturnsAsync(expectedMessages);

            // Act
            var service = _cosmosDbServiceMock.Object;
            var messages = await service.GetSessionExistingMessagesAsync(rParams, System.DateTime.Now);

            // Assert
            Assert.AreEqual(expectedMessages.Count, messages.Count);
            CollectionAssert.AreEqual(expectedMessages, messages);
        }
        
    }
}
