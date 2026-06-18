namespace KwaWicks.Application.DTOs;

public class SiteDto
{
    public string SiteId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public bool IsActive { get; set; }
}

public class CreateSiteRequest
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
}
