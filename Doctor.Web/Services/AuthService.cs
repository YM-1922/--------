using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Doctor.Web.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface IAuthService
{
    Task<User?> AuthenticateAsync(string username, string password);
    Task<bool> HasPermissionAsync(int userId, string permissionCode);
    Task<List<string>> GetUserPermissionCodesAsync(int userId);
    Task<User?> GetUserByIdAsync(int userId);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;

    public AuthService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null || user.Status != UserStatus.Active)
        {
            return null;
        }

        if (!PasswordHasher.VerifyPassword(user.PasswordHash, password))
        {
            return null;
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return user;
    }

    public async Task<bool> HasPermissionAsync(int userId, string permissionCode)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return false;

        // Admin has all permissions
        if (user.Role.Name == "Admin") return true;

        return await _context.RolePermissions
            .Include(rp => rp.Permission)
            .AnyAsync(rp => rp.RoleId == user.RoleId && rp.Permission.Code == permissionCode);
    }

    public async Task<List<string>> GetUserPermissionCodesAsync(int userId)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return new List<string>();

        if (user.Role.Name == "Admin")
        {
            return await _context.Permissions.Select(p => p.Code).ToListAsync();
        }

        return await _context.RolePermissions
            .Where(rp => rp.RoleId == user.RoleId)
            .Select(rp => rp.Permission.Code)
            .ToListAsync();
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        if (!PasswordHasher.VerifyPassword(user.PasswordHash, currentPassword))
        {
            return false;
        }

        user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        await _context.SaveChangesAsync();
        return true;
    }
}
