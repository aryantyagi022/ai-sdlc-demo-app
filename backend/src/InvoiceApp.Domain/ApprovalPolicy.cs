namespace InvoiceApp.Domain;

/// <summary>
/// Who may approve what. Role limits are supplied from configuration; the two-approver
/// threshold is derived from them rather than configured separately, so the ladder can
/// never disagree with itself.
/// </summary>
public class ApprovalPolicy
{
    private readonly IReadOnlyDictionary<string, decimal> _roleLimits;

    public ApprovalPolicy(IReadOnlyDictionary<string, decimal> roleLimits, string secondApproverRole)
    {
        if (roleLimits.Count == 0)
        {
            throw new ArgumentException("At least one role limit must be configured.", nameof(roleLimits));
        }

        foreach (var (role, limit) in roleLimits)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("A role name cannot be blank.", nameof(roleLimits));
            }

            if (limit <= 0)
            {
                throw new ArgumentException(
                    $"The limit for {role} is {limit}; approval limits must be greater than zero.",
                    nameof(roleLimits));
            }
        }

        _roleLimits = new Dictionary<string, decimal>(roleLimits, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(secondApproverRole))
        {
            throw new ArgumentException(
                "The second-approver role must be configured.", nameof(secondApproverRole));
        }

        if (!_roleLimits.ContainsKey(secondApproverRole))
        {
            throw new ArgumentException(
                $"The second-approver role {secondApproverRole} has no configured limit, so no invoice " +
                $"above the dual-approval threshold could ever be approved. Known roles: " +
                $"{string.Join(", ", _roleLimits.Keys)}.",
                nameof(secondApproverRole));
        }

        SecondApproverRole = secondApproverRole;
    }

    /// <summary>The role required for the second signature on a large invoice.</summary>
    public string SecondApproverRole { get; }

    /// <summary>Above this amount one signature is never enough. The highest single-approver limit.</summary>
    public decimal DualApprovalThreshold => _roleLimits.Values.Max();

    public bool CanApprove(string role) => _roleLimits.ContainsKey(role);

    /// <summary>The most this role may approve on its own, or null if the role cannot approve at all.</summary>
    public decimal? LimitFor(string role) =>
        _roleLimits.TryGetValue(role, out var limit) ? limit : null;

    public bool IsSecondApproverRole(string role) =>
        string.Equals(role, SecondApproverRole, StringComparison.OrdinalIgnoreCase);

    public bool RequiresTwoApprovers(decimal amount) => amount > DualApprovalThreshold;
}
