using Microsoft.AspNetCore.Identity;

namespace Compass.Api.Data;

/// <summary>
/// Compass's user. Empty on purpose: it is the stable extension point for anything a later
/// phase needs on a user, not a place Phase 0 needs a field. <c>UserName</c> is always the email.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>;
