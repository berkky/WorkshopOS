using Microsoft.AspNetCore.Identity;

namespace WorkshopOS.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ApplicationRole(string roleName)
        : this()
    {
        Name = roleName;
    }
}
