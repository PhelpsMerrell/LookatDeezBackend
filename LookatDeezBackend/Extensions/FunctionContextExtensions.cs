using Microsoft.Azure.Functions.Worker;

namespace LookatDeezBackend.Extensions
{
    public static class FunctionContextExtensions
    {
        public static string GetUserId(this FunctionContext context)
        {
            if (context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is string userId)
            {
                return userId;
            }
            throw new UnauthorizedAccessException("User ID not found in context");
        }
    }
}
