namespace FullTime.Api.Models;

public enum DevicePlatform
{
    Android,
    iOS,
}

public class DeviceToken
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public User? User { get; set; }
    public required string Token { get; set; }
    public DevicePlatform Platform { get; set; }
    public DateTime CreatedAt { get; set; }
}
