namespace CameraServer.Shared.DTO;

/// <summary>
/// Standard API response wrapper for consistent error handling
/// </summary>
/// <typeparam name="T">Type of data being returned</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ValidationErrors { get; set; } = new();

    public static ApiResponse<T> SuccessResponse(T data)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data
        };
    }

    public static ApiResponse<T> ErrorResponse(string errorMessage)
    {
        return new ApiResponse<T>
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }

    public static ApiResponse<T> ValidationErrorResponse(List<string> validationErrors)
    {
        return new ApiResponse<T>
        {
            Success = false,
            ValidationErrors = validationErrors
        };
    }
}
