namespace MailListenerWorker.Models;

public class DepartmentMappingConfig
{
    public List<DepartmentMapping> Departments { get; set; } = [];
    public string DefaultAssignee { get; set; } = string.Empty;
}

public class DepartmentMapping
{
    public List<string> Keywords { get; set; } = [];
    public string Assignee { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
