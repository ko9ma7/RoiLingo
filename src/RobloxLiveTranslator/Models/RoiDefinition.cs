namespace RobloxLiveTranslator.Models;

public sealed class RoiDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "ROI";
    public bool Enabled { get; set; } = true;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? OcrLanguagesOverride { get; set; }
    public string? TargetLanguageOverride { get; set; }
    public bool ShowOverlay { get; set; } = true;
    public bool EventMode { get; set; }

    public RoiDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        OcrLanguagesOverride = OcrLanguagesOverride,
        TargetLanguageOverride = TargetLanguageOverride,
        ShowOverlay = ShowOverlay,
        EventMode = EventMode
    };
}
