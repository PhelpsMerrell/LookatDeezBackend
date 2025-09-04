using Microsoft.Azure.Cosmos;
using LookatDeezBackend.Data.Models;
using Microsoft.Extensions.Logging;
using System.Net;

namespace LookatDeezBackend.Data.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly Container _container;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(CosmosClient cosmosClient, string databaseName, ILogger<UserRepository> logger = null)
        {
            _container = cosmosClient.GetContainer(databaseName, "users");
            _logger = logger;
            
            _logger?.LogInformation("UserRepository initialized with database: {DatabaseName}, container: users", databaseName);
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
                _logger?.LogInformation("=== CreateUserAsync ===" );
                _logger?.LogInformation("Creating user - ID: {UserId}, Email: {Email}, DisplayName: {DisplayName}", 
                    user.Id, user.Email, user.DisplayName);
                    
                var response = await _container.CreateItemAsync(user, new PartitionKey(user.Id));
                
                _logger?.LogInformation("User created successfully - ID: {UserId}, Email: {Email}", user.Id, user.Email);
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                _logger?.LogWarning("User already exists - ID: {UserId}, Email: {Email}", user.Id, user.Email);
                throw new InvalidOperationException($"User with ID {user.Id} already exists", ex);
            }
            catch (CosmosException ex)
            {
                _logger?.LogError(ex, "CosmosDB error creating user - StatusCode: {StatusCode}, Message: {Message}, UserId: {UserId}", 
                    ex.StatusCode, ex.Message, user.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error creating user - UserId: {UserId}, Email: {Email}", user.Id, user.Email);
                throw;
            }
        }

        public async Task<Models.User> UpdateUserAsync(Models.User user)
        {
            try
            {
                _logger?.LogInformation("Updating user - ID: {UserId}, Friends count: {FriendCount}, Friends: [{Friends}]", 
                    user.Id, user.Friends?.Count ?? 0, user.Friends != null ? string.Join(", ", user.Friends) : "none");
                    
                var response = await _container.ReplaceItemAsync(user, user.Id, new PartitionKey(user.Id));
                
                _logger?.LogInformation("User updated successfully - ID: {UserId}", user.Id);
                return response.Resource;
            }
            catch (CosmosException ex)
            {
                _logger?.LogError(ex, "CosmosDB error updating user - StatusCode: {StatusCode}, Message: {Message}, UserId: {UserId}", 
                    ex.StatusCode, ex.Message, user.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error updating user - UserId: {UserId}", user.Id);
                throw;
            }
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