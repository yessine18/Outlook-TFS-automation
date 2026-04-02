using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MailListenerWorker.Models;

namespace MailListenerWorker.Services;

public class DepartmentRoutingService
{
    private readonly ILogger<DepartmentRoutingService> _logger;
    private readonly DepartmentMappingConfig _config;

    public DepartmentRoutingService(ILogger<DepartmentRoutingService> logger, IConfiguration configuration)
    {
        _logger = logger;

        // Load department routing configuration
        var routingConfig = new DepartmentMappingConfig();
        configuration.GetSection("Routing").Bind(routingConfig);
        _config = routingConfig;

        if (string.IsNullOrEmpty(_config.DefaultAssignee))
        {
            _logger.LogWarning("DefaultAssignee not configured in Routing settings");
        }
    }

    /// <summary>
    /// Resolves a department name to an assignee email address
    /// </summary>
    public string ResolveAssignee(string department)
    {
        if (string.IsNullOrEmpty(department))
        {
            _logger.LogInformation("No department specified, using default assignee");
            return _config.DefaultAssignee ?? "support@domain.com";
        }

        var departmentLower = department.ToLowerInvariant();

        foreach (var mapping in _config.Departments)
        {
            if (mapping.Keywords.Any(k => k.Equals(departmentLower, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("Resolved department {Department} to assignee {Assignee}", department, mapping.Assignee);
                return mapping.Assignee;
            }
        }

        _logger.LogInformation("No routing found for department {Department}, using default assignee", department);
        return _config.DefaultAssignee ?? "support@domain.com";
    }

    /// <summary>
    /// Gets the full department name (human-readable) for a department
    /// </summary>
    public string GetDepartmentName(string department)
    {
        if (string.IsNullOrEmpty(department))
            return "Support";

        var departmentLower = department.ToLowerInvariant();

        var mapping = _config.Departments.FirstOrDefault(m =>
            m.Keywords.Any(k => k.Equals(departmentLower, StringComparison.OrdinalIgnoreCase)));

        return mapping?.Name ?? department;
    }
}
