using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PacketPeek;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options);
