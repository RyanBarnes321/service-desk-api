namespace ServiceDesk.Core.Security;

public enum PasswordVerificationOutcome
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2
}
