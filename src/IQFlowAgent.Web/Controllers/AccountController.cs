using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using IQFlowAgent.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IQFlowAgent.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthSettingsService _authSettings;
    private readonly ILdapAuthService _ldapAuth;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuthSettingsService authSettings,
        ILdapAuthService ldapAuth)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _authSettings = authSettings;
        _ldapAuth = ldapAuth;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "UserManagement");
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var settings = await _authSettings.GetSettingsAsync();

        if (settings.AuthMode == "LDAP")
        {
            var ldapOk = await _ldapAuth.AuthenticateAsync(model.Username, model.Password, settings);
            if (!ldapOk)
            {
                ModelState.AddModelError(string.Empty, "Invalid credentials.");
                return View(model);
            }

            var user = await _userManager.FindByNameAsync(model.Username);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = model.Username,
                    Email = $"{model.Username}@ldap.local",
                    FullName = model.Username,
                    IsActive = true,
                    EmailConfirmed = true
                };
                await _userManager.CreateAsync(user);
                await _userManager.AddToRoleAsync(user, "User");
            }
            user.LastLogin = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            await _signInManager.SignInAsync(user, model.RememberMe);
        }
        else
        {
            var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, model.RememberMe, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }
            var user = await _userManager.FindByNameAsync(model.Username);
            if (user != null)
            {
                user.LastLogin = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
            }
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "UserManagement");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }
}
