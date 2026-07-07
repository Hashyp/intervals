using Microsoft.Extensions.Options;

namespace Intervals.Api.Auth;

public sealed class PasswordPolicy
{
    private readonly PasswordOptions _options;

    public PasswordPolicy(IOptions<AuthOptions> options)
    {
        _options = options.Value.Password;
    }

    public bool IsValid(string? password, out string failureCode)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < _options.MinLength
            || password.Length > _options.MaxLength)
        {
            failureCode = AuthResultCodes.WeakPassword;
            return false;
        }

        failureCode = string.Empty;
        return true;
    }
}
