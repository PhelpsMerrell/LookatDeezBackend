using Microsoft.Azure.Cosmos;
using LookatDeezBackend.Data.Models;
using System.Net;

namespace LookatDeezBackend.Data.Repositories
{
    public class FriendRequestRepository : IFriendRequestRepository
    {
        private readonly Container _container;

        public FriendRequestRepository(CosmosClient cosmosClient, string databaseName)
        {
            _container = cosmosClient.GetContainer(databaseName, "friendrequests");
        }

        public async Task<FriendRequest> CreateFriendRequestAsync(FriendRequest friendRequest)
        {
            try
            {
                var response = await _container.CreateItemAsync(friendRequest, new PartitionKey(friendRequest.FromUserId));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException($"Friend request with ID {friendRequest.Id} already exists", ex);
            }
        }

        public async Task<FriendRequest> GetFriendRequestByIdAsync(string requestId)
        {
            try
            {
                // Since we need to search across partitions, use a cross-partition query
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                    .WithParameter("@id", requestId);

                using var iterator = _container.GetItemQueryIterator<FriendRequest>(query);
                var response = await iterator.ReadNextAsync();
                
                return response.FirstOrDefault();
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<FriendRequest> UpdateFriendRequestAsync(FriendRequest friendRequest)
        {
            var response = await _container.ReplaceItemAsync(friendRequest, friendRequest.Id, new PartitionKey(friendRequest.FromUserId));
            return response.Resource;
        }

        public async Task<bool> DeleteFriendRequestAsync(string requestId, string fromUserId)
        {
            try
            {
                await _container.DeleteItemAsync<FriendRequest>(requestId, new PartitionKey(fromUserId));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task<List<FriendRequest>> GetSentRequestsAsync(string fromUserId)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.fromUserId = @fromUserId")
                .WithParameter("@fromUserId", fromUserId);

            var results = new List<FriendRequest>();
            using var resultSet = _container.GetItemQueryIterator<FriendRequest>(query);

            while (resultSet.HasMoreResults)
            {
                var response = await resultSet.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        public async Task<List<FriendRequest>> GetReceivedRequestsAsync(string toUserId)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.toUserId = @toUserId")
                .WithParameter("@toUserId", toUserId);

            var results = new List<FriendRequest>();
            using var resultSet = _container.GetItemQueryIterator<FriendRequest>(query);

            while (resultSet.HasMoreResults)
            {
                var response = await resultSet.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        public async Task<FriendRequest> GetExistingRequestAsync(string fromUserId, string toUserId)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.fromUserId = @fromUserId AND c.toUserId = @toUserId")
                .WithParameter("@fromUserId", fromUserId)
                .WithParameter("@toUserId", toUserId);

            using var iterator = _container.GetItemQueryIterator<FriendRequest>(query);
            var response = await iterator.ReadNextAsync();
            
            return response.FirstOrDefault();
        }

        public async Task<List<FriendRequest>> GetPendingRequestsAsync(string userId)
        {
            // Get both sent and received pending requests
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE (c.fromUserId = @userId OR c.toUserId = @userId) AND c.status = @status")
                .WithParameter("@userId", userId)
                .WithParameter("@status", (int)FriendRequestStatus.Pending);

            var results = new List<FriendRequest>();
            using var resultSet = _container.GetItemQueryIterator<FriendRequest>(query);

            while (resultSet.HasMoreResults)
            {
                var response = await resultSet.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }
    }
}
