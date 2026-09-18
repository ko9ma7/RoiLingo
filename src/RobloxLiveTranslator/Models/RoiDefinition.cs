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
    public string? SourceLanguageOverride { get; set; }
    public string? TargetLanguageOverride { get; set; }
    public bool ShowOverlay { get; set; } = true;
    public bool EventMode { get; set; }

    // Per-ROI game overlay presentation. Offsets are relative to the automatic
    // below/above placement and are expressed in device-independent pixels.
    public double OverlayOffsetX { get; set; }
    public double OverlayOffsetY { get; set; }
    public double OverlayWidthScale { get; set; } = 1.0;
    public double OverlayHeightScale { get; set; } = 1.0;
    // v2.1 direct overlay size in DIPs. 0 means automatic size based on the ROI/content.
    public double OverlayWidth { get; set; }
    public double OverlayHeight { get; set; }
    // Seconds to keep a completed translation visible after the source disappears. 0 = until replaced.
    public double OverlayHoldSeconds { get; set; } = 20;
    public double OverlayOpacity { get; set; } = 0.82;
    public double OverlayFontSize { get; set; } = 17;
    public bool OverlayShowSource { get; set; } = true;
    public bool OverlayShowMeta { get; set; } = true;

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
        SourceLanguageOverride = SourceLanguageOverride,
        TargetLanguageOverride = TargetLanguageOverride,
        ShowOverlay = ShowOverlay,
        EventMode = EventMode,
        OverlayOffsetX = OverlayOffsetX,
        OverlayOffsetY = OverlayOffsetY,
        OverlayWidthScale = OverlayWidthScale,
        OverlayHeightScale = OverlayHeightScale,
        OverlayWidth = OverlayWidth,
        OverlayHeight = OverlayHeight,
        OverlayHoldSeconds = OverlayHoldSeconds,
        OverlayOpacity = OverlayOpacity,
        OverlayFontSize = OverlayFontSize,
        OverlayShowSource = OverlayShowSource,
        OverlayShowMeta = OverlayShowMeta
    };
}
