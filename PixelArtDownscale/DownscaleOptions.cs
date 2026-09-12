namespace PixelArtDownscale;

public enum BlockSelectionMode
{
    Automatic,
    Manual
}

public enum PaletteKind
{
    None,
    DB16,
    DB32,
    NES,
    GameBoy,
    Step
}

public enum QuantizationMethod
{
    MedianCut,
    KMeansLab,
    KMeansLinear
}

public enum CropHorizontalAlignment
{
    Center,
    Left,
    Right
}

public enum CropVerticalAlignment
{
    Center,
    Top,
    Bottom
}

public sealed class ManualBlockCriteria
{
    public double TargetBrightness { get; init; } = 0.5;
    public double TargetContrast { get; init; } = 0.5;
    public double TargetSaturation { get; init; } = 0.5;
    public double TargetEdge { get; init; } = 0.5;

    public double BrightnessImportance { get; init; } = 1.0;
    public double ContrastImportance { get; init; } = 1.0;
    public double SaturationImportance { get; init; } = 1.0;
    public double EdgeImportance { get; init; } = 1.0;
}

public sealed class DownscaleOptions
{
    public const int MaxQuantizationColors = 4096;

    public bool SpriteMode { get; init; }
    public int AlphaThreshold { get; init; } = 50;
    public int TargetWidth { get; init; } = 48;
    public int TargetHeight { get; init; } = 48;
    public CropHorizontalAlignment CropHorizontal { get; init; } = CropHorizontalAlignment.Center;
    public CropVerticalAlignment CropVertical { get; init; } = CropVerticalAlignment.Center;
    public PaletteKind Palette { get; init; } = PaletteKind.DB16;
    public int PaletteStep { get; init; } = 32;
    public bool EnableDithering { get; init; }
    public int QuantizationColors { get; init; } = 64;
    public bool UseColorWeights { get; init; }
    public QuantizationMethod Quantization { get; init; } = QuantizationMethod.KMeansLab;
    public int ThreadCount { get; init; } = Environment.ProcessorCount;
    public BlockSelectionMode BlockMode { get; init; } = BlockSelectionMode.Automatic;
    public ManualBlockCriteria? ManualCriteria { get; init; }
}
