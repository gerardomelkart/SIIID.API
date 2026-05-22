using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
}