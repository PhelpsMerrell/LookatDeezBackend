using Microsoft.Azure.Cosmos;
using LookatDeezBackend.Data.Models;
using System.Net;

namespace LookatDeezBackend.Data.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly Container _container;

        public UserRepository(CosmosClient cosmosClient, string databaseName)
        {
            _container = cosmosClient.GetContainer(databaseName, "users");
        }

        public async Task<Models.User> GetUserByIdAsync(string userId)
        {
            try
            {
                var response = await _container.ReadItemAsync<Models.User>(userId, new PartitionKey(userId));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<Models.User?> GetUserByEmailAsync(string email)
        {
            try
            {
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.email = @email")
                    .WithParameter("@email", email.ToLowerInvariant());

                using var iterator = _container.GetItemQueryIterator<Models.User>(query);
                var response = await iterator.ReadNextAsync();

                return response.FirstOrDefault();
            }
            catch (CosmosException)
            {
                return null;
            }
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            var user = await GetUserByEmailAsync(email);
            return user != null;
        }

        public async Task<Models.User> CreateUserAsync(Models.User user)
        {
            try
            {
                var response = await _container.CreateItemAsync(user, new PartitionKey(user.Id));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException($"User with ID {user.Id} already exists", ex);
            }
        }

        public async Task<Models.User> UpdateUserAsync(Models.User user)
        {
            var response = await _container.ReplaceItemAsync(user, user.Id, new PartitionKey(user.Id));
            return response.Resource;
        }

        public async Task<List<Models.User>> SearchUsersAsync(string searchTerm)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE CONTAINS(UPPER(c.displayName), UPPER(@searchTerm)) OR CONTAINS(UPPER(c.email), UPPER(@searchTerm))")
                .WithParameter("@searchTerm", searchTerm);

            var results = new List<Models.User>();
            using var resultSet = _container.GetItemQueryIterator<Models.User>(query);

            while (resultSet.HasMoreResults)
            {
                var response = await resultSet.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            try
            {
                await _container.DeleteItemAsync<Models.User>(userId, new PartitionKey(userId));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}