using Microsoft.AspNetCore.Identity;

namespace WorkshopOS.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
