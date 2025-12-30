namespace Equibles.Sec.HostedService.Models;

public class ScrapingResult {
    public int CompaniesProcessed { get; set; }
    public int DocumentsFound { get; set; }
    public int DocumentsAdded { get; set; }
    public int DocumentsSkipped { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = [];
    public List<DeferredFiling> DeferredFilings { get; set; } = [];
    public TimeSpan Duration { get; set; }
}