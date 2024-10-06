﻿using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PRN231.ExploreNow.BusinessObject.Entities;
using PRN231.ExploreNow.BusinessObject.Enums;
using PRN231.ExploreNow.BusinessObject.Models.Response.Auth;
using PRN231.ExploreNow.BusinessObject.Utilities;
using PRN231.ExploreNow.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PRN231.ExploreNow.Services.Services;

public class AuthService : IAuthService

{
    private readonly IConfiguration _configuration;
    private readonly EmailVerify _emailVerify;
    private readonly IConfigurationSection _googleSettings;
    private readonly IConfigurationSection _jwtSettings;
    private readonly ILogger<AuthService> _logger;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ILogger<AuthService> logger,
    IConfiguration configuration,
    EmailVerify emailVerify,
    TokenGenerator tokenGenerator
)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _configuration = configuration;
        _jwtSettings = _configuration.GetSection("JWT");
        _googleSettings = _configuration.GetSection("GoogleAuthSettings");
        _logger = logger;
        _emailVerify = emailVerify;
    }

    public async Task<AuthResponse> SeedRolesAsync()
    {
        var isOwnerRoleExists = await _roleManager.RoleExistsAsync(StaticUserRoles.MODERATOR);
        var isAdminRoleExists = await _roleManager.RoleExistsAsync(StaticUserRoles.ADMIN);
        var isUserRoleExists = await _roleManager.RoleExistsAsync(StaticUserRoles.CUSTOMER);

        if (isOwnerRoleExists && isAdminRoleExists && isUserRoleExists)
            return new AuthResponse
            {
                IsSucceed = true,
                Token = "Roles Seeding is Already Done"
            };

        await _roleManager.CreateAsync(new IdentityRole(StaticUserRoles.CUSTOMER));
        await _roleManager.CreateAsync(new IdentityRole(StaticUserRoles.ADMIN));
        await _roleManager.CreateAsync(new IdentityRole(StaticUserRoles.MODERATOR));

        return new AuthResponse
        {
            IsSucceed = true,
            Token = "Role Seeding Done Successfully"
        };
    }
    public async Task<AuthResponse> RegisterAsync(RegisterResponse registerResponse)
    {
        var isExistsUser = await _userManager.FindByNameAsync(registerResponse.UserName);

        if (isExistsUser != null)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "UserName Already Exists"
            };

        // Check if email is already in use
        var isExistsEmail = await _userManager.FindByEmailAsync(registerResponse.Email);
        if (isExistsEmail != null)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "Email Already Exists"
            };

        if (registerResponse.Password != registerResponse.ConfirmPassword)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "The password and confirmation password do not match."
            };

        var newUser = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            FirstName = registerResponse.FirstName,
            LastName = registerResponse.LastName,
            Email = registerResponse.Email,
            UserName = registerResponse.UserName,
            SecurityStamp = Guid.NewGuid().ToString(),
            VerifyTokenExpires = DateTime.Now.AddHours(24),
            CreatedDate = DateTime.UtcNow,
        };

        var createUserResult = await _userManager.CreateAsync(newUser, registerResponse.Password);

        if (!createUserResult.Succeeded)
        {
            var errorString = "User Creation Failed Because: " +
                              string.Join(" # ", createUserResult.Errors.Select(e => e.Description));
            return new AuthResponse { IsSucceed = false, Token = errorString };
        }

        // Add a Default USER Role to all users
        await _userManager.AddToRoleAsync(newUser, StaticUserRoles.CUSTOMER);

        // Generate verification token using custom TokenGenerator
        var verificationToken = TokenGenerator.CreateRandomToken();
        newUser.VerifyToken = verificationToken;

        // Update user with verification token
        var updateUserResult = await _userManager.UpdateAsync(newUser);
        if (!updateUserResult.Succeeded)
        {
            var errorString = "User Update Failed Because: " +
                              string.Join(" # ", updateUserResult.Errors.Select(e => e.Description));
            return new AuthResponse { IsSucceed = false, Token = errorString };
        }

        // Send verification email
        var emailSent = _emailVerify.SendVerifyAccountEmail(newUser.Email, verificationToken);
        if (!emailSent)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "Email sending failed!"
            };

        return new AuthResponse
        {
            IsSucceed = true,
            Token = "Account created successfully and check your email to verify account!"
        };
    }
    public async Task<AuthResponse> LoginAsync(LoginResponse loginResponse)
    {
        var user = await _userManager.FindByNameAsync(loginResponse.UserName);

        if (user is null)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "Invalid Credentials"
            };
        if (!user.isActived)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "Account not verified!"
            };

        var isPasswordCorrect = await _userManager.CheckPasswordAsync(user, loginResponse.Password);

        if (!isPasswordCorrect)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "Invalid Credentials"
            };

        var userRoles = await _userManager.GetRolesAsync(user);
        var role = userRoles.FirstOrDefault() ?? StaticUserRoles.CUSTOMER;

        var authClaims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.NameIdentifier, user.Id),
            new("JWTID", Guid.NewGuid().ToString()),
            new("FirstName", user.FirstName),
            new("LastName", user.LastName),
            new("email", user.Email)
        };

        foreach (var userRole in userRoles) authClaims.Add(new Claim(ClaimTypes.Role, userRole));

        var token = GenerateNewJsonWebToken(authClaims);

        return new AuthResponse { IsSucceed = true, Token = token, Role = role, UserId = user.Id, Email = user.Email };
    }
    private string GenerateNewJsonWebToken(List<Claim> claims)
    {
        var authSecret = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["JWT:Secret"])
        );
        var expires = DateTime.UtcNow.AddDays(7);

        var tokenObject = new JwtSecurityToken(
            _configuration["JWT:ValidIssuer"],
            _configuration["JWT:ValidAudience"],
            expires: DateTime.Now.AddHours(1),
            claims: claims,
            signingCredentials: new SigningCredentials(
                authSecret,
                SecurityAlgorithms.HmacSha256
            )
        );

        var token = new JwtSecurityTokenHandler().WriteToken(tokenObject);

        return token;
    }
    private SigningCredentials GetSigningCredentials()
    {
        var key = Encoding.UTF8.GetBytes(_jwtSettings.GetSection("Secret").Value);
        var secret = new SymmetricSecurityKey(key);

        return new SigningCredentials(secret, SecurityAlgorithms.HmacSha256);
    }

    private async Task<List<Claim>> GetClaims(ApplicationUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Email)
        };

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));

        return claims;
    }

    private JwtSecurityToken GenerateTokenOptions(SigningCredentials signingCredentials, List<Claim> claims)
    {
        var tokenOptions = new JwtSecurityToken(
            _jwtSettings["ValidIssuer"],
            _jwtSettings["ValidAudience"],
            claims,
            expires: DateTime.Now.AddMinutes(Convert.ToDouble(_jwtSettings["expiryInMinutes"])),
            signingCredentials: signingCredentials);

        return tokenOptions;
    }
    public async Task<string> GenerateToken(ApplicationUser user)
    {
        var signingCredentials = GetSigningCredentials();
        var claims = await GetClaims(user);
        var tokenOptions = GenerateTokenOptions(signingCredentials, claims);
        var token = new JwtSecurityTokenHandler().WriteToken(tokenOptions);

        return token;
    }
    public async Task<AuthResponse> MakeAdminAsync(
        UpdatePermissionResponse updatePermissionDto
    )
    {
        var user = await _userManager.FindByNameAsync(updatePermissionDto.UserName);

        if (user is null)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "Invalid User name!!!!!!!!"
            };
        var roles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, roles.ToArray());

        await _userManager.AddToRoleAsync(user, StaticUserRoles.ADMIN);

        return new AuthResponse
        {
            IsSucceed = true,
            Token = "User is now an ADMIN"
        };
    }

    public async Task<AuthResponse> MakeModeratorAsync(
        UpdatePermissionResponse updatePermissionDto
    )
    {
        var user = await _userManager.FindByNameAsync(updatePermissionDto.UserName);

        if (user is null)
            return new AuthResponse
            {
                IsSucceed = false,
                Token = "Invalid User name!!!!!!!!"
            };
        var roles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, roles.ToArray());

        await _userManager.AddToRoleAsync(user, StaticUserRoles.MODERATOR);

        return new AuthResponse
        {
            IsSucceed = true,
            Token = "User is now an STAFF"
        };
    }
}