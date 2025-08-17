using LookatDeezBackend.Data.Models;
using Microsoft.Azure.Cosmos;

namespace LookatDeezBackend.Data.Repositories
{
    public interface IUserRepository
    {
        Task<Models.User> GetUserByIdAsync(string userId);
        Task<Models.User?> GetUserByEmailAsync(string email); // Added this method
        Task<Models.User> CreateUserAsync(Models.User user);
        Task<Models.User> UpdateUserAsync(Models.User user);
        Task<List<Models.User>> SearchUsersAsync(string searchTerm);
        Task<bool> DeleteUserAsync(string userId);
        Task<bool> EmailExistsAsync(string email); // Added helper method
    }
}