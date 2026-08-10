using Microsoft.AspNetCore.Authorization;
using WorkshopOS.Application.Customers;

namespace WorkshopOS.Infrastructure.Authorization;

public sealed class CustomerManagerRequirement : IAuthorizationRequirement;
